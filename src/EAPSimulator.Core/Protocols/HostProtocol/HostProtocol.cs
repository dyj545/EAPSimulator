using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace EAPSimulator.Core.Protocols.HostProtocol;

/// <summary>
/// Host protocol for MES communication.
/// Implements the IProtocol interface for generic handling.
/// Uses IHostTransport for pluggable transport backends (TCP, HTTP, MQ, etc.).
/// Messages are serialized as JSON strings.
/// </summary>
public class HostProtocol : IProtocol
{
    private readonly ILogger _logger;
    private IHostTransport? _transport;
    private CancellationTokenSource? _cts;

    public string Name => "Host";
    public ProtocolRole Role { get; set; } = ProtocolRole.Host;
    public ProtocolState State { get; private set; } = ProtocolState.Disconnected;

    /// <summary>The currently configured transport type, if any.</summary>
    public TransportType? ActiveTransportType => _transport?.TransportType;

    public event EventHandler<ProtocolMessageEventArgs>? MessageReceived;
    public event EventHandler<ProtocolMessageEventArgs>? MessageSent;
    public event EventHandler<ProtocolStateEventArgs>? StateChanged;

    /// <summary>
    /// Raised in addition to <see cref="MessageReceived"/> with the full <see cref="HostMessage"/>,
    /// not the lossy <see cref="ProtocolMessage"/> projection. Used by ScenarioEngine to
    /// match by name and field values.
    /// </summary>
    public event EventHandler<HostMessage>? HostMessageReceived;

    public HostProtocol(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Configure with a specific transport instance.
    /// </summary>
    public void Configure(IHostTransport transport)
    {
        _transport = transport;
        _transport.MessageReceived += OnMessageReceived;
        _transport.Disconnected += OnDisconnected;
    }

    /// <summary>
    /// Configure using HostTransportConfig. Creates the appropriate transport via factory.
    /// </summary>
    public void Configure(HostTransportConfig config)
    {
        var transport = HostTransportFactory.Create(config.TransportType, _logger);
        transport.Configure(config);
        Configure(transport);
    }

    /// <summary>
    /// Configure with legacy TCP parameters (backward compatible).
    /// </summary>
    public void Configure(bool isActiveMode, string remoteHost, int remotePort,
        string localHost, int localPort, int deviceId)
    {
        var config = new HostTransportConfig
        {
            TransportType = TransportType.Tcp,
            IsActiveMode = isActiveMode,
            RemoteHost = remoteHost,
            RemotePort = remotePort,
            LocalHost = localHost,
            LocalPort = localPort,
        };
        Configure(config);
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (_transport == null)
            throw new InvalidOperationException("Transport not configured");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        SetState(ProtocolState.Connecting);

        try
        {
            await _transport.ConnectAsync(_cts.Token);
            SetState(ProtocolState.Connected);
            _logger.LogInformation("Host protocol started via {Transport}", _transport.TransportType);
        }
        catch (Exception ex)
        {
            SetState(ProtocolState.Disconnected);
            _logger.LogError(ex, "Host protocol start failed");
            throw;
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (_transport != null)
        {
            await _transport.DisconnectAsync();
        }
        _cts?.Cancel();
        SetState(ProtocolState.Disconnected);
        _logger.LogInformation("Host protocol stopped");
    }

    public async Task SendAsync(ProtocolMessage message, CancellationToken ct)
    {
        if (_transport == null || !_transport.IsConnected)
            throw new InvalidOperationException("Not connected");

        var hostMsg = new HostMessage
        {
            Name = message.Name,
            Description = message.Description,
            Direction = HostMessageDirection.Send,
        };
        foreach (var (key, value) in message.Fields)
            hostMsg.Fields[key] = new HostField { Name = key, Value = value?.ToString() ?? "" };

        var json = hostMsg.ToJson();
        await _transport.SendAsync(json, ct);

        MessageSent?.Invoke(this, new ProtocolMessageEventArgs(message, MessageDirection.Send));
        _logger.LogInformation("Host sent: {Name}", message.Name);
    }

    /// <summary>
    /// Send a host message directly.
    /// </summary>
    public async Task SendHostMessageAsync(HostMessage message, CancellationToken ct)
    {
        if (_transport == null || !_transport.IsConnected)
            throw new InvalidOperationException("Not connected");

        // Use ToWireBody so Raw / custom JSON shapes go out as authored.
        var body = message.ToWireBody();
        await _transport.SendAsync(body, ct);

        var protocolMsg = message.ToProtocolMessage();
        MessageSent?.Invoke(this, new ProtocolMessageEventArgs(protocolMsg, MessageDirection.Send));
        _logger.LogInformation("Host sent: {Name}", message.Name);
    }

    private void OnMessageReceived(object? sender, string json)
    {
        try
        {
            if (string.IsNullOrEmpty(json)) return;

            var hostMsg = HostMessage.FromJson(json);
            if (hostMsg == null) return;

            var protocolMsg = hostMsg.ToProtocolMessage();
            MessageReceived?.Invoke(this, new ProtocolMessageEventArgs(protocolMsg, MessageDirection.Receive));
            HostMessageReceived?.Invoke(this, hostMsg);
            _logger.LogInformation("Host received: {Name}", hostMsg.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse host message");
        }
    }

    private void OnDisconnected(object? sender, string? reason)
    {
        SetState(ProtocolState.Disconnected);
        _logger.LogWarning("Host disconnected: {Reason}", reason);
    }

    private void SetState(ProtocolState newState)
    {
        var oldState = State;
        if (oldState == newState) return;
        State = newState;
        StateChanged?.Invoke(this, new ProtocolStateEventArgs(oldState, newState));
    }

    public async ValueTask DisposeAsync()
    {
        if (_transport != null)
        {
            _transport.MessageReceived -= OnMessageReceived;
            _transport.Disconnected -= OnDisconnected;
            await _transport.DisposeAsync();
        }
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
