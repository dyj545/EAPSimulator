using System.Text;
using Microsoft.Extensions.Logging;
using MQTTnet;

namespace EAPSimulator.Core.Protocols.HostProtocol;

/// <summary>
/// MQTT transport for Host/MES communication.
/// Publishes and subscribes to MQTT topics for message exchange.
/// </summary>
public class MqttTransport : IHostTransport
{
    private readonly ILogger _logger;
    private HostTransportConfig _config = new();
    private IMqttClient? _client;

    public TransportType TransportType => TransportType.Mqtt;
    public bool IsConnected => _client?.IsConnected == true;
    public string? Endpoint => $"{_config.MqttBroker}:{_config.MqttPort}";

    public event EventHandler<string>? MessageReceived;
    public event EventHandler<string?>? Disconnected;

    public MqttTransport(ILogger logger)
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
            var factory = new MqttClientFactory();
            _client = factory.CreateMqttClient();

            var clientId = _config.MqttClientId ?? $"eap-{Guid.NewGuid():N8}";
            var builder = new MqttClientOptionsBuilder()
                .WithTcpServer(_config.MqttBroker, _config.MqttPort)
                .WithClientId(clientId)
                .WithCleanSession();

            // Anonymous when MqttUsername is empty/null. Some brokers (EMQX with anonymous=false)
            // would otherwise reject the connect with ConnAck=NotAuthorized.
            if (!string.IsNullOrEmpty(_config.MqttUsername))
                builder = builder.WithCredentials(_config.MqttUsername, _config.MqttPassword ?? "");

            var options = builder.Build();

            _client.ApplicationMessageReceivedAsync += OnMessageReceived;
            _client.DisconnectedAsync += args =>
            {
                Disconnected?.Invoke(this, args.Reason.ToString());
                return Task.CompletedTask;
            };

            await _client.ConnectAsync(options, ct);

            // Subscribe to topic
            var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(f => f.WithTopic(_config.MqttTopic))
                .Build();
            await _client.SubscribeAsync(subscribeOptions, ct);

            _logger.LogInformation("MQTT connected to {Broker}:{Port}, topic: {Topic}",
                _config.MqttBroker, _config.MqttPort, _config.MqttTopic);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MQTT connect failed");
            throw;
        }
    }

    private Task OnMessageReceived(MqttApplicationMessageReceivedEventArgs args)
    {
        try
        {
            var payload = Encoding.UTF8.GetString(args.ApplicationMessage.Payload.FirstSpan);
            if (!string.IsNullOrEmpty(payload))
                MessageReceived?.Invoke(this, payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MQTT message receive error");
        }
        return Task.CompletedTask;
    }

    public async Task SendAsync(string message, CancellationToken ct)
    {
        if (_client == null || !_client.IsConnected)
            throw new InvalidOperationException("Not connected");

        var mqttMsg = new MqttApplicationMessageBuilder()
            .WithTopic(_config.MqttTopic)
            .WithPayload(Encoding.UTF8.GetBytes(message))
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        try
        {
            await _client.PublishAsync(mqttMsg, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Surface the disconnect to listeners so the UI light goes red immediately —
            // MQTT broker may have closed the connection between heartbeats and the client
            // hasn't fired its own DisconnectedAsync yet.
            Disconnected?.Invoke(this, ex.Message);
            throw;
        }
        _logger.LogDebug("MQTT published to {Topic}", _config.MqttTopic);
    }

    public async Task DisconnectAsync()
    {
        if (_client != null && _client.IsConnected)
        {
            try
            {
                await _client.DisconnectAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MQTT disconnect error");
            }
        }
        _client?.Dispose();
        _client = null;
        Disconnected?.Invoke(this, "Disconnected");
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        GC.SuppressFinalize(this);
    }
}
