using EAPSimulator.Core.EquipmentState;
using EAPSimulator.Core.Protocols.SecsGem;
using EAPSimulator.Core.Protocols.SecsGem.AutoReply;
using EAPSimulator.Core.Protocols.SecsGem.SecsII;
using HostMsg = EAPSimulator.Core.Protocols.HostProtocol;
using Microsoft.Extensions.Logging;

namespace EAPSimulator.Core.Protocols.Bridge;

/// <summary>
/// EAP Bridge: bidirectional data bridge between SECS/GEM equipment and Host MES.
/// Routes SECS events to Host messages and Host commands to SECS messages.
/// </summary>
public class EapBridge : IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly EquipmentStateManager _stateManager;
    private readonly DataMapper _mapper;
    private HostMsg.HostProtocol? _hostProtocol;
    private SecsGemProtocol? _secsProtocol;
    private ScenarioEngine? _scenarioEngine;

    // Template storages for message building
    private List<HostMsg.HostMessageTemplate> _hostTemplates = [];
    private List<SecsMessageTemplate> _secsTemplates = [];

    /// <summary>Raised when a bridge event occurs (for logging).</summary>
    public event EventHandler<BridgeEventArgs>? BridgeEvent;

    public EapBridge(ILogger logger, EquipmentStateManager stateManager)
    {
        _logger = logger;
        _stateManager = stateManager;
        _mapper = new DataMapper();
    }

    /// <summary>Access the data mapper for configuring field mappings.</summary>
    public DataMapper Mapper => _mapper;

    /// <summary>Set host message templates for SECS→Host message building.</summary>
    public void SetHostTemplates(IEnumerable<HostMsg.HostMessageTemplate> templates)
    {
        _hostTemplates = templates.ToList();
        _logger.LogInformation("EAP Bridge: {Count} host templates loaded", _hostTemplates.Count);
    }

    /// <summary>Set SECS message templates for Host→SECS message building.</summary>
    public void SetSecsTemplates(IEnumerable<SecsMessageTemplate> templates)
    {
        _secsTemplates = templates.ToList();
        _logger.LogInformation("EAP Bridge: {Count} SECS templates loaded", _secsTemplates.Count);
    }

    /// <summary>Attach a scenario engine for Host message routing.</summary>
    public void AttachScenarioEngine(ScenarioEngine engine)
    {
        _scenarioEngine = engine;
        _logger.LogInformation("EAP Bridge: Scenario engine attached");
    }

    /// <summary>
    /// Attach a SECS protocol instance to the bridge.
    /// </summary>
    public void AttachSecsProtocol(SecsGemProtocol protocol)
    {
        _secsProtocol = protocol;
        _secsProtocol.MessageReceived += OnSecsMessageReceived;
        _logger.LogInformation("EAP Bridge: SECS protocol attached");
    }

    /// <summary>
    /// Attach a Host protocol instance to the bridge.
    /// </summary>
    public void AttachHostProtocol(HostMsg.HostProtocol protocol)
    {
        _hostProtocol = protocol;
        _hostProtocol.MessageReceived += OnHostMessageReceived;
        _logger.LogInformation("EAP Bridge: Host protocol attached");
    }

    /// <summary>
    /// Handle a SECS message received from equipment.
    /// Routes collection events (S6F11) to Host messages.
    /// </summary>
    private void OnSecsMessageReceived(object? sender, ProtocolMessageEventArgs e)
    {
        try
        {
            var msg = e.Message;
            // S6F11: Collection Event Report
            if (msg.Name.Contains("S6F11") || msg.Name.Contains("CollectionEvent"))
            {
                HandleCollectionEventReport(msg);
            }
            // S5F1: Alarm Report
            else if (msg.Name.Contains("S5F1") || msg.Name.Contains("Alarm"))
            {
                HandleAlarmReport(msg);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EAP Bridge: Error handling SECS message");
        }
    }

    /// <summary>
    /// Handle a Host message received from MES.
    /// Routes commands to SECS messages.
    /// </summary>
    private void OnHostMessageReceived(object? sender, ProtocolMessageEventArgs e)
    {
        try
        {
            var msg = e.Message;

            // Remote command
            if (msg.Name.Contains("RemoteCommand"))
            {
                HandleRemoteCommand(msg);
            }
            // Recipe download
            else if (msg.Name.Contains("RecipeDownload"))
            {
                HandleRecipeDownload(msg);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EAP Bridge: Error handling Host message");
        }
    }

    /// <summary>
    /// Handle S6F11 Collection Event Report from equipment.
    /// Maps CE data to Host message and sends to MES.
    /// </summary>
    private void HandleCollectionEventReport(ProtocolMessage msg)
    {
        var ceid = msg.GetField<int>("CEID");
        var ce = _stateManager.GetCollectionEvent(ceid);

        if (ce == null)
        {
            _logger.LogWarning("EAP Bridge: Unknown CEID {CEID}", ceid);
            return;
        }

        RaiseBridgeEvent($"CE Report: {ce.Name} (CEID={ceid})", BridgeEventType.SecsToHost);

        // Find linked reports and collect variable values
        var eventData = new Dictionary<string, string>
        {
            ["CEID"] = ceid.ToString(),
            ["EventName"] = ce.Name,
        };

        foreach (var rptid in ce.ReportLinks)
        {
            var report = _stateManager.GetDataReport(rptid);
            if (report == null) continue;

            foreach (var svid in report.VariableIds)
            {
                var sv = _stateManager.GetStatusVariable(svid);
                if (sv != null)
                    eventData[sv.Name] = sv.Value;
            }
        }

        // Find matching host template by event name
        var template = _hostTemplates.FirstOrDefault(t =>
            t.Name.Equals(ce.Name, StringComparison.OrdinalIgnoreCase));

        if (template != null && _hostProtocol != null)
        {
            try
            {
                var hostMsg = template.BuildMessage();

                // Apply collected event data to the host message fields
                foreach (var (fieldName, value) in eventData)
                    hostMsg.SetFieldValue(fieldName, value);

                // Fire-and-forget send (non-blocking)
                _ = _hostProtocol.SendHostMessageAsync(hostMsg, CancellationToken.None)
                    .ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                            _logger.LogError(t.Exception,
                                "EAP Bridge: Failed to send Host message for CE {CE}", ce.Name);
                    });

                _logger.LogInformation("EAP Bridge: Sent Host message '{Name}' for CE {CE}",
                    hostMsg.Name, ce.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EAP Bridge: Error building Host message for CE {CE}", ce.Name);
            }
        }
        else
        {
            _logger.LogDebug("EAP Bridge: No host template/connection for CE {CE}", ce.Name);
        }
    }

    /// <summary>
    /// Handle S5F1 Alarm Report from equipment.
    /// </summary>
    private void HandleAlarmReport(ProtocolMessage msg)
    {
        var alid = msg.GetField<int>("ALID");
        var alarm = _stateManager.GetAlarm(alid);

        if (alarm == null)
        {
            _logger.LogWarning("EAP Bridge: Unknown ALID {ALID}", alid);
            return;
        }

        alarm.IsSet = true;
        RaiseBridgeEvent($"Alarm: {alarm.Name} ({alarm.Severity})", BridgeEventType.SecsToHost);
        _logger.LogWarning("EAP Bridge: Alarm set - {Name} ({Severity})", alarm.Name, alarm.Severity);

        // Forward alarm to Host if templates available
        if (_hostProtocol != null)
        {
            var alarmTemplate = _hostTemplates.FirstOrDefault(t =>
                t.Name.Equals("AlarmReport", StringComparison.OrdinalIgnoreCase));

            if (alarmTemplate != null)
            {
                try
                {
                    var hostMsg = alarmTemplate.BuildMessage();
                    hostMsg.SetFieldValue("ALID", alid.ToString());
                    hostMsg.SetFieldValue("AlarmName", alarm.Name);
                    hostMsg.SetFieldValue("Severity", alarm.Severity.ToString());

                    _ = _hostProtocol.SendHostMessageAsync(hostMsg, CancellationToken.None)
                        .ContinueWith(t =>
                        {
                            if (t.IsFaulted)
                                _logger.LogError(t.Exception, "EAP Bridge: Failed to send Alarm to Host");
                        });

                    _logger.LogInformation("EAP Bridge: Sent Host alarm report for ALID {ALID}", alid);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "EAP Bridge: Error sending alarm to Host");
                }
            }
        }
    }

    /// <summary>
    /// Handle Remote Command from MES.
    /// Sends S2F41 to equipment.
    /// </summary>
    private void HandleRemoteCommand(ProtocolMessage msg)
    {
        var command = msg.GetField<string>("Command") ?? "";
        RaiseBridgeEvent($"Remote Command: {command}", BridgeEventType.HostToSecs);
        _logger.LogInformation("EAP Bridge: Remote command received - {Command}", command);

        if (_secsProtocol == null) return;

        try
        {
            // Build a ProtocolMessage with Stream=2, Function=41
            var secsCmd = new ProtocolMessage
            {
                Name = $"S2F41_{command}",
                Description = $"Host Command: {command}",
                Fields = new Dictionary<string, object?>
                {
                    ["Stream"] = (byte)2,
                    ["Function"] = (byte)41,
                    ["WBit"] = true,
                    ["RCMD"] = command,
                },
            };

            // Copy any additional parameters from the host message
            foreach (var (key, value) in msg.Fields)
            {
                if (key != "Command" && key != "Stream" && key != "Function")
                    secsCmd.SetField(key, value);
            }

            // Try to find a matching SECS template and use it
            var template = _secsTemplates.FirstOrDefault(t =>
                t.Stream == 2 && t.Function == 41
                && (t.Name.Contains(command, StringComparison.OrdinalIgnoreCase)
                    || t.Name.Contains("RemoteCommand", StringComparison.OrdinalIgnoreCase)));

            if (template != null)
            {
                var builtMsg = template.BuildMessage();
                _ = _secsProtocol.SendSecsMessageAsync(builtMsg, CancellationToken.None)
                    .ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                            _logger.LogError(t.Exception,
                                "EAP Bridge: Failed to send S2F41 for command {Cmd}", command);
                    });
            }
            else
            {
                _ = _secsProtocol.SendAsync(secsCmd, CancellationToken.None)
                    .ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                            _logger.LogError(t.Exception,
                                "EAP Bridge: Failed to send S2F41 for command {Cmd}", command);
                    });
            }

            _logger.LogInformation("EAP Bridge: Sent S2F41 for command '{Command}'", command);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EAP Bridge: Error handling RemoteCommand '{Command}'", command);
        }
    }

    /// <summary>
    /// Handle Recipe Download from MES.
    /// Sends S7F3/S7F5 to equipment.
    /// </summary>
    private void HandleRecipeDownload(ProtocolMessage msg)
    {
        var recipeName = msg.GetField<string>("RecipeName") ?? "";
        var recipeBody = msg.GetField<string>("RecipeBody") ?? "";
        RaiseBridgeEvent($"Recipe Download: {recipeName}", BridgeEventType.HostToSecs);
        _logger.LogInformation("EAP Bridge: Recipe download - {Recipe}", recipeName);

        if (_secsProtocol == null) return;

        try
        {
            // Try to find S7F3/S7F5 template
            var template = _secsTemplates.FirstOrDefault(t =>
                t.Stream == 7 && (t.Function == 3 || t.Function == 5)
                && t.Name.Contains(recipeName, StringComparison.OrdinalIgnoreCase));

            if (template != null)
            {
                var builtMsg = template.BuildMessage();
                _ = _secsProtocol.SendSecsMessageAsync(builtMsg, CancellationToken.None)
                    .ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                            _logger.LogError(t.Exception,
                                "EAP Bridge: Failed to send S7F3 for recipe {Recipe}", recipeName);
                    });
            }
            else
            {
                // Fallback: build basic S7F3
                var secsMsg = new ProtocolMessage
                {
                    Name = $"S7F3_{recipeName}",
                    Description = $"Recipe: {recipeName}",
                    Fields = new Dictionary<string, object?>
                    {
                        ["Stream"] = (byte)7,
                        ["Function"] = (byte)3,
                        ["WBit"] = true,
                        ["PPID"] = recipeName,
                    },
                };

                if (!string.IsNullOrEmpty(recipeBody))
                    secsMsg.SetField("PPBODY", recipeBody);

                _ = _secsProtocol.SendAsync(secsMsg, CancellationToken.None)
                    .ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                            _logger.LogError(t.Exception,
                                "EAP Bridge: Failed to send S7F3 for recipe {Recipe}", recipeName);
                    });
            }

            _logger.LogInformation("EAP Bridge: Sent S7F3 for recipe '{Recipe}'", recipeName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EAP Bridge: Error handling RecipeDownload '{Recipe}'", recipeName);
        }
    }

    private void RaiseBridgeEvent(string description, BridgeEventType type)
    {
        BridgeEvent?.Invoke(this, new BridgeEventArgs(description, type));
    }

    public async ValueTask DisposeAsync()
    {
        if (_secsProtocol != null)
            _secsProtocol.MessageReceived -= OnSecsMessageReceived;
        if (_hostProtocol != null)
            _hostProtocol.MessageReceived -= OnHostMessageReceived;
        await Task.CompletedTask;
    }
}

/// <summary>
/// Event args for bridge events.
/// </summary>
public class BridgeEventArgs : EventArgs
{
    public string Description { get; }
    public BridgeEventType Type { get; }
    public DateTime Timestamp { get; }

    public BridgeEventArgs(string description, BridgeEventType type)
    {
        Description = description;
        Type = type;
        Timestamp = DateTime.Now;
    }
}

/// <summary>
/// Types of bridge events.
/// </summary>
public enum BridgeEventType
{
    SecsToHost,
    HostToSecs,
    StateChange,
    Error
}
