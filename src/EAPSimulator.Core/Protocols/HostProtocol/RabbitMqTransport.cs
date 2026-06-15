using System.Text;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace EAPSimulator.Core.Protocols.HostProtocol;

/// <summary>
/// RabbitMQ transport for Host/MES communication.
/// Uses AMQP protocol for reliable message delivery.
/// </summary>
public class RabbitMqTransport : IHostTransport
{
    private readonly ILogger _logger;
    private HostTransportConfig _config = new();
    private IConnection? _connection;
    private IChannel? _channel;
    private string? _consumerTag;

    public TransportType TransportType => TransportType.RabbitMq;
    public bool IsConnected => _connection?.IsOpen == true;
    public string? Endpoint => $"{_config.RabbitMqHost}:{_config.RabbitMqPort}";

    public event EventHandler<string>? MessageReceived;
    public event EventHandler<string?>? Disconnected;

    public RabbitMqTransport(ILogger logger)
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
            var factory = new ConnectionFactory
            {
                HostName = _config.RabbitMqHost,
                Port = _config.RabbitMqPort,
                UserName = _config.Username ?? "guest",
                Password = _config.Password ?? "guest",
            };

            _connection = await factory.CreateConnectionAsync(ct);
            _channel = await _connection.CreateChannelAsync(cancellationToken: ct);

            // Declare queue
            await _channel.QueueDeclareAsync(
                queue: _config.RabbitMqQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: ct);

            // Start consuming
            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += async (_, ea) =>
            {
                var body = Encoding.UTF8.GetString(ea.Body.ToArray());
                MessageReceived?.Invoke(this, body);
                await Task.CompletedTask;
            };

            _consumerTag = await _channel.BasicConsumeAsync(
                queue: _config.RabbitMqQueue,
                autoAck: true,
                consumer: consumer,
                cancellationToken: ct);

            _logger.LogInformation("RabbitMQ connected to {Host}:{Port}, queue: {Queue}",
                _config.RabbitMqHost, _config.RabbitMqPort, _config.RabbitMqQueue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RabbitMQ connect failed");
            throw;
        }
    }

    public async Task SendAsync(string message, CancellationToken ct)
    {
        if (_channel == null)
            throw new InvalidOperationException("Not connected");

        var body = Encoding.UTF8.GetBytes(message);
        var props = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
        };

        await _channel.BasicPublishAsync(
            exchange: _config.RabbitMqExchange,
            routingKey: _config.RabbitMqRoutingKey,
            mandatory: false,
            basicProperties: props,
            body: body,
            cancellationToken: ct);

        _logger.LogDebug("RabbitMQ published to {Exchange}/{Key}", _config.RabbitMqExchange, _config.RabbitMqRoutingKey);
    }

    public async Task DisconnectAsync()
    {
        try
        {
            if (_consumerTag != null && _channel != null)
                await _channel.BasicCancelAsync(_consumerTag);
            if (_channel != null)
                await _channel.CloseAsync();
            if (_connection != null)
                await _connection.CloseAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RabbitMQ disconnect error");
        }
        finally
        {
            _channel?.Dispose();
            _connection?.Dispose();
            _channel = null;
            _connection = null;
            _consumerTag = null;
            Disconnected?.Invoke(this, "Disconnected");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        GC.SuppressFinalize(this);
    }
}
