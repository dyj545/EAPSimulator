using System.Text;
using Microsoft.Extensions.Logging;

namespace EAPSimulator.Core.Protocols.HostProtocol;

/// <summary>
/// OPC UA transport for Host/MES communication.
/// Provides a simplified HTTP-based OPC UA endpoint client.
/// For full OPC UA client functionality (sessions, subscriptions, security),
/// add the OPCFoundation.NetStandard.Opc.Ua NuGet package.
/// </summary>
public class OpcUaTransport : IHostTransport
{
    private readonly ILogger _logger;
    private HttpClient? _httpClient;
    private CancellationTokenSource? _pollCts;
    private HostTransportConfig _config = new();

    public TransportType TransportType => TransportType.OpcUa;
    public bool IsConnected { get; private set; }
    public string? Endpoint => _config.OpcUaEndpoint;

    public event EventHandler<string>? MessageReceived;
    public event EventHandler<string?>? Disconnected;

    public OpcUaTransport(ILogger logger)
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
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30),
                BaseAddress = new Uri(_config.OpcUaEndpoint),
            };

            // Test connection by sending a simple GET to the discovery endpoint
            var response = await _httpClient.GetAsync("/.well-known/opcua", ct);
            if (response.IsSuccessStatusCode)
            {
                IsConnected = true;
                _logger.LogInformation("OPC UA transport connected to {Endpoint}", _config.OpcUaEndpoint);

                // Start polling for messages if configured
                _pollCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _ = PollLoopAsync(_pollCts.Token);
            }
            else
            {
                // Even without OPC UA discovery, mark as ready (OPC UA may not expose HTTP)
                IsConnected = true;
                _logger.LogInformation("OPC UA transport ready at {Endpoint} (no discovery endpoint)", _config.OpcUaEndpoint);
            }
        }
        catch (Exception ex)
        {
            // OPC UA servers may not have HTTP endpoints; mark as connected anyway
            // for opc.tcp:// connections, a full OPC UA client stack would be needed
            IsConnected = true;
            _logger.LogWarning(ex, "OPC UA transport: HTTP discovery failed, transport ready in passive mode at {Endpoint}", _config.OpcUaEndpoint);
        }
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(5000, ct);
                // In a full implementation, this would poll OPC UA subscriptions
                // or monitored items for new data
            }
        }
        catch (OperationCanceledException) { }
    }

    public async Task SendAsync(string message, CancellationToken ct)
    {
        if (_httpClient == null)
            throw new InvalidOperationException("Not connected");

        // Send via HTTP POST to the OPC UA endpoint (REST-style)
        // For full OPC UA, this would use the OPC UA client SDK to write to nodes
        var content = new StringContent(message, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(_config.OpcUaEndpoint, content, ct);

        if (response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            if (!string.IsNullOrEmpty(responseBody))
                MessageReceived?.Invoke(this, responseBody);
        }
    }

    public async Task DisconnectAsync()
    {
        _pollCts?.Cancel();
        _httpClient?.Dispose();
        _httpClient = null;
        IsConnected = false;
        Disconnected?.Invoke(this, "Disconnected");
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _pollCts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
