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
                    // Authorization check: when ExpectedAuthorization is configured, the
                    // request's header must match verbatim. Mismatch → 401 + skip dispatch
                    // so neither MessageReceived nor any downstream consumer sees the body.
                    if (!string.IsNullOrEmpty(_config.ExpectedAuthorization))
                    {
                        var auth = context.Request.Headers["Authorization"];
                        if (!string.Equals(auth, _config.ExpectedAuthorization, StringComparison.Ordinal))
                        {
                            context.Response.StatusCode = 401;
                            context.Response.AddHeader("WWW-Authenticate", "Bearer");
                            var body401 = Encoding.UTF8.GetBytes("Unauthorized");
                            try { await context.Response.OutputStream.WriteAsync(body401, ct); }
                            catch { }
                            context.Response.Close();
                            _logger.LogWarning("HTTP listener: rejected POST with bad Authorization");
                            continue;
                        }
                    }

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

    /// <summary>
    /// Probe the configured endpoint to verify reachability AND that any configured auth
    /// headers are accepted. Independent of <see cref="ConnectAsync"/>; does not mutate
    /// <see cref="IsConnected"/>. Returns (success, statusCode?, message).
    ///
    /// Active mode strategy: send OPTIONS first (cheapest, doesn't change server state).
    /// If the server returns 405/501 we retry with HEAD, then GET — many MES endpoints
    /// only accept POST and reject the verb itself, which still proves reachability.
    /// 2xx → success. 401/403 → auth failure (token wrong). 5xx → server error.
    /// 4xx other than 401/403 → endpoint reachable, treat as success with status reported.
    /// Network exception (DNS/refused/timeout) → failure with the underlying message.
    ///
    /// Passive mode strategy: try to bind a temporary <see cref="HttpListener"/> on the
    /// configured prefix and immediately stop. Surfaces "port in use" / "ACL denied"
    /// without leaving any listener running.
    /// </summary>
    public async Task<(bool ok, int? status, string message)> ProbeAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_config.HttpUrl))
            return (false, null, "URL 为空");

        if (!_config.IsActiveMode)
            return ProbePassive();

        return await ProbeActiveAsync(ct).ConfigureAwait(false);
    }

    private async Task<(bool ok, int? status, string message)> ProbeActiveAsync(CancellationToken ct)
    {
        using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        foreach (var header in _config.HttpHeaders)
            probe.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);

        // Try OPTIONS, then HEAD, then GET. Stop at the first method that gets a real
        // HTTP response (any status); fall through only on 405 / 501 "method not supported".
        foreach (var method in new[] { HttpMethod.Options, HttpMethod.Head, HttpMethod.Get })
        {
            try
            {
                using var req = new HttpRequestMessage(method, _config.HttpUrl);
                using var res = await probe.SendAsync(req, ct).ConfigureAwait(false);
                var code = (int)res.StatusCode;

                if (code is 405 or 501)
                    continue; // try next verb

                return InterpretStatus(code, res.ReasonPhrase ?? "");
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                return (false, null, "请求超时");
            }
            catch (HttpRequestException ex)
            {
                // DNS / refused / TLS — don't bother trying other verbs, the host is bad.
                return (false, null, ex.Message);
            }
        }

        return (false, null, "服务器对 OPTIONS / HEAD / GET 都拒绝了");
    }

    private static (bool ok, int? status, string message) InterpretStatus(int code, string reason)
    {
        var label = string.IsNullOrEmpty(reason) ? code.ToString() : $"{code} {reason}";
        return code switch
        {
            >= 200 and < 300 => (true, code, $"端点可达 ({label})"),
            401 => (false, code, $"鉴权失败 ({label}) — 检查 Authorization 头"),
            403 => (false, code, $"权限不足 ({label})"),
            >= 500 => (false, code, $"服务器错误 ({label})"),
            // 4xx other than auth: server is up but rejected the probe verb — still useful.
            _ => (true, code, $"端点可达但返回 {label}"),
        };
    }

    private (bool ok, int? status, string message) ProbePassive()
    {
        var prefix = _config.HttpUrl.EndsWith('/') ? _config.HttpUrl : _config.HttpUrl + "/";
        HttpListener? listener = null;
        try
        {
            listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            listener.Start();
            return (true, null, $"可监听 {prefix}");
        }
        catch (HttpListenerException ex)
        {
            return (false, null, $"无法绑定 {prefix}: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
        finally
        {
            try { listener?.Stop(); } catch { }
            try { listener?.Close(); } catch { }
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
