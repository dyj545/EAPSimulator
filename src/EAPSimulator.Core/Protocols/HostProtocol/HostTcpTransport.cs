using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace EAPSimulator.Core.Protocols.HostProtocol;

/// <summary>
/// TCP transport for Host protocol communication with MES.
/// Supports both Active (client) and Passive (server) connection modes.
/// Messages are newline-delimited JSON strings.
/// </summary>
public class HostTcpTransport : IHostTransport
{
    private readonly ILogger _logger;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _receiveCts;
    private HostTransportConfig _config = new();

    public TransportType TransportType => TransportType.Tcp;
    public bool IsConnected => _client?.Connected == true;
    public string? Endpoint { get; private set; }

    public event EventHandler<string>? MessageReceived;
    public event EventHandler<string?>? Disconnected;

    public HostTcpTransport(ILogger logger)
    {
        _logger = logger;
    }

    public void Configure(HostTransportConfig config)
    {
        _config = config;
    }

    public async Task ConnectAsync(CancellationToken ct)
    {
        if (IsConnected) return;

        try
        {
            if (_config.IsActiveMode)
                await ConnectAsActiveAsync(ct);
            else
                await ConnectAsPassiveAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Host TCP connect failed");
            throw;
        }
    }

    private async Task ConnectAsActiveAsync(CancellationToken ct)
    {
        _logger.LogInformation("Host connecting to {Host}:{Port} (Active)", _config.RemoteHost, _config.RemotePort);
        _client = new TcpClient();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        await _client.ConnectAsync(_config.RemoteHost, _config.RemotePort, linkedCts.Token);
        _stream = _client.GetStream();
        Endpoint = $"{_config.RemoteHost}:{_config.RemotePort}";
        StartReceiving();
        _logger.LogInformation("Host connected to {Endpoint}", Endpoint);
    }

    private async Task ConnectAsPassiveAsync(CancellationToken ct)
    {
        _logger.LogInformation("Host listening on {Host}:{Port} (Passive)", _config.LocalHost, _config.LocalPort);
        var listener = new TcpListener(IPAddress.Parse(_config.LocalHost), _config.LocalPort);
        listener.Start();

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            _client = await listener.AcceptTcpClientAsync(linkedCts.Token);
            _stream = _client.GetStream();
            Endpoint = _client.Client.RemoteEndPoint?.ToString();
            StartReceiving();
            _logger.LogInformation("Host accepted connection from {Endpoint}", Endpoint);
        }
        finally
        {
            listener.Stop();
        }
    }

    private void StartReceiving()
    {
        _receiveCts = new CancellationTokenSource();
        _ = Task.Run(() => ReceiveLoop(_receiveCts.Token));
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        var buffer = new byte[8192];
        var messageBuffer = new StringBuilder();
        try
        {
            while (!ct.IsCancellationRequested && _stream != null)
            {
                var bytesRead = await _stream.ReadAsync(buffer, ct);
                if (bytesRead == 0)
                {
                    _logger.LogInformation("Host connection closed by remote");
                    break;
                }

                var chunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                messageBuffer.Append(chunk);

                // Process complete messages (newline-delimited)
                var content = messageBuffer.ToString();
                int newlineIndex;
                while ((newlineIndex = content.IndexOf('\n')) >= 0)
                {
                    var message = content[..newlineIndex].Trim();
                    content = content[(newlineIndex + 1)..];
                    if (!string.IsNullOrEmpty(message))
                        MessageReceived?.Invoke(this, message);
                }
                messageBuffer.Clear();
                messageBuffer.Append(content);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Host receive error");
        }
        finally
        {
            Disconnected?.Invoke(this, "Connection lost");
        }
    }

    public async Task SendAsync(string message, CancellationToken ct)
    {
        if (_stream == null)
            throw new InvalidOperationException("Not connected");

        var data = Encoding.UTF8.GetBytes(message + "\n");
        await _stream.WriteAsync(data, ct);
        await _stream.FlushAsync(ct);
    }

    public async Task DisconnectAsync()
    {
        _receiveCts?.Cancel();
        _stream?.Close();
        _client?.Close();
        _stream = null;
        _client = null;
        Endpoint = null;
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _receiveCts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
