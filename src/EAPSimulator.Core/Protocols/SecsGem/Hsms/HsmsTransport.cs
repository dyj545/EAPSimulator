using System.Net;
using System.Net.Sockets;
using EAPSimulator.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace EAPSimulator.Core.Protocols.SecsGem.Hsms;

/// <summary>
/// HSMS transport: manages TCP connection lifecycle, supports Active (client) and Passive (server) modes.
/// Implements ITransport for protocol-level abstraction.
/// </summary>
public class HsmsTransport : ITransport
{
    private readonly ILogger<HsmsTransport> _logger;
    private readonly HsmsSettings _settings;
    private HsmsConnection? _connection;
    private TcpListener? _listener;
    private CancellationTokenSource? _listenerCts;
    private Task? _listenerTask;

    public bool IsConnected => _connection?.IsConnected == true;
    public string? RemoteEndpoint => _connection?.RemoteEndpoint;
    public HsmsConnection? Connection => _connection;

    public event EventHandler<byte[]>? DataReceived;
    public event EventHandler<string?>? Disconnected;

    public event EventHandler<HsmsMessage>? HsmsMessageReceived;
    public event EventHandler<HsmsMessage>? HsmsMessageSent;
    public event EventHandler<(HsmsState OldState, HsmsState NewState)>? HsmsStateChanged;

    public HsmsTransport(ILogger<HsmsTransport> logger, HsmsSettings settings)
    {
        _logger = logger;
        _settings = settings;
    }

    public async Task ConnectAsync(CancellationToken ct)
    {
        switch (_settings.ConnectionMode)
        {
            case Core.Configuration.ConnectionMode.Active:
                await ConnectAsActive(ct);
                break;
            case Core.Configuration.ConnectionMode.Passive:
                await ConnectAsPassive(ct);
                break;
            case Core.Configuration.ConnectionMode.Alternating:
                await ConnectAsAlternating(ct);
                break;
        }
    }

    private async Task ConnectAsActive(CancellationToken ct)
    {
        _logger.LogInformation("HSMS Active: connecting to {Host}:{Port}", _settings.RemoteHost, _settings.RemotePort);

        var conn = new HsmsConnection(
            _logger, _settings.DeviceId,
            _settings.T3Timeout, _settings.T5Timeout, _settings.T6Timeout,
            _settings.T7Timeout, _settings.T8Timeout);

        WireConnectionEvents(conn);
        await conn.ConnectAsync(_settings.RemoteHost, _settings.RemotePort, ct);
        _connection = conn;

        _logger.LogInformation("HSMS Active: connected, sending Select");
        await conn.SendSelectAsync(ct);
    }

    private Task ConnectAsPassive(CancellationToken ct)
    {
        _logger.LogInformation("HSMS Passive: listening on {Host}:{Port}", _settings.LocalHost, _settings.LocalPort);

        _listener = new TcpListener(System.Net.IPAddress.Parse(_settings.LocalHost), _settings.LocalPort);
        _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.Start();

        _listenerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _listenerTask = Task.Run(() => AcceptLoop(_listenerCts.Token));

        return Task.CompletedTask;
    }

    private async Task ConnectAsAlternating(CancellationToken ct)
    {
        _logger.LogInformation("HSMS Alternating: listening on {LocalHost}:{LocalPort}, will connect to {RemoteHost}:{RemotePort}",
            _settings.LocalHost, _settings.LocalPort, _settings.RemoteHost, _settings.RemotePort);

        _listener = new TcpListener(System.Net.IPAddress.Parse(_settings.LocalHost), _settings.LocalPort);
        _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.Start();

        var connectionTcs = new TaskCompletionSource<HsmsConnection>();
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Accept loop: if someone connects to us first
        var acceptTask = Task.Run(async () =>
        {
            HsmsConnection? conn = null;
            try
            {
                var client = await _listener.AcceptTcpClientAsync(linkedCts.Token);
                _logger.LogInformation("HSMS Alternating: accepted inbound connection from {Remote}",
                    client.Client.RemoteEndPoint);

                conn = new HsmsConnection(
                    _logger, _settings.DeviceId,
                    _settings.T3Timeout, _settings.T5Timeout, _settings.T6Timeout,
                    _settings.T7Timeout, _settings.T8Timeout);
                WireConnectionEvents(conn);
                conn.AttachTcpClient(client);

                // If the outbound path won, our conn becomes orphaned — shut it down so
                // the receive loop, timers and TcpClient don't leak.
                if (!connectionTcs.TrySetResult(conn))
                    conn.CloseConnection();
            }
            catch (OperationCanceledException) { conn?.CloseConnection(); }
            catch (Exception ex)
            {
                conn?.CloseConnection();
                _logger.LogDebug(ex, "HSMS Alternating: accept failed");
            }
        });

        // Outbound connect attempt: wait 1s then try to connect to remote
        var outboundTask = Task.Run(async () =>
        {
            HsmsConnection? conn = null;
            try
            {
                await Task.Delay(1000, linkedCts.Token);
                _logger.LogInformation("HSMS Alternating: attempting outbound to {RemoteHost}:{RemotePort}",
                    _settings.RemoteHost, _settings.RemotePort);

                conn = new HsmsConnection(
                    _logger, _settings.DeviceId,
                    _settings.T3Timeout, _settings.T5Timeout, _settings.T6Timeout,
                    _settings.T7Timeout, _settings.T8Timeout);
                await conn.ConnectAsync(_settings.RemoteHost, _settings.RemotePort, linkedCts.Token);
                _logger.LogInformation("HSMS Alternating: outbound connected, sending Select");
                await conn.SendSelectAsync(linkedCts.Token);
                WireConnectionEvents(conn);

                // If the inbound path won, our conn becomes orphaned — shut it down so
                // the receive loop, timers and TcpClient don't leak.
                if (!connectionTcs.TrySetResult(conn))
                    conn.CloseConnection();
            }
            catch (OperationCanceledException) { conn?.CloseConnection(); }
            catch (Exception ex)
            {
                conn?.CloseConnection();
                _logger.LogDebug(ex, "HSMS Alternating: outbound connect failed");
            }
        });

        // Wait for whichever succeeds first
        _connection = await connectionTcs.Task;

        // Cancel the loser
        linkedCts.Cancel();
        _listenerCts = linkedCts;
        _listenerTask = Task.WhenAll(acceptTask, outboundTask);
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                _logger.LogInformation("HSMS Passive: accepted connection from {Remote}",
                    client.Client.RemoteEndPoint);

                // If a prior connection still exists (peer reconnected before T7/T8 fired),
                // close it before accepting the new one — otherwise the old HsmsConnection's
                // receive loop / timers / sockets leak and HsmsTransport receives events
                // from BOTH connections.
                var prev = Interlocked.Exchange(ref _connection, null);
                if (prev != null)
                {
                    _logger.LogWarning("HSMS Passive: closing prior connection before accepting new one");
                    prev.CloseConnection();
                }

                var conn = new HsmsConnection(
                    _logger, _settings.DeviceId,
                    _settings.T3Timeout, _settings.T5Timeout, _settings.T6Timeout,
                    _settings.T7Timeout, _settings.T8Timeout);

                WireConnectionEvents(conn);
                conn.AttachTcpClient(client);
                _connection = conn;
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }                       // listener stopped
            catch (SocketException ex)
                when (ex.SocketErrorCode == SocketError.OperationAborted) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HSMS Passive: accept error");
                // Avoid a tight error loop if accept keeps failing for some other reason.
                try { await Task.Delay(500, ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private void WireConnectionEvents(HsmsConnection conn)
    {
        conn.MessageReceived += (_, msg) =>
        {
            HsmsMessageReceived?.Invoke(this, msg);

            // Forward raw SECS data for Data messages
            if (msg.IsDataMessage && msg.Data != null)
            {
                // Construct full HSMS bytes for raw data event
                var fullMsg = msg.Encode();
                DataReceived?.Invoke(this, fullMsg);
            }
        };

        conn.MessageSent += (_, msg) =>
        {
            HsmsMessageSent?.Invoke(this, msg);
        };

        conn.StateMachine.StateChanged += (_, args) =>
        {
            _logger.LogInformation("HSMS state: {Old} -> {New} on {Event}",
                args.OldState, args.NewState, args.Event);
            HsmsStateChanged?.Invoke(this, (args.OldState, args.NewState));
        };

        conn.ConnectionClosed += (_, _) =>
        {
            _logger.LogInformation("HSMS connection closed");
            Disconnected?.Invoke(this, "Connection closed");
        };
    }

    public Task SendAsync(byte[] data, CancellationToken ct)
    {
        if (_connection == null)
            throw new InvalidOperationException("Not connected");

        var msg = new HsmsMessage
        {
            Header = HsmsHeader.Decode(data),
            Data = data.Length > 14 ? data[14..] : null,
        };

        return _connection.SendHsmsAsync(msg, ct);
    }

    public async Task DisconnectAsync()
    {
        if (_connection != null)
        {
            if (_connection.StateMachine.State == HsmsState.Selected)
            {
                var separate = HsmsMessage.CreateControlMessage(
                    HsmsMessageType.SeparateReq, _settings.DeviceId, 0);
                await _connection.SendHsmsAsync(separate, CancellationToken.None);
            }
            _connection.CloseConnection();
            _connection = null;
        }

        _listenerCts?.Cancel();
        _listener?.Stop();
        _listener = null;

        if (_listenerTask != null)
            await _listenerTask;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _listenerCts?.Dispose();
    }
}
