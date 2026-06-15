using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;

namespace EAPSimulator.Core.Protocols.HostProtocol;

/// <summary>
/// HTTP POST transport for Host/MES communication.
/// Sends messages as HTTP POST requests, receives responses.
/// Optionally listens for incoming POST requests on a local endpoint.
/// </summary>
public class HttpPostTransport : IHostTransport
{
    private readonly ILogger _logger;
    private HttpClient? _httpClient;
    private HttpListener? _listener;
    private CancellationTokenSource? _listenCts;
    private HostTransportConfig _config = new();

    public TransportType TransportType => TransportType.HttpPost;
    public bool IsConnected { get; private set; }
    public string? Endpoint => _config.HttpUrl;

    public event EventHandler<string>? MessageReceived;
    public event EventHandler<string?>? Disconnected;

    public HttpPostTransport(ILogger logger)
    {
        _logger = logger;
    }

    public void Configure(HostTransportConfig config)
    {
        _config = config;
    }

    public async Task ConnectAsync(CancellationToken ct)
    {
        try
        {
            if (_config.IsActiveMode)
            {
                // Active: create HTTP client for sending POST requests
                _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                foreach (var header in _config.HttpHeaders)
                    _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
                IsConnected = true;
                _logger.LogInformation("HTTP POST transport ready, endpoint: {Url}", _config.HttpUrl);
            }
            else
            {
                // Passive: start HTTP listener to receive POST requests
                _listener = new HttpListener();
                var prefix = _config.HttpUrl.EndsWith('/') ? _config.HttpUrl : _config.HttpUrl + "/";
                _listener.Prefixes.Add(prefix);
                _listener.Start();
                IsConnected = true;
                _listenCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _ = Task.Run(() => ListenLoop(_listenCts.Token));
                _logger.LogInformation("HTTP POST listener started on {Url}", prefix);
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HTTP POST transport connect failed");
            throw;
        }
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _listener != null)
            {
                var context = await _listener.GetContextAsync();
                if (context.Request.HttpMethod == "POST")
                {
                    using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                    var body = await reader.ReadToEndAsync(ct);
                    if (!string.IsNullOrEmpty(body))
                        MessageReceived?.Invoke(this, body);

                    // Send 200 OK response
                    context.Response.StatusCode = 200;
                    var responseBytes = Encoding.UTF8.GetBytes("OK");
                    await context.Response.OutputStream.WriteAsync(responseBytes, ct);
                }
                context.Response.Close();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is not HttpListenerException)
        {
            _logger.LogError(ex, "HTTP POST listener error");
        }
    }

    public async Task SendAsync(string message, CancellationToken ct)
    {
        if (_httpClient == null)
            throw new InvalidOperationException("Not configured for sending");

        var content = new StringContent(message, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(_config.HttpUrl, content, ct);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync(ct);
        if (!string.IsNullOrEmpty(responseBody))
            MessageReceived?.Invoke(this, responseBody);
    }

    public async Task DisconnectAsync()
    {
        _listenCts?.Cancel();
        _listener?.Stop();
        _listener = null;
        _httpClient?.Dispose();
        _httpClient = null;
        IsConnected = false;
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _listenCts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
