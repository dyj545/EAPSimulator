using System.Text;
using Microsoft.Extensions.Logging;

namespace EAPSimulator.Core.Protocols.Custom;

/// <summary>
/// Custom protocol implementation.
/// Uses JSON-defined message types and supports plugin-based message handling.
/// </summary>
public class CustomProtocol : IProtocol
{
    private readonly ILogger<CustomProtocol> _logger;
    private readonly CustomTransport _transport;
    private readonly ProtocolDefinition _definition;
    private readonly List<ICustomProtocolPlugin> _plugins = new();
    private readonly Dictionary<string, Func<ProtocolMessage, CancellationToken, Task<ProtocolMessage?>>> _handlers = new();
    private ProtocolState _state = ProtocolState.Disconnected;
    private CancellationTokenSource? _cts;
    private uint _messageCounter;

    public string Name => _definition.Name;
    public ProtocolRole Role { get; set; } = ProtocolRole.Equipment;
    public ProtocolState State => _state;
    public ProtocolDefinition Definition => _definition;

    public event EventHandler<ProtocolMessageEventArgs>? MessageReceived;
    public event EventHandler<ProtocolMessageEventArgs>? MessageSent;
    public event EventHandler<ProtocolStateEventArgs>? StateChanged;

    public CustomProtocol(
        ILogger<CustomProtocol> logger,
        ILogger<CustomTransport> transportLogger,
        ProtocolDefinition definition,
        string host,
        int port,
        bool isServer = false)
    {
        _logger = logger;
        _definition = definition;
        _transport = new CustomTransport(transportLogger, definition, host, port, isServer);
        WireTransportEvents();
    }

    /// <summary>
    /// Register a plugin for custom message handling.
    /// </summary>
    public void RegisterPlugin(ICustomProtocolPlugin plugin)
    {
        _plugins.Add(plugin);
        plugin.Initialize(this);
        _logger.LogInformation("Registered custom protocol plugin: {Name}", plugin.Name);
    }

    /// <summary>
    /// Register a handler for a specific message type.
    /// </summary>
    public void RegisterHandler(string messageId, Func<ProtocolMessage, CancellationToken, Task<ProtocolMessage?>> handler)
    {
        _handlers[messageId] = handler;
        _logger.LogDebug("Registered handler for message: {MessageId}", messageId);
    }

    private void WireTransportEvents()
    {
        _transport.DataReceived += async (_, data) =>
        {
            try
            {
                var encoding = _definition.Encoding == "UTF-8" ? Encoding.UTF8 : Encoding.ASCII;
                var text = encoding.GetString(data).TrimEnd('\r', '\n');

                var message = ParseMessage(text);
                if (message == null) return;

                _logger.LogInformation("<< [{Name}] {Id}: {Content}", _definition.Name, message.Id, message.Description);
                MessageReceived?.Invoke(this, new ProtocolMessageEventArgs(message, MessageDirection.Receive));

                // Try registered handlers first
                if (_handlers.TryGetValue(message.Id, out var handler) ||
                    _handlers.TryGetValue(message.Name, out handler))
                {
                    var response = await handler(message, _cts?.Token ?? CancellationToken.None);
                    if (response != null)
                    {
                        await SendAsync(response, CancellationToken.None);
                    }
                }

                // Try plugins
                foreach (var plugin in _plugins)
                {
                    var response = await plugin.HandleMessageAsync(message, _cts?.Token ?? CancellationToken.None);
                    if (response != null)
                    {
                        await SendAsync(response, CancellationToken.None);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling custom message");
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
            ChangeState(ProtocolState.Connected);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start custom protocol");
            ChangeState(ProtocolState.Disconnected);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        await _transport.DisconnectAsync();
        ChangeState(ProtocolState.Disconnected);
        _cts?.Cancel();
    }

    public async Task SendAsync(ProtocolMessage message, CancellationToken ct)
    {
        if (!_transport.IsConnected)
            throw new InvalidOperationException("Not connected");

        var text = FormatMessage(message);
        await _transport.SendMessageAsync(text, ct);

        _logger.LogInformation(">> [{Name}] {Id}: {Content}", _definition.Name, message.Id, message.Description);
        MessageSent?.Invoke(this, new ProtocolMessageEventArgs(message, MessageDirection.Send));
    }

    /// <summary>
    /// Send a raw string message.
    /// </summary>
    public async Task SendRawAsync(string text, CancellationToken ct)
    {
        await _transport.SendMessageAsync(text, ct);
    }

    /// <summary>
    /// Parse a received text message into a ProtocolMessage.
    /// Supports simple "KEY=VALUE|KEY=VALUE" format.
    /// </summary>
    private ProtocolMessage? ParseMessage(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var message = new ProtocolMessage
        {
            Id = (++_messageCounter).ToString(),
            Description = text,
        };

        // Try to find matching message definition
        var parts = text.Split('|', StringSplitOptions.RemoveEmptyEntries);
        string? msgName = null;

        foreach (var part in parts)
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2)
            {
                var key = kv[0].Trim();
                var value = kv[1].Trim();
                message.Fields[key] = value;

                // First field is typically the message name
                if (msgName == null)
                    msgName = key;
            }
        }

        if (msgName != null)
        {
            message.Name = msgName;
            var def = _definition.GetMessageByName(msgName);
            if (def != null)
            {
                message.Id = def.Id;
                message.Name = def.Name;
            }
        }

        return message;
    }

    /// <summary>
    /// Format a ProtocolMessage to a string for sending.
    /// </summary>
    private string FormatMessage(ProtocolMessage message)
    {
        if (message.Fields.Count == 0)
            return message.Description ?? message.Name;

        return string.Join("|", message.Fields.Select(kv => $"{kv.Key}={kv.Value}"));
    }

    private void ChangeState(ProtocolState newState)
    {
        if (_state == newState) return;
        var oldState = _state;
        _state = newState;
        _logger.LogInformation("Custom protocol state: {Old} -> {New}", oldState, newState);
        StateChanged?.Invoke(this, new ProtocolStateEventArgs(oldState, newState));
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        await _transport.DisposeAsync();
    }
}
