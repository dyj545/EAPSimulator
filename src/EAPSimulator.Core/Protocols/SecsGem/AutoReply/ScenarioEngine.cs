using System.Threading.Channels;
using EAPSimulator.Core.Protocols;
using EAPSimulator.Core.Protocols.HostProtocol;
using EAPSimulator.Core.Protocols.SecsGem.Gem;
using EAPSimulator.Core.Protocols.SecsGem.Handlers;
using EAPSimulator.Core.Protocols.SecsGem.SecsII;
using Microsoft.Extensions.Logging;

namespace EAPSimulator.Core.Protocols.SecsGem.AutoReply;

/// <summary>
/// Famate-style scenario interpreter. Drives a script of heterogeneous steps
/// (Send/Receive/Reply/Delay/Log/Branch/HostSend/HostReceive/SetVariable/Loop/EndLoop/CallScenario)
/// and lets the user run/stop/single-step. Implements <see cref="ISecsMessageHandler"/> so an
/// active Receive step can consume an inbound SECS message off the router pipeline.
/// Host messages are forwarded via <see cref="OnHostMessageReceived"/>; wire it up to
/// <c>HostProtocol.MessageReceived</c>.
///
/// Variable interpolation: any string template field that supports <c>${name}</c> is
/// rendered through <see cref="ScenarioVariables.Render"/> just before send/build.
///
/// Sub-scenario calls run on a per-engine frame stack with a recursion depth bound to
/// avoid runaway A→B→A loops. Variables are shared across all frames.
/// </summary>
public class ScenarioEngine : ISecsMessageHandler
{
    /// <summary>Maximum sub-scenario nesting depth — prevents runaway recursion.</summary>
    public const int MaxCallDepth = 16;

    private readonly ILogger _logger;
    private readonly Func<string, SecsMessageTemplate?> _templateLookup;
    private readonly Func<SecsMessage, CancellationToken, Task>? _send;
    private readonly ProtocolRole _currentRole;
    private readonly Func<string, ScenarioDefinition?>? _scenarioLookup;

    // Per-channel host wiring. Key "" = default channel (used when a step omits HostChannelName).
    private readonly Dictionary<string, Func<string, HostMessageTemplate?>> _hostTemplateLookups = new();
    private readonly Dictionary<string, Func<HostMessage, CancellationToken, Task>> _hostSends = new();

    private readonly object _lock = new();
    private CancellationTokenSource? _runCts;
    private Task? _runTask;

    // Current SECS Receive step waits on this channel; the router pushes inbound SECS messages in.
    private Channel<SecsMessage>? _inbox;

    // Per-channel host inboxes; HostReceive step reads from the channel its step specifies.
    private readonly Dictionary<string, Channel<HostMessage>> _hostInboxes = new();

    // Last-received SECS message — used by a subsequent Reply step.
    private SecsMessage? _lastReceived;

    // Last-received Host message — used by a subsequent Branch evaluating Host fields.
    private HostMessage? _lastHostReceived;

    /// <summary>Variable bag shared across loops and sub-scenarios for the active run.</summary>
    private ScenarioVariables _vars = new();

    /// <summary>
    /// Tracks which side fed <see cref="_lastReceived"/> / <see cref="_lastHostReceived"/> last,
    /// so Branch knows which message to evaluate conditions against.
    /// </summary>
    private LastSource _lastSource = LastSource.None;
    private enum LastSource { None, Secs, Host }

    public event Action<ScenarioDefinition, int, ScenarioStep>? StepStarted;
    public event Action<ScenarioDefinition, int, ScenarioStep, string>? StepCompleted; // status text
    public event Action<ScenarioDefinition, string>? ScenarioFinished; // status text
    public event Action<string>? Log;

    public bool IsRunning => _runTask is { IsCompleted: false };
    public ScenarioDefinition? RunningScenario { get; private set; }
    public int CurrentStepIndex { get; private set; }

    public bool ConsumedLast { get; private set; }

    public ScenarioEngine(
        ILogger logger,
        Func<string, SecsMessageTemplate?> templateLookup,
        Func<SecsMessage, CancellationToken, Task>? send = null,
        ProtocolRole currentRole = ProtocolRole.Equipment,
        Func<string, HostMessageTemplate?>? hostTemplateLookup = null,
        Func<HostMessage, CancellationToken, Task>? hostSend = null,
        Func<string, ScenarioDefinition?>? scenarioLookup = null)
    {
        _logger = logger;
        _templateLookup = templateLookup;
        _send = send;
        _currentRole = currentRole;
        _scenarioLookup = scenarioLookup;
        // Backwards-compat: ctor lookup + send register under default channel.
        if (hostTemplateLookup != null) _hostTemplateLookups[""] = hostTemplateLookup;
        if (hostSend != null) _hostSends[""] = hostSend;
    }

    /// <summary>
    /// Register Host capability for the given channel name. Use empty string for the default
    /// channel that catches steps with no <see cref="ScenarioStep.HostChannelName"/> set.
    /// </summary>
    public void AttachHostChannel(
        string channelName,
        Func<string, HostMessageTemplate?> hostTemplateLookup,
        Func<HostMessage, CancellationToken, Task> hostSend)
    {
        var key = channelName ?? "";
        _hostTemplateLookups[key] = hostTemplateLookup;
        _hostSends[key] = hostSend;
    }

    /// <summary>Detach a host channel (e.g. when its transport disconnects).</summary>
    public void DetachHostChannel(string channelName)
    {
        var key = channelName ?? "";
        _hostTemplateLookups.Remove(key);
        _hostSends.Remove(key);
    }

    /// <summary>
    /// Backwards-compat shim — registers as the default channel.
    /// </summary>
    public void AttachHost(
        Func<string, HostMessageTemplate?> hostTemplateLookup,
        Func<HostMessage, CancellationToken, Task> hostSend)
        => AttachHostChannel("", hostTemplateLookup, hostSend);

    /// <summary>
    /// Start running the scenario from step 0. Returns immediately; observe events for progress.
    /// Refuses to run if the scenario's <see cref="ScenarioDefinition.Role"/> doesn't match
    /// the simulator's current role — Send/Receive get reversed across roles, so loading the
    /// wrong file by mistake is the typical pain point this guard catches.
    /// </summary>
    public void Start(ScenarioDefinition scenario)
    {
        lock (_lock)
        {
            if (!RoleAllows(scenario.Role, _currentRole))
            {
                var msg = $"Scenario '{scenario.Name}' authored for {scenario.Role}, " +
                          $"but this simulator runs as {_currentRole}. Refusing to start.";
                _logger.LogWarning("{Msg}", msg);
                Log?.Invoke($"⚠ {msg}");
                ScenarioFinished?.Invoke(scenario, "RoleMismatch");
                return;
            }

            if (IsRunning)
            {
                _logger.LogWarning("Cannot start '{Name}': '{Cur}' is already running",
                    scenario.Name, RunningScenario?.Name);
                return;
            }
            _runCts = new CancellationTokenSource();
            RunningScenario = scenario;
            CurrentStepIndex = 0;
            _lastReceived = null;
            _lastHostReceived = null;
            _lastSource = LastSource.None;
            _vars = new ScenarioVariables();
            _inbox = Channel.CreateUnbounded<SecsMessage>();
            // Lazily create host inboxes per channel as steps reference them.
            _hostInboxes.Clear();
            var ct = _runCts.Token;
            _runTask = Task.Run(() => RunLoopAsync(scenario, ct), ct);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _runCts?.Cancel();
        }
    }

    /// <summary>
    /// Run a one-shot scenario synchronously to completion. Wires up no role check (the caller
    /// already vetted) and exposes the final status. Used by tests.
    /// </summary>
    internal async Task<string> RunOnceAsync(ScenarioDefinition scenario, CancellationToken ct)
    {
        _vars = new ScenarioVariables();
        _inbox = Channel.CreateUnbounded<SecsMessage>();
        _hostInboxes.Clear();
        _lastReceived = null;
        _lastHostReceived = null;
        _lastSource = LastSource.None;
        RunningScenario = scenario;
        string? finishStatus = null;
        ScenarioFinished += Capture;
        try { await RunLoopAsync(scenario, ct).ConfigureAwait(false); }
        finally { ScenarioFinished -= Capture; }
        return finishStatus ?? "Completed";

        void Capture(ScenarioDefinition _, string status) => finishStatus = status;
    }

    /// <summary>
    /// Match a scenario's authored role against the simulator's current protocol role.
    /// <see cref="ScenarioRole.Any"/> matches both sides.
    /// </summary>
    public static bool RoleAllows(ScenarioRole authored, ProtocolRole current) => authored switch
    {
        ScenarioRole.Any => true,
        ScenarioRole.Host => current == ProtocolRole.Host,
        ScenarioRole.Equipment => current == ProtocolRole.Equipment,
        _ => false,
    };

    // ─── Frame stack ───

    private sealed class Frame
    {
        public required ScenarioDefinition Scenario;
        public int Pc;
        public required Dictionary<string, int> LabelIndex;
        public Dictionary<string, int> LoopStartByLoopId = new(StringComparer.Ordinal);
        public Dictionary<string, int> LoopEndByLoopId = new(StringComparer.Ordinal);
        public Stack<LoopFrame> Loops = new();
    }

    private sealed class LoopFrame
    {
        public required string LoopId;
        public int StartPc;       // pc of the Loop step
        public int EndPc;         // pc of the matching EndLoop step
        public int Iteration;     // 1-based once running
        public int Times;         // 0 = unbounded (relies on cancellation)
    }

    private static Frame BuildFrame(ScenarioDefinition scenario, ILogger logger)
    {
        var labelIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var loopStart = new Dictionary<string, int>(StringComparer.Ordinal);
        var loopEnd = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < scenario.Steps.Count; i++)
        {
            var s = scenario.Steps[i];
            if (!string.IsNullOrEmpty(s.Label) && !labelIndex.TryAdd(s.Label, i))
                logger.LogWarning("Scenario '{Name}' duplicate label '{Lbl}' at step {I}; first wins",
                    scenario.Name, s.Label, i);
            if (s.Kind == ScenarioStepKind.Loop && !string.IsNullOrEmpty(s.LoopId))
            {
                if (!loopStart.TryAdd(s.LoopId, i))
                    logger.LogWarning("Scenario '{Name}' duplicate Loop id '{Id}' at step {I}; first wins",
                        scenario.Name, s.LoopId, i);
            }
            else if (s.Kind == ScenarioStepKind.EndLoop && !string.IsNullOrEmpty(s.LoopId))
            {
                if (!loopEnd.TryAdd(s.LoopId, i))
                    logger.LogWarning("Scenario '{Name}' duplicate EndLoop id '{Id}' at step {I}; first wins",
                        scenario.Name, s.LoopId, i);
            }
        }
        return new Frame
        {
            Scenario = scenario,
            Pc = 0,
            LabelIndex = labelIndex,
            LoopStartByLoopId = loopStart,
            LoopEndByLoopId = loopEnd,
        };
    }

    private async Task RunLoopAsync(ScenarioDefinition scenario, CancellationToken ct)
    {
        try
        {
            do
            {
                var frames = new Stack<Frame>();
                frames.Push(BuildFrame(scenario, _logger));
                while (frames.Count > 0)
                {
                    var frame = frames.Peek();
                    if (frame.Pc >= frame.Scenario.Steps.Count)
                    {
                        frames.Pop();
                        if (frames.Count > 0)
                        {
                            // Returning from a sub-scenario — advance the caller past CallScenario.
                            frames.Peek().Pc++;
                        }
                        continue;
                    }

                    ct.ThrowIfCancellationRequested();
                    var step = frame.Scenario.Steps[frame.Pc];
                    // For UI: report step against the active (top) frame's scenario, so highlight
                    // follows control flow into sub-scenarios. UIs that only know the root will
                    // ignore frames they didn't load.
                    CurrentStepIndex = frame.Pc;
                    RunningScenario = frame.Scenario;
                    StepStarted?.Invoke(frame.Scenario, frame.Pc, step);
                    EmitLog($"Scenario '{frame.Scenario.Name}' step {frame.Pc}: {step.DisplayText}");

                    var (status, advance) = await DispatchAsync(frame, frames, step, ct).ConfigureAwait(false);
                    StepCompleted?.Invoke(frame.Scenario, frame.Pc, step, status);
                    if (advance) frame.Pc++;
                }
            } while (scenario.Loop && !ct.IsCancellationRequested);

            ScenarioFinished?.Invoke(scenario, "Completed");
            EmitLog($"Scenario '{scenario.Name}' completed");
        }
        catch (OperationCanceledException)
        {
            ScenarioFinished?.Invoke(scenario, "Stopped");
            EmitLog($"Scenario '{scenario.Name}' stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scenario '{Name}' failed", scenario.Name);
            ScenarioFinished?.Invoke(scenario, $"Failed: {ex.Message}");
            EmitLog($"Scenario '{scenario.Name}' FAILED: {ex.Message}");
        }
        finally
        {
            lock (_lock)
            {
                RunningScenario = null;
                _inbox?.Writer.TryComplete();
                _inbox = null;
                foreach (var inbox in _hostInboxes.Values)
                    inbox.Writer.TryComplete();
                _hostInboxes.Clear();
            }
        }
    }

    /// <summary>
    /// Execute one step. Returns (statusText, advancePc). Steps that manage their own PC
    /// (Branch / Loop / EndLoop / CallScenario) return advancePc=false.
    /// </summary>
    private async Task<(string status, bool advance)> DispatchAsync(
        Frame frame, Stack<Frame> frames, ScenarioStep step, CancellationToken ct)
    {
        switch (step.Kind)
        {
            case ScenarioStepKind.Send:
                return (await ExecuteSendAsync(step, ct).ConfigureAwait(false), true);
            case ScenarioStepKind.Receive:
                return (await ExecuteReceiveAsync(step, ct).ConfigureAwait(false), true);
            case ScenarioStepKind.Reply:
                return (await ExecuteReplyAsync(step, ct).ConfigureAwait(false), true);
            case ScenarioStepKind.Delay:
                await Task.Delay(Math.Max(0, step.DelayMs), ct).ConfigureAwait(false);
                return ($"Slept {step.DelayMs} ms", true);
            case ScenarioStepKind.Log:
                EmitLog(_vars.Render(step.Message));
                return ("logged", true);
            case ScenarioStepKind.Branch:
            {
                var (status, nextPc) = ExecuteBranch(step, frame.Pc, frame.LabelIndex);
                frame.Pc = nextPc;
                return (status, false);
            }
            case ScenarioStepKind.HostSend:
                return (await ExecuteHostSendAsync(step, ct).ConfigureAwait(false), true);
            case ScenarioStepKind.HostReceive:
                return (await ExecuteHostReceiveAsync(step, ct).ConfigureAwait(false), true);
            case ScenarioStepKind.SetVariable:
                return (ExecuteSetVariable(step), true);
            case ScenarioStepKind.Loop:
            {
                var (status, nextPc) = ExecuteLoopStart(frame, step);
                frame.Pc = nextPc;
                return (status, false);
            }
            case ScenarioStepKind.EndLoop:
            {
                var (status, nextPc) = ExecuteEndLoop(frame, step);
                frame.Pc = nextPc;
                return (status, false);
            }
            case ScenarioStepKind.CallScenario:
                return (ExecuteCallScenario(frame, frames, step), false);
            default:
                return ($"unknown kind {step.Kind}", true);
        }
    }

    private (string status, int nextPc) ExecuteBranch(
        ScenarioStep step, int pc, Dictionary<string, int> labelIndex)
    {
        // Cases evaluated in order against the most recently captured Receive (SECS or Host).
        // Conditions with empty path on a null lastReceived match (degenerate "always").
        for (int ci = 0; ci < step.Cases.Count; ci++)
        {
            var c = step.Cases[ci];
            bool ok = true;
            foreach (var cond in c.Conditions)
            {
                bool match = _lastSource switch
                {
                    LastSource.Host => MatchUtil.MatchesCondition(cond, _lastHostReceived),
                    LastSource.Secs => MatchUtil.MatchesCondition(cond, _lastReceived?.RootItem),
                    _ => MatchUtil.MatchesCondition(cond, _lastReceived?.RootItem),
                };
                if (!match)
                {
                    ok = false;
                    break;
                }
            }
            if (!ok) continue;

            if (string.IsNullOrEmpty(c.TargetLabel))
                return ($"case {ci} matched, no target → fall through", pc + 1);
            if (!labelIndex.TryGetValue(c.TargetLabel, out var target))
                throw new InvalidOperationException(
                    $"Branch step references unknown label '{c.TargetLabel}'.");
            return ($"case {ci} matched → goto {c.TargetLabel} (step {target})", target);
        }

        if (!string.IsNullOrEmpty(step.DefaultLabel))
        {
            if (!labelIndex.TryGetValue(step.DefaultLabel, out var target))
                throw new InvalidOperationException(
                    $"Branch step references unknown default label '{step.DefaultLabel}'.");
            return ($"no case matched → goto {step.DefaultLabel} (step {target})", target);
        }

        return ("no case matched → fall through", pc + 1);
    }

    private string ExecuteSetVariable(ScenarioStep step)
    {
        if (string.IsNullOrEmpty(step.VariableName))
            throw new InvalidOperationException("SetVariable step requires a variable name.");
        string? value = step.VariableSource switch
        {
            VariableSource.Literal => _vars.Render(step.LiteralValue),
            VariableSource.LastSecsField =>
                ScenarioVariables.ReadSecsPath(_lastReceived?.RootItem, step.VariablePath),
            VariableSource.LastHostField =>
                ScenarioVariables.ReadHostField(_lastHostReceived, step.VariablePath),
            _ => "",
        };
        _vars.Set(step.VariableName, value ?? "");
        return $"set {step.VariableName} = \"{value ?? ""}\"";
    }

    private (string status, int nextPc) ExecuteLoopStart(Frame frame, ScenarioStep step)
    {
        if (string.IsNullOrEmpty(step.LoopId))
            throw new InvalidOperationException("Loop step requires a LoopId.");
        if (!frame.LoopEndByLoopId.TryGetValue(step.LoopId, out var endPc))
            throw new InvalidOperationException($"Loop '{step.LoopId}' has no matching EndLoop.");

        // First entry pushes a new LoopFrame; the engine never re-executes the Loop step head
        // — EndLoop jumps back to startPc+1, skipping the head — so this branch is only the
        // initial entry path.
        var lf = new LoopFrame
        {
            LoopId = step.LoopId,
            StartPc = frame.Pc,
            EndPc = endPc,
            Iteration = 0,
            Times = step.LoopTimes,
        };
        frame.Loops.Push(lf);

        // Zero-times Loop with no other guard should still execute the body indefinitely
        // (matches the model documentation). Zero-times *and* the user pressed "skip"? No,
        // there's no such mode — fall through to the body.
        if (lf.Times > 0 && lf.Iteration >= lf.Times)
        {
            // 0-shot loop — pop and skip past EndLoop.
            frame.Loops.Pop();
            return ($"loop {lf.LoopId} skipped (times=0 effective)", endPc + 1);
        }

        lf.Iteration = 1;
        _vars.Set($"$loop.{lf.LoopId}.i", lf.Iteration.ToString());
        return ($"loop {lf.LoopId} iter 1/{(lf.Times > 0 ? lf.Times.ToString() : "∞")}", frame.Pc + 1);
    }

    private (string status, int nextPc) ExecuteEndLoop(Frame frame, ScenarioStep step)
    {
        if (frame.Loops.Count == 0)
            throw new InvalidOperationException("EndLoop without an active Loop.");
        var lf = frame.Loops.Peek();
        if (!string.IsNullOrEmpty(step.LoopId) && step.LoopId != lf.LoopId)
            throw new InvalidOperationException(
                $"EndLoop '{step.LoopId}' does not match the active loop '{lf.LoopId}'.");

        lf.Iteration++;
        if (lf.Times > 0 && lf.Iteration > lf.Times)
        {
            frame.Loops.Pop();
            return ($"loop {lf.LoopId} done after {lf.Times} iterations", frame.Pc + 1);
        }

        _vars.Set($"$loop.{lf.LoopId}.i", lf.Iteration.ToString());
        // Jump back to the step *after* the Loop head — re-executing the head would push a
        // second LoopFrame for the same logical loop.
        return ($"loop {lf.LoopId} iter {lf.Iteration}/{(lf.Times > 0 ? lf.Times.ToString() : "∞")}",
            lf.StartPc + 1);
    }

    private string ExecuteCallScenario(Frame frame, Stack<Frame> frames, ScenarioStep step)
    {
        if (string.IsNullOrEmpty(step.SubScenarioName))
            throw new InvalidOperationException("CallScenario step requires a sub-scenario name.");
        if (_scenarioLookup == null)
            throw new InvalidOperationException(
                "ScenarioEngine has no scenario lookup; CallScenario unavailable.");
        if (frames.Count >= MaxCallDepth)
            throw new InvalidOperationException(
                $"CallScenario depth exceeded ({MaxCallDepth}); refusing to recurse further.");
        var sub = _scenarioLookup(step.SubScenarioName)
            ?? throw new InvalidOperationException(
                $"Sub-scenario '{step.SubScenarioName}' not found.");
        if (!RoleAllows(sub.Role, _currentRole))
            throw new InvalidOperationException(
                $"Sub-scenario '{sub.Name}' role {sub.Role} incompatible with {_currentRole}.");

        // Push the child frame; do NOT advance the caller's PC — the post-return logic in
        // RunLoopAsync (frames pop branch) bumps it once when the child completes.
        frames.Push(BuildFrame(sub, _logger));
        return $"call {sub.Name}";
    }

    private async Task<string> ExecuteSendAsync(ScenarioStep step, CancellationToken ct)
    {
        if (_send == null)
            throw new InvalidOperationException("ScenarioEngine has no send delegate; cannot execute Send step.");
        var raw = _templateLookup(step.TemplateName)
            ?? throw new InvalidOperationException($"Template '{step.TemplateName}' not found for Send step.");
        var msg = BuildRenderedSecs(raw);
        await _send(msg, ct).ConfigureAwait(false);
        return $"sent S{msg.Stream}F{msg.Function}";
    }

    private async Task<string> ExecuteReceiveAsync(ScenarioStep step, CancellationToken ct)
    {
        var inbox = _inbox ?? throw new InvalidOperationException("Inbox not initialized.");

        var timeout = step.TimeoutMs <= 0 ? Timeout.InfiniteTimeSpan : TimeSpan.FromMilliseconds(step.TimeoutMs);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout != Timeout.InfiniteTimeSpan)
            timeoutCts.CancelAfter(timeout);

        try
        {
            while (await inbox.Reader.WaitToReadAsync(timeoutCts.Token).ConfigureAwait(false))
            {
                while (inbox.Reader.TryRead(out var candidate))
                {
                    if (MatchesReceiveStep(step, candidate))
                    {
                        _lastReceived = candidate;
                        _lastSource = LastSource.Secs;
                        return $"matched S{candidate.Stream}F{candidate.Function}";
                    }
                    // Non-matching messages fall through silently — they'll be handled by other handlers
                    // because we only consume from the inbox; the router pushes a copy.
                }
            }
            return "channel closed";
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout (the linked CTS, not the run CTS).
            return step.OnTimeout switch
            {
                ReceiveTimeoutAction.Skip => "timeout — skipped",
                ReceiveTimeoutAction.Continue => "timeout — continue",
                _ => throw new TimeoutException($"Receive step timed out after {step.TimeoutMs} ms"),
            };
        }
    }

    private async Task<string> ExecuteReplyAsync(ScenarioStep step, CancellationToken ct)
    {
        if (_send == null)
            throw new InvalidOperationException("ScenarioEngine has no send delegate; cannot execute Reply step.");
        if (_lastReceived == null)
            throw new InvalidOperationException("Reply step requires a preceding Receive that captured a message.");
        var raw = _templateLookup(step.TemplateName)
            ?? throw new InvalidOperationException($"Template '{step.TemplateName}' not found for Reply step.");
        var reply = BuildRenderedSecs(raw);
        reply.SystemBytes = _lastReceived.SystemBytes;
        reply.WBit = false;
        await _send(reply, ct).ConfigureAwait(false);
        return $"replied S{reply.Stream}F{reply.Function}";
    }

    /// <summary>
    /// Build a SECS message from a template, but interpolate <c>${var}</c> in the
    /// <see cref="SecsMessageTemplate.ItemXml"/> first. The original template is left intact —
    /// the engine clones the template's value-bearing fields onto a transient instance.
    /// </summary>
    private SecsMessage BuildRenderedSecs(SecsMessageTemplate raw)
    {
        var rendered = new SecsMessageTemplate
        {
            Name = raw.Name,
            Stream = raw.Stream,
            Function = raw.Function,
            WBit = raw.WBit,
            Description = raw.Description,
            ItemXml = _vars.Render(raw.ItemXml),
            FieldMetadata = raw.FieldMetadata,
        };
        return rendered.BuildMessage();
    }

    private static bool MatchesReceiveStep(ScenarioStep step, SecsMessage msg)
    {
        if (step.Stream != 0 && step.Stream != msg.Stream) return false;
        if (step.Function != 0 && step.Function != msg.Function) return false;
        foreach (var cond in step.Conditions)
        {
            if (!MatchUtil.MatchesCondition(cond, msg.RootItem)) return false;
        }
        return true;
    }

    private async Task<string> ExecuteHostSendAsync(ScenarioStep step, CancellationToken ct)
    {
        var (lookup, send, channelKey) = ResolveHostChannel(step.HostChannelName);
        if (send == null || lookup == null)
            throw new InvalidOperationException(
                $"ScenarioEngine has no host channel '{channelKey}'; configure & connect it first.");
        var tpl = lookup(step.HostMessageName)
            ?? throw new InvalidOperationException($"Host template '{step.HostMessageName}' not found on channel '{channelKey}' for HostSend step.");
        var msg = tpl.BuildMessage();
        _vars.RenderInPlace(msg);
        await send(msg, ct).ConfigureAwait(false);
        return $"host-sent [{channelKey}] {msg.Name}";
    }

    private async Task<string> ExecuteHostReceiveAsync(ScenarioStep step, CancellationToken ct)
    {
        var channelKey = step.HostChannelName ?? "";
        var inbox = GetOrCreateHostInbox(channelKey);

        var timeout = step.TimeoutMs <= 0 ? Timeout.InfiniteTimeSpan : TimeSpan.FromMilliseconds(step.TimeoutMs);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout != Timeout.InfiniteTimeSpan)
            timeoutCts.CancelAfter(timeout);

        try
        {
            while (await inbox.Reader.WaitToReadAsync(timeoutCts.Token).ConfigureAwait(false))
            {
                while (inbox.Reader.TryRead(out var candidate))
                {
                    if (MatchesHostReceiveStep(step, candidate))
                    {
                        _lastHostReceived = candidate;
                        _lastSource = LastSource.Host;
                        return $"host-matched [{channelKey}] {candidate.Name}";
                    }
                }
            }
            return $"host channel '{channelKey}' closed";
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return step.OnTimeout switch
            {
                ReceiveTimeoutAction.Skip => "timeout — skipped",
                ReceiveTimeoutAction.Continue => "timeout — continue",
                _ => throw new TimeoutException($"HostReceive step timed out after {step.TimeoutMs} ms"),
            };
        }
    }

    private static bool MatchesHostReceiveStep(ScenarioStep step, HostMessage msg)
    {
        // HostMessageName "" matches any inbound host message; otherwise exact-match by name.
        if (!string.IsNullOrEmpty(step.HostMessageName)
            && !string.Equals(step.HostMessageName, msg.Name, StringComparison.Ordinal))
            return false;
        foreach (var cond in step.Conditions)
        {
            if (!MatchUtil.MatchesCondition(cond, msg)) return false;
        }
        return true;
    }

    /// <summary>
    /// Resolve (lookup, send) for the given channel name. Falls back to default channel ""
    /// if the requested channel isn't registered (so empty step.HostChannelName + single
    /// AttachHost call still works).
    /// </summary>
    private (Func<string, HostMessageTemplate?>? lookup, Func<HostMessage, CancellationToken, Task>? send, string key)
        ResolveHostChannel(string? channelName)
    {
        var key = channelName ?? "";
        if (_hostSends.TryGetValue(key, out var send) && _hostTemplateLookups.TryGetValue(key, out var lookup))
            return (lookup, send, key);
        if (key != "" && _hostSends.TryGetValue("", out var defSend) && _hostTemplateLookups.TryGetValue("", out var defLookup))
            return (defLookup, defSend, "(default)");
        return (null, null, key);
    }

    private Channel<HostMessage> GetOrCreateHostInbox(string channelKey)
    {
        if (!_hostInboxes.TryGetValue(channelKey, out var inbox))
        {
            inbox = Channel.CreateUnbounded<HostMessage>();
            _hostInboxes[channelKey] = inbox;
        }
        return inbox;
    }

    /// <summary>
    /// Wire this to <c>HostProtocol.HostMessageReceived</c>. Pass the channel name the
    /// message arrived on so HostReceive steps targeting that channel can consume it.
    /// </summary>
    public void OnHostMessageReceived(string channelName, HostMessage msg)
    {
        var key = channelName ?? "";
        var inbox = GetOrCreateHostInbox(key);
        inbox.Writer.TryWrite(msg);
    }

    /// <summary>Backwards-compat overload — uses default channel.</summary>
    public void OnHostMessageReceived(HostMessage msg) => OnHostMessageReceived("", msg);

    private void EmitLog(string text)
    {
        Log?.Invoke(text);
        _logger.LogInformation("{Text}", text);
    }

    // ─── ISecsMessageHandler — runs on the router pipeline ───

    /// <summary>
    /// Forward inbound messages to the running scenario's Receive step (if any).
    /// Always returns null (no synchronous reply); when the message is forwarded into the
    /// inbox, <see cref="ConsumedLast"/> is set so the router knows to suppress built-in
    /// handlers for this message — otherwise a W-bit request gets answered twice (once by
    /// the script's Reply step, once by the built-in handler).
    /// </summary>
    public Task<SecsMessage?> HandleAsync(SecsMessage request, EquipmentModel model, ProtocolRole role, CancellationToken ct)
    {
        var inbox = _inbox;
        ConsumedLast = inbox != null && inbox.Writer.TryWrite(request);
        return Task.FromResult<SecsMessage?>(null);
    }
}
