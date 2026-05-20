using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace EAPSimulator.Core.Protocols.Custom;

/// <summary>
/// TCP transport for custom protocols.
/// Supports configurable framing: delimiter-based or length-prefix.
/// </summary>
public class CustomTransport : ITransport
{
    private readonly ILogger<CustomTransport> _logger;
    private readonly ProtocolDefinition _protocol;
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private TcpListener? _listener;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly bool _isServer;

    public bool IsConnected => _tcpClient?.Connected == true;
    public string? RemoteEndpoint { get; private set; }

    public event EventHandler<byte[]>? DataReceived;
    public event EventHandler<string?>? Disconnected;

    public CustomTransport(ILogger<CustomTransport> logger, ProtocolDefinition protocol, string host, int port, bool isServer = false)
    {
        _logger = logger;
        _protocol = protocol;
        _isServer = isServer;
        Host = host;
        Port = port;
    }

    public string Host { get; }
    public int Port { get; }

    public async Task ConnectAsync(CancellationToken ct)
    {
        if (_isServer)
            await StartServer(ct);
        else
            await ConnectClient(ct);
    }

    private async Task ConnectClient(CancellationToken ct)
    {
        _logger.LogInformation("Custom transport: connecting to {Host}:{Port}", Host, Port);
        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(Host, Port);
        _stream = _tcpClient.GetStream();
        RemoteEndpoint = $"{Host}:{Port}";
        StartReceiveLoop();
        _logger.LogInformation("Custom transport: connected");
    }

    private Task StartServer(CancellationToken ct)
    {
        _logger.LogInformation("Custom transport: listening on {Host}:{Port}", Host, Port);
        _listener = new TcpListener(IPAddress.Parse(Host), Port);
        _listener.Start();

        _ = Task.Run(async () =>
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                _tcpClient = client;
                _stream = client.GetStream();
                RemoteEndpoint = client.Client.RemoteEndPoint?.ToString();
                StartReceiveLoop();
                _logger.LogInformation("Custom transport: accepted connection from {Remote}", RemoteEndpoint);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Custom transport: accept error");
            }
        }, ct);

        return Task.CompletedTask;
    }

    private void StartReceiveLoop()
    {
        _receiveCts = new CancellationTokenSource();
        _receiveTask = Task.Run(() => ReceiveLoop(_receiveCts.Token));
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        try
        {
            if (_stream == null) return;
            var buffer = new byte[8192];

            while (!ct.IsCancellationRequested)
            {
                if (_protocol.Framing == FramingType.Delimiter)
                {
                    // Delimiter-based: read until delimiter found
                    var received = new List<byte>();
                    while (true)
                    {
                        int b = _stream.ReadByte();
                        if (b == -1) goto Closed;
                        received.Add((byte)b);

                        var text = Encoding.UTF8.GetString(received.ToArray());
                        if (text.EndsWith(_protocol.Delimiter))
                        {
                            break;
                        }
                    }

                    if (received.Count > 0)
                        DataReceived?.Invoke(this, received.ToArray());
                }
                else // LengthPrefix
                {
                    // Read 4-byte length prefix
                    var lenBuf = new byte[4];
                    int read = 0;
                    while (read < 4)
                    {
                        int n = await _stream.ReadAsync(lenBuf, read, 4 - read, ct);
                        if (n == 0) goto Closed;
                        read += n;
                    }

                    int msgLen = (lenBuf[0] << 24) | (lenBuf[1] << 16) | (lenBuf[2] << 8) | lenBuf[3];
                    if (msgLen <= 0 || msgLen > 1024 * 1024) // sanity check
                    {
                        _logger.LogWarning("Invalid message length: {Length}", msgLen);
                        continue;
                    }

                    var msgData = new byte[msgLen];
                    read = 0;
                    while (read < msgLen)
                    {
                        int n = await _stream.ReadAsync(msgData, read, msgLen - read, ct);
                        if (n == 0) goto Closed;
                        read += n;
                    }

                    // Include the 4-byte length prefix in the data
                    var fullData = lenBuf.Concat(msgData).ToArray();
                    DataReceived?.Invoke(this, fullData);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Custom transport receive error");
        }

    Closed:
        Disconnected?.Invoke(this, "Connection closed");
    }

    public async Task SendAsync(byte[] data, CancellationToken ct)
    {
        if (_stream == null) throw new InvalidOperationException("Not connected");

        await _sendLock.WaitAsync(ct);
        try
        {
            await _stream.WriteAsync(data, ct);
            await _stream.FlushAsync(ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task SendMessageAsync(string message, CancellationToken ct)
    {
        var encoding = _protocol.Encoding == "UTF-8" ? Encoding.UTF8 : Encoding.ASCII;
        byte[] data;

        if (_protocol.Framing == FramingType.Delimiter)
        {
            data = encoding.GetBytes(message + _protocol.Delimiter);
        }
        else
        {
            var msgBytes = encoding.GetBytes(message);
            var lenPrefix = new byte[]
            {
                (byte)(msgBytes.Length >> 24),
                (byte)(msgBytes.Length >> 16),
                (byte)(msgBytes.Length >> 8),
                (byte)msgBytes.Length
            };
            data = lenPrefix.Concat(msgBytes).ToArray();
        }

        await SendAsync(data, ct);
    }

    public async Task DisconnectAsync()
    {
        _receiveCts?.Cancel();
        _stream?.Close();
        _tcpClient?.Close();
        _listener?.Stop();

        if (_receiveTask != null)
            await _receiveTask;

        Disconnected?.Invoke(this, "Disconnected");
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _sendLock.Dispose();
        _receiveCts?.Dispose();
    }
}
