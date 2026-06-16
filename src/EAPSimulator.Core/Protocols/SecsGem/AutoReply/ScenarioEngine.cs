using System.Threading.Channels;
using EAPSimulator.Core.Protocols;
using EAPSimulator.Core.Protocols.SecsGem.Gem;
using EAPSimulator.Core.Protocols.SecsGem.Handlers;
using EAPSimulator.Core.Protocols.SecsGem.SecsII;
using Microsoft.Extensions.Logging;

namespace EAPSimulator.Core.Protocols.SecsGem.AutoReply;

/// <summary>
/// Famate-style scenario interpreter. Drives a script of heterogeneous steps
/// (Send/Receive/Reply/Delay/Log) and lets the user run/stop/single-step.
/// Implements <see cref="ISecsMessageHandler"/> so an active Receive step can
/// consume an inbound message off the router pipeline.
/// </summary>
public class ScenarioEngine : ISecsMessageHandler
{
    private readonly ILogger _logger;
    private readonly Func<string, SecsMessageTemplate?> _templateLookup;
    private readonly Func<SecsMessage, CancellationToken, Task>? _send;
    private readonly ProtocolRole _currentRole;

    private readonly object _lock = new();
    private CancellationTokenSource? _runCts;
    private Task? _runTask;

    // Current Receive step waits on this channel; the router pushes inbound messages in.
    private Channel<SecsMessage>? _inbox;

    // Last-received message — used by a subsequent Reply step.
    private SecsMessage? _lastReceived;

    public event Action<ScenarioDefinition, int, ScenarioStep>? StepStarted;
    public event Action<ScenarioDefinition, int, ScenarioStep, string>? StepCompleted; // status text
    public event Action<ScenarioDefinition, string>? ScenarioFinished; // status text
    public event Action<string>? Log;

    public bool IsRunning => _runTask is { IsCompleted: false };
    public ScenarioDefinition? RunningScenario { get; private set; }
    public int CurrentStepIndex { get; private set; }

    /// <summary>
    /// Set by <see cref="HandleAsync"/> when an inbound message was forwarded into the running
    /// scenario's inbox. The router checks this to decide whether to suppress built-in handlers
    /// for that message — otherwise a W-bit request gets answered twice (once by the script's
    /// Reply step, once by the built-in handler).
    /// </summary>
    public bool ConsumedLast { get; private set; }

    public ScenarioEngine(
        ILogger logger,
        Func<string, SecsMessageTemplate?> templateLookup,
        Func<SecsMessage, CancellationToken, Task>? send = null,
        ProtocolRole currentRole = ProtocolRole.Equipment)
    {
        _logger = logger;
        _templateLookup = templateLookup;
        _send = send;
        _currentRole = currentRole;
    }

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
            _inbox = Channel.CreateUnbounded<SecsMessage>();
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

    private async Task RunLoopAsync(ScenarioDefinition scenario, CancellationToken ct)
    {
        try
        {
            // Build label → step-index map. Earlier definition wins on duplicates; warn the rest.
            var labelIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < scenario.Steps.Count; i++)
            {
                var lbl = scenario.Steps[i].Label;
                if (string.IsNullOrEmpty(lbl)) continue;
                if (!labelIndex.TryAdd(lbl, i))
                    _logger.LogWarning("Scenario '{Name}' duplicate label '{Lbl}' at step {I}; first wins",
                        scenario.Name, lbl, i);
            }

            do
            {
                int pc = 0;
                while (pc < scenario.Steps.Count)
                {
                    ct.ThrowIfCancellationRequested();
                    CurrentStepIndex = pc;
                    var step = scenario.Steps[pc];
                    StepStarted?.Invoke(scenario, pc, step);
                    EmitLog($"Scenario '{scenario.Name}' step {pc}: {step.DisplayText}");

                    // Branch is the only step that can change PC; everything else falls through to pc+1.
                    if (step.Kind == ScenarioStepKind.Branch)
                    {
                        var (status, nextPc) = ExecuteBranch(step, pc, labelIndex);
                        StepCompleted?.Invoke(scenario, pc, step, status);
                        pc = nextPc;
                    }
                    else
                    {
                        var status = await ExecuteStepAsync(step, ct).ConfigureAwait(false);
                        StepCompleted?.Invoke(scenario, pc, step, status);
                        pc++;
                    }
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
            }
        }
    }

    private (string status, int nextPc) ExecuteBranch(
        ScenarioStep step, int pc, Dictionary<string, int> labelIndex)
    {
        // Cases evaluated in order against the most recently captured Receive message.
        // Conditions with empty path on a null lastReceived match (degenerate "always").
        for (int ci = 0; ci < step.Cases.Count; ci++)
        {
            var c = step.Cases[ci];
            bool ok = true;
            foreach (var cond in c.Conditions)
            {
                if (!MatchUtil.MatchesCondition(cond, _lastReceived?.RootItem))
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

    private async Task<string> ExecuteStepAsync(ScenarioStep step, CancellationToken ct)
    {
        switch (step.Kind)
        {
            case ScenarioStepKind.Send:
                return await ExecuteSendAsync(step, ct).ConfigureAwait(false);
            case ScenarioStepKind.Receive:
                return await ExecuteReceiveAsync(step, ct).ConfigureAwait(false);
            case ScenarioStepKind.Reply:
                return await ExecuteReplyAsync(step, ct).ConfigureAwait(false);
            case ScenarioStepKind.Delay:
                await Task.Delay(Math.Max(0, step.DelayMs), ct).ConfigureAwait(false);
                return $"Slept {step.DelayMs} ms";
            case ScenarioStepKind.Log:
                EmitLog(step.Message);
                return "logged";
            case ScenarioStepKind.Branch:
                // Should never reach here — Branch is handled in RunLoopAsync directly so it can change PC.
                throw new InvalidOperationException("Branch step must be handled by RunLoopAsync");
            default:
                return $"unknown kind {step.Kind}";
        }
    }

    private async Task<string> ExecuteSendAsync(ScenarioStep step, CancellationToken ct)
    {
        if (_send == null)
            throw new InvalidOperationException("ScenarioEngine has no send delegate; cannot execute Send step.");
        var tpl = _templateLookup(step.TemplateName)
            ?? throw new InvalidOperationException($"Template '{step.TemplateName}' not found for Send step.");
        var msg = tpl.BuildMessage();
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
        var tpl = _templateLookup(step.TemplateName)
            ?? throw new InvalidOperationException($"Template '{step.TemplateName}' not found for Reply step.");
        var reply = tpl.BuildMessage();
        reply.SystemBytes = _lastReceived.SystemBytes;
        reply.WBit = false;
        await _send(reply, ct).ConfigureAwait(false);
        return $"replied S{reply.Stream}F{reply.Function}";
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
