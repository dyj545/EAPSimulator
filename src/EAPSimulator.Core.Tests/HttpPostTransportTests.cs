using System.Net;
using System.Net.Http;
using System.Text;
using EAPSimulator.Core.Protocols.HostProtocol;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EAPSimulator.Core.Tests;

/// <summary>
/// HTTP transport probe + listener auth tests. Each test spins up a local
/// <see cref="HttpListener"/> on a free port and tears it down on completion.
/// </summary>
public class HttpPostTransportTests
{
    /// <summary>Pick a free TCP port by binding 0 and reading back what the OS assigned.</summary>
    private static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        var port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    /// <summary>
    /// Mini HTTP server: every request goes through <paramref name="handler"/>, which
    /// writes the response. Returns disposable that stops the listener.
    /// </summary>
    private static IDisposable StartLocalServer(string prefix, Action<HttpListenerContext> handler, out CancellationTokenSource cts)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();
        var localCts = new CancellationTokenSource();
        cts = localCts;
        _ = Task.Run(async () =>
        {
            try
            {
                while (!localCts.IsCancellationRequested)
                {
                    var ctx = await listener.GetContextAsync();
                    try { handler(ctx); } catch { }
                }
            }
            catch { }
        });
        return new DisposableAction(() =>
        {
            localCts.Cancel();
            try { listener.Stop(); } catch { }
            try { listener.Close(); } catch { }
        });
    }

    private sealed class DisposableAction : IDisposable
    {
        private readonly Action _a;
        public DisposableAction(Action a) { _a = a; }
        public void Dispose() => _a();
    }

    [Fact]
    public async Task ProbeAsync_2xx_ReturnsSuccess()
    {
        var port = FreePort();
        var url = $"http://127.0.0.1:{port}/probe/";
        using var server = StartLocalServer(url, ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.Close();
        }, out _);

        var transport = new HttpPostTransport(NullLogger.Instance);
        transport.Configure(new HostTransportConfig { TransportType = TransportType.HttpPost, IsActiveMode = true, HttpUrl = url });
        var (ok, status, msg) = await transport.ProbeAsync(CancellationToken.None);
        Assert.True(ok);
        Assert.Equal(200, status);
        Assert.Contains("可达", msg);
    }

    [Fact]
    public async Task ProbeAsync_401_FailsWithAuthMessage()
    {
        var port = FreePort();
        var url = $"http://127.0.0.1:{port}/probe/";
        using var server = StartLocalServer(url, ctx =>
        {
            ctx.Response.StatusCode = 401;
            ctx.Response.Close();
        }, out _);

        var transport = new HttpPostTransport(NullLogger.Instance);
        transport.Configure(new HostTransportConfig { TransportType = TransportType.HttpPost, IsActiveMode = true, HttpUrl = url });
        var (ok, status, msg) = await transport.ProbeAsync(CancellationToken.None);
        Assert.False(ok);
        Assert.Equal(401, status);
        Assert.Contains("鉴权", msg);
    }

    [Fact]
    public async Task ProbeAsync_5xx_FailsWithServerError()
    {
        var port = FreePort();
        var url = $"http://127.0.0.1:{port}/probe/";
        using var server = StartLocalServer(url, ctx =>
        {
            ctx.Response.StatusCode = 503;
            ctx.Response.Close();
        }, out _);

        var transport = new HttpPostTransport(NullLogger.Instance);
        transport.Configure(new HostTransportConfig { TransportType = TransportType.HttpPost, IsActiveMode = true, HttpUrl = url });
        var (ok, status, msg) = await transport.ProbeAsync(CancellationToken.None);
        Assert.False(ok);
        Assert.Equal(503, status);
        Assert.Contains("服务器错误", msg);
    }

    [Fact]
    public async Task ProbeAsync_405ThenFallbackToGet_StillSucceeds()
    {
        // Server returns 405 to OPTIONS/HEAD then 200 to GET — proves verb fallback.
        var port = FreePort();
        var url = $"http://127.0.0.1:{port}/probe/";
        using var server = StartLocalServer(url, ctx =>
        {
            if (ctx.Request.HttpMethod == "GET")
            {
                ctx.Response.StatusCode = 200;
            }
            else
            {
                ctx.Response.StatusCode = 405;
            }
            ctx.Response.Close();
        }, out _);

        var transport = new HttpPostTransport(NullLogger.Instance);
        transport.Configure(new HostTransportConfig { TransportType = TransportType.HttpPost, IsActiveMode = true, HttpUrl = url });
        var (ok, status, msg) = await transport.ProbeAsync(CancellationToken.None);
        Assert.True(ok);
        Assert.Equal(200, status);
    }

    [Fact]
    public async Task ProbeAsync_RefusedConnection_FailsWithNetworkMessage()
    {
        // Pick a port and DON'T bind anything — connect should refuse instantly.
        var port = FreePort();
        var url = $"http://127.0.0.1:{port}/dead/";
        var transport = new HttpPostTransport(NullLogger.Instance);
        transport.Configure(new HostTransportConfig { TransportType = TransportType.HttpPost, IsActiveMode = true, HttpUrl = url });
        var (ok, status, msg) = await transport.ProbeAsync(CancellationToken.None);
        Assert.False(ok);
        Assert.Null(status);
        Assert.False(string.IsNullOrEmpty(msg));
    }

    [Fact]
    public async Task ProbeAsync_PassiveMode_OpenPort_Succeeds()
    {
        var port = FreePort();
        var url = $"http://127.0.0.1:{port}/listen/";
        var transport = new HttpPostTransport(NullLogger.Instance);
        transport.Configure(new HostTransportConfig { TransportType = TransportType.HttpPost, IsActiveMode = false, HttpUrl = url });
        var (ok, status, msg) = await transport.ProbeAsync(CancellationToken.None);
        Assert.True(ok);
        Assert.Null(status);
        Assert.Contains("可监听", msg);
    }

    [Fact]
    public async Task Listener_AcceptsRequestWhenAuthMatches()
    {
        var port = FreePort();
        var url = $"http://127.0.0.1:{port}/listen/";
        var transport = new HttpPostTransport(NullLogger.Instance);
        transport.Configure(new HostTransportConfig
        {
            TransportType = TransportType.HttpPost,
            IsActiveMode = false,
            HttpUrl = url,
            ExpectedAuthorization = "Bearer test-token",
        });
        string? received = null;
        transport.MessageReceived += (_, body) => received = body;
        await transport.ConnectAsync(CancellationToken.None);
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer test-token");
            var resp = await client.PostAsync(url, new StringContent("hello", Encoding.UTF8, "application/json"));
            Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
            // Listener dispatches MessageReceived asynchronously; spin briefly.
            for (int i = 0; i < 50 && received == null; i++) await Task.Delay(20);
            Assert.Equal("hello", received);
        }
        finally
        {
            await transport.DisposeAsync();
        }
    }

    [Fact]
    public async Task Listener_Returns401WhenAuthMismatches()
    {
        var port = FreePort();
        var url = $"http://127.0.0.1:{port}/listen/";
        var transport = new HttpPostTransport(NullLogger.Instance);
        transport.Configure(new HostTransportConfig
        {
            TransportType = TransportType.HttpPost,
            IsActiveMode = false,
            HttpUrl = url,
            ExpectedAuthorization = "Bearer good",
        });
        string? received = null;
        transport.MessageReceived += (_, body) => received = body;
        await transport.ConnectAsync(CancellationToken.None);
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer wrong");
            var resp = await client.PostAsync(url, new StringContent("hello"));
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, resp.StatusCode);
            // Body must NOT be dispatched on a rejected request.
            await Task.Delay(100);
            Assert.Null(received);
        }
        finally
        {
            await transport.DisposeAsync();
        }
    }

    [Fact]
    public async Task Listener_AcceptsRequestWhenExpectedAuthEmpty()
    {
        var port = FreePort();
        var url = $"http://127.0.0.1:{port}/listen/";
        var transport = new HttpPostTransport(NullLogger.Instance);
        transport.Configure(new HostTransportConfig
        {
            TransportType = TransportType.HttpPost,
            IsActiveMode = false,
            HttpUrl = url,
            ExpectedAuthorization = "",  // explicit: no check
        });
        string? received = null;
        transport.MessageReceived += (_, body) => received = body;
        await transport.ConnectAsync(CancellationToken.None);
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            // No Authorization header at all.
            var resp = await client.PostAsync(url, new StringContent("anonymous"));
            Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
            for (int i = 0; i < 50 && received == null; i++) await Task.Delay(20);
            Assert.Equal("anonymous", received);
        }
        finally
        {
            await transport.DisposeAsync();
        }
    }
}
