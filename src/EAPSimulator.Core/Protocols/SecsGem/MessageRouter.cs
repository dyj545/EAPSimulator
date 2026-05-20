using EAPSimulator.Core.Protocols.SecsGem.AutoReply;
using EAPSimulator.Core.Protocols.SecsGem.Gem;
using EAPSimulator.Core.Protocols.SecsGem.Handlers;
using EAPSimulator.Core.Protocols.SecsGem.Hsms;
using EAPSimulator.Core.Protocols.SecsGem.SecsII;
using Microsoft.Extensions.Logging;

namespace EAPSimulator.Core.Protocols.SecsGem;

/// <summary>
/// Routes incoming SECS messages to registered handlers by (Stream, Function) key.
/// Supports chained lookup: auto-reply rules → scenario engine → built-in handlers.
/// </summary>
public class MessageRouter
{
    private readonly ILogger<MessageRouter> _logger;
    private readonly Dictionary<(byte Stream, byte Function), ISecsMessageHandler> _handlers = new();

    // Auto-reply quick rules: multiple rules per (S,F) key, each with conditions
    private readonly Dictionary<(byte Stream, byte Function), AutoReplyHandler> _quickReplyHandlers = new();

    // Scenario engine (optional, set externally)
    private ScenarioEngine? _scenarioEngine;

    public MessageRouter(ILogger<MessageRouter> logger)
    {
        _logger = logger;
    }

    public void RegisterHandler(byte stream, byte function, ISecsMessageHandler handler)
    {
        _handlers[(stream, function)] = handler;
        _logger.LogDebug("Registered handler for S{Stream}F{Function}", stream, function);
    }

    public void RegisterDefaultHandlers()
    {
        // S1: Equipment State
        RegisterHandler(1, 1, new S1F1Handler());        // Are You There → S1F2
        RegisterHandler(1, 2, new NoReplyHandler());      // S1F2 (reply, no action)
        RegisterHandler(1, 11, new S1F11Handler());      // SV Namelist Req → S1F12
        RegisterHandler(1, 12, new NoReplyHandler());     // S1F12 (reply, no action)
        RegisterHandler(1, 13, new S1F13Handler());      // Establish Comm → S1F14
        RegisterHandler(1, 14, new NoReplyHandler());     // S1F14 (reply, no action)

        // S2: Equipment Control
        RegisterHandler(2, 13, new S2F13Handler());      // Process Program → S2F14
        RegisterHandler(2, 14, new NoReplyHandler());     // S2F14 (reply, no action)
        RegisterHandler(2, 41, new S2F41Handler());      // Host Command → S2F42
        RegisterHandler(2, 42, new NoReplyHandler());     // S2F42 (reply, no action)

        // S5: Alarms
        RegisterHandler(5, 1, new S5F1Handler());        // Alarm Report → S5F2
        RegisterHandler(5, 2, new NoReplyHandler());      // S5F2 (reply, no action)

        // S6: Collection Events
        RegisterHandler(6, 11, new S6F11Handler());      // CE Report → S6F12
        RegisterHandler(6, 12, new NoReplyHandler());     // S6F12 (reply, no action)
    }

    public bool UnregisterHandler(byte stream, byte function)
    {
        var removed = _handlers.Remove((stream, function));
        if (removed)
            _logger.LogDebug("Unregistered handler for S{Stream}F{Function}", stream, function);
        return removed;
    }

    /// <summary>
    /// Register a quick-reply rule. Multiple rules with different conditions can share the same (S,F) key.
    /// </summary>
    public void RegisterQuickReplyRule(AutoReplyRule rule, SecsMessageTemplate replyTemplate)
    {
        if (!rule.Enabled) return;

        var key = (rule.TriggerStream, rule.TriggerFunction);
        if (!_quickReplyHandlers.TryGetValue(key, out var handler))
        {
            handler = new AutoReplyHandler();
            _quickReplyHandlers[key] = handler;
        }

        handler.AddRule(rule.Conditions, replyTemplate);
        _logger.LogInformation("Quick-reply registered: {Trigger} → S{RS}F{RF} ({Count} rules for this key)",
            rule.TriggerDisplay, replyTemplate.Stream, replyTemplate.Function, handler.RuleCount);
    }

    /// <summary>
    /// Set the scenario engine for multi-step conversation flows.
    /// </summary>
    public void SetScenarioEngine(ScenarioEngine? engine)
    {
        _scenarioEngine = engine;
    }

    /// <summary>
    /// Clear all quick-reply rules.
    /// </summary>
    public void ClearQuickReplyRules()
    {
        _quickReplyHandlers.Clear();
    }

    public async Task<SecsMessage?> RouteAsync(SecsMessage request, EquipmentModel model, ProtocolRole role, CancellationToken ct)
    {
        var key = (request.Stream, request.Function);

        // Priority 1: Quick-reply rules (conditional match)
        if (_quickReplyHandlers.TryGetValue(key, out var quickHandler))
        {
            var quickReply = await quickHandler.HandleAsync(request, model, role, ct);
            if (quickReply != null)
            {
                _logger.LogInformation("Quick-reply matched for S{S}F{F}", request.Stream, request.Function);
                return quickReply;
            }
        }

        // Priority 2: Scenario engine
        if (_scenarioEngine != null)
        {
            var scenarioReply = await _scenarioEngine.HandleAsync(request, model, role, ct);
            if (scenarioReply != null)
            {
                _logger.LogInformation("Scenario matched for S{S}F{F}", request.Stream, request.Function);
                return scenarioReply;
            }
        }

        // Priority 3: Built-in handlers
        if (_handlers.TryGetValue(key, out var handler))
        {
            _logger.LogInformation("Built-in handler for S{S}F{F} (role={Role})", request.Stream, request.Function, role);
            return await handler.HandleAsync(request, model, role, ct);
        }

        _logger.LogWarning("No handler for S{S}F{F}", request.Stream, request.Function);
        return null;
    }
}
