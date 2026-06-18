using System.Text;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;

namespace EAPSimulator.Core.Protocols.HostProtocol;

/// <summary>
/// gRPC transport for Host/MES communication.
/// Uses generic unary RPC calls for request/response messaging.
/// The actual .proto service definition should be configured per deployment.
/// </summary>
public class GrpcTransport : IHostTransport
{
    private readonly ILogger _logger;
    private HostTransportConfig _config = new();
    private GrpcChannel? _channel;
    private HttpClient? _httpClient;

    public TransportType TransportType => TransportType.Grpc;
    public bool IsConnected => _channel != null;
    public string? Endpoint => _config.GrpcEndpoint;

    public event EventHandler<string>? MessageReceived;
    public event EventHandler<string?>? Disconnected;

    public GrpcTransport(ILogger logger)
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
            // Create channel - for unauthenticated connections
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30),
            };
            _channel = GrpcChannel.ForAddress(_config.GrpcEndpoint, new GrpcChannelOptions
            {
                HttpClient = _httpClient,
            });

            _logger.LogInformation("gRPC transport ready, endpoint: {Endpoint}", _config.GrpcEndpoint);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "gRPC connect failed");
            throw;
        }
    }

    /// <summary>
    /// Send a message via gRPC. This is a generic implementation that sends
    /// the message as a UTF-8 byte payload. For production use, replace with
    /// a proper .proto-generated client stub.
    /// </summary>
    public async Task SendAsync(string message, CancellationToken ct)
    {
        if (_channel == null)
            throw new InvalidOperationException("Not connected");

        // Generic implementation: use HttpClient to POST JSON to gRPC-gateway
        // or a custom REST bridge. For native gRPC, a .proto definition is needed.
        var content = new StringContent(message, Encoding.UTF8, "application/json");
        HttpResponseMessage response;
        try
        {
            response = await _httpClient!.PostAsync(_config.GrpcEndpoint, content, ct);
            response.EnsureSuccessStatusCode();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Disconnected?.Invoke(this, ex.Message);
            throw;
        }

        var responseBody = await response.Content.ReadAsStringAsync(ct);
        if (!string.IsNullOrEmpty(responseBody))
            MessageReceived?.Invoke(this, responseBody);
    }

    public async Task DisconnectAsync()
    {
        _channel?.Dispose();
        _httpClient?.Dispose();
        _channel = null;
        _httpClient = null;
        Disconnected?.Invoke(this, "Disconnected");
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        GC.SuppressFinalize(this);
    }
}
