using EAPSimulator.Core.Configuration;
using EAPSimulator.Core.Protocols.SecsGem.Gem;
using EAPSimulator.Core.Protocols.SecsGem.Hsms;
using EAPSimulator.Core.Protocols.SecsGem.SecsII;
using Microsoft.Extensions.Logging;

namespace EAPSimulator.Core.Protocols.SecsGem;

/// <summary>
/// SECS/GEM protocol implementation using HSMS transport.
/// Can operate as Host (Active) or Equipment (Passive).
/// Implements IProtocol for the plugin system.
/// </summary>
public class SecsGemProtocol : IProtocol
{
    private readonly ILogger<SecsGemProtocol> _logger;
    private readonly HsmsTransport _transport;
    private readonly MessageRouter _router;
    private readonly EquipmentModel _equipmentModel;
    private ProtocolRole _role;
    private ProtocolState _state = ProtocolState.Disconnected;
    private CancellationTokenSource? _cts;

    public string Name => "SECS/GEM";
    public ProtocolRole Role
    {
        get => _role;
        set => _role = value;
    }
    public ProtocolState State => _state;
    public EquipmentModel EquipmentModel => _equipmentModel;
    public MessageRouter Router => _router;

    public event EventHandler<ProtocolMessageEventArgs>? MessageReceived;
    public event EventHandler<ProtocolMessageEventArgs>? MessageSent;
    public event EventHandler<ProtocolStateEventArgs>? StateChanged;

    public SecsGemProtocol(
        ILogger<SecsGemProtocol> logger,
        ILogger<HsmsTransport> transportLogger,
        ILogger<MessageRouter> routerLogger,
        HsmsSettings settings,
        EquipmentModel? equipmentModel = null)
    {
        _logger = logger;
        _role = settings.ConnectionMode == ConnectionMode.Active ? ProtocolRole.Host : ProtocolRole.Equipment;
        _transport = new HsmsTransport(transportLogger, settings);
        _router = new MessageRouter(routerLogger);
        _router.RegisterDefaultHandlers();
        _equipmentModel = equipmentModel ?? new EquipmentModel();

        WireTransportEvents();
    }

    private void WireTransportEvents()
    {
        _transport.HsmsMessageReceived += async (_, hsmsMsg) =>
        {
            if (!hsmsMsg.IsDataMessage) return;

            var secsMsg = hsmsMsg.DecodeSecsMessage();
            if (secsMsg == null) return;

            var protocolMsg = SecsToProtocolMessage(secsMsg, MessageDirection.Receive);
            _logger.LogInformation("<< {MessageId}: {Content}", protocolMsg.Id, protocolMsg.Description);
            MessageReceived?.Invoke(this, new ProtocolMessageEventArgs(protocolMsg, MessageDirection.Receive));

            // Route to handler and send reply if W-bit is set
            if (secsMsg.WBit)
            {
                // Snapshot the cts token: _cts may be replaced/disposed concurrently by Stop.
                var token = _cts?.Token ?? CancellationToken.None;
                var reply = await _router.RouteAsync(secsMsg, _equipmentModel, _role, token);
                if (reply != null)
                {
                    reply.SystemBytes = secsMsg.SystemBytes;
                    var replyMsg = SecsToProtocolMessage(reply, MessageDirection.Send);
                    try
                    {
                        await _transport.Connection!.SendDataMessageAsync(reply, false, CancellationToken.None);
                        _logger.LogInformation(">> {MessageId}: {Content}", replyMsg.Id, replyMsg.Description);
                        MessageSent?.Invoke(this, new ProtocolMessageEventArgs(replyMsg, MessageDirection.Send));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send reply S{Stream}F{Function}", reply.Stream, reply.Function);
                    }
                }
            }
        };

        _transport.HsmsStateChanged += async (_, args) =>
        {
            if (args.NewState == HsmsState.Selected)
            {
                ChangeState(ProtocolState.Connected);

                // Send S1F13 to establish GEM communication
                try
                {
                    var token = _cts?.Token ?? CancellationToken.None;
                    await SendEstablishCommunicationAsync(token);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send S1F13 after connection established.");
                }
            }
            else if (args.NewState == HsmsState.NotConnected)
            {
                ChangeState(ProtocolState.Disconnected);
            }
        };

        _transport.Disconnected += (_, reason) =>
        {
            ChangeState(ProtocolState.Disconnected);
        };
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        ChangeState(ProtocolState.Connecting);

        try
        {
            await _transport.ConnectAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to establish HSMS transport");
            ChangeState(ProtocolState.Disconnected);
            throw;
        }

        // Only mark Connected if transport has an actual connection.
        // Passive mode: ConnectAsync just starts listening, no TCP connection yet.
        // The HsmsStateChanged → Selected handler will set Connected and send S1F13 when a peer connects.
        if (_transport.IsConnected)
        {
            ChangeState(ProtocolState.Connected);
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        // Swap out the cts before disconnecting; senders that snapshot _cts.Token will see
        // a cancelled token rather than risk an ObjectDisposedException after we dispose it.
        var cts = _cts;
        _cts = null;
        try { cts?.Cancel(); } catch (ObjectDisposedException) { }

        try { await _transport.DisconnectAsync(); }
        finally { cts?.Dispose(); }

        ChangeState(ProtocolState.Disconnected);
    }

    public async Task SendAsync(ProtocolMessage message, CancellationToken ct)
    {
        if (!IsConnected())
            throw new InvalidOperationException("Not connected");

        var secsMsg = ProtocolMessageToSecs(message);
        if (secsMsg == null)
            throw new ArgumentException("Cannot convert protocol message to SECS message");

        var hsmsMsg = HsmsMessage.CreateDataMessage(secsMsg,
            _transport.Connection!.SessionId,
            secsMsg.SystemBytes);

        await _transport.Connection.SendHsmsAsync(hsmsMsg, ct);

        var sentMsg = SecsToProtocolMessage(secsMsg, MessageDirection.Send);
        _logger.LogInformation(">> {MessageId}: {Content}", sentMsg.Id, sentMsg.Description);
        MessageSent?.Invoke(this, new ProtocolMessageEventArgs(sentMsg, MessageDirection.Send));
    }

    /// <summary>
    /// Send a raw SECS message directly.
    /// </summary>
    public async Task SendSecsMessageAsync(SecsMessage secsMsg, CancellationToken ct)
    {
        if (_transport.Connection == null)
            throw new InvalidOperationException("Not connected");

        // Fire MessageSent BEFORE sending, so the log entry appears immediately.
        // SendDataMessageAsync blocks when W-bit=true (waiting for T3 reply up to 45s),
        // which would otherwise delay the log entry until the reply arrives.
        var sentMsg = SecsToProtocolMessage(secsMsg, MessageDirection.Send);
        _logger.LogInformation(">> {MessageId}: {Content}", sentMsg.Id, sentMsg.Description);
        MessageSent?.Invoke(this, new ProtocolMessageEventArgs(sentMsg, MessageDirection.Send));

        await _transport.Connection.SendDataMessageAsync(secsMsg, secsMsg.WBit, ct);
    }

    /// <summary>
    /// Send S1F13 (Establish Communication) to initiate GEM online sequence.
    /// </summary>
    public async Task SendEstablishCommunicationAsync(CancellationToken ct)
    {
        var s1f13 = new SecsMessage(1, 13, true);
        // Send fire-and-forget: don't wait for T3 timeout if equipment doesn't reply S1F14
        try
        {
            await SendSecsMessageAsync(s1f13, ct);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("S1F13 timeout — peer did not reply S1F14");
        }

        _equipmentModel.GemStateMachine.TryTrigger(GemEvent.StartCommunication);
    }

    /// <summary>
    /// Send S5F1 (Alarm Report).
    /// </summary>
    public async Task SendAlarmReportAsync(ushort alid, bool set, CancellationToken ct)
    {
        var alarm = _equipmentModel.GetAlarm(alid);
        if (alarm != null) alarm.IsSet = set;

        var s5f1 = new SecsMessage(5, 1, true,
            SecsItem.L(
                SecsItem.U1((byte)(set ? 1 : 0)),  // ALED
                SecsItem.U2(alid),                   // ALID
                SecsItem.A(alarm?.Name ?? "UNKNOWN"), // ALCD
                SecsItem.A(alarm?.Description ?? "")
            ));
        await SendSecsMessageAsync(s5f1, ct);
    }

    /// <summary>
    /// Send S6F11 (Collection Event Report).
    /// </summary>
    public async Task SendCollectionEventAsync(ushort ceid, Dictionary<ushort, string> variables, CancellationToken ct)
    {
        var svItems = variables.Select(v =>
            SecsItem.L(
                SecsItem.U2(v.Key),
                SecsItem.A(v.Value)
            )).ToArray();

        var s6f11 = new SecsMessage(6, 11, true,
            SecsItem.L(
                SecsItem.U4(0),        // DATAID
                SecsItem.U2(ceid),     // CEID
                SecsItem.L(             // RPT
                    SecsItem.L(svItems)
                )
            ));
        await SendSecsMessageAsync(s6f11, ct);
    }

    public bool IsConnected() =>
        _transport.IsConnected &&
        _transport.Connection?.StateMachine.State == HsmsState.Selected;

    private void ChangeState(ProtocolState newState)
    {
        if (_state == newState) return;
        var oldState = _state;
        _state = newState;
        _logger.LogInformation("Protocol state: {Old} -> {New}", oldState, newState);
        StateChanged?.Invoke(this, new ProtocolStateEventArgs(oldState, newState));
    }

    private static ProtocolMessage SecsToProtocolMessage(SecsMessage msg, MessageDirection direction)
    {
        return new ProtocolMessage
        {
            Id = msg.MessageId,
            Name = msg.MessageId,
            Description = msg.ToFormattedString(),
            Fields = new Dictionary<string, object?>
            {
                ["Stream"] = msg.Stream,
                ["Function"] = msg.Function,
                ["WBit"] = msg.WBit,
                ["SystemBytes"] = msg.SystemBytes,
                ["SessionId"] = msg.SessionId,
                ["Items"] = msg.RootItem,
            },
        };
    }

    private static SecsMessage? ProtocolMessageToSecs(ProtocolMessage msg)
    {
        var stream = msg.GetField<byte>("Stream");
        var function = msg.GetField<byte>("Function");
        if (stream == 0 && function == 0) return null;

        return new SecsMessage
        {
            Stream = stream,
            Function = function,
            WBit = msg.GetField<bool>("WBit"),
            SystemBytes = msg.GetField<uint>("SystemBytes"),
            SessionId = msg.GetField<uint>("SessionId"),
            RootItem = msg.GetField<SecsItem>("Items"),
        };
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        await _transport.DisposeAsync();
    }
}
