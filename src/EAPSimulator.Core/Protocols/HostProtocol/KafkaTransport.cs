using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace EAPSimulator.Core.Protocols.HostProtocol;

/// <summary>
/// Apache Kafka transport for Host/MES communication.
/// Produces messages to a topic and consumes from another topic.
/// </summary>
public class KafkaTransport : IHostTransport
{
    private readonly ILogger _logger;
    private HostTransportConfig _config = new();
    private IProducer<string, string>? _producer;
    private Task? _consumeTask;
    private CancellationTokenSource? _consumeCts;

    public TransportType TransportType => TransportType.Kafka;
    public bool IsConnected => _producer != null;
    public string? Endpoint => _config.KafkaBootstrapServers;

    public event EventHandler<string>? MessageReceived;
    public event EventHandler<string?>? Disconnected;

    public KafkaTransport(ILogger logger)
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
            var producerConfig = new ProducerConfig
            {
                BootstrapServers = _config.KafkaBootstrapServers,
                Acks = Acks.All,
            };
            _producer = new ProducerBuilder<string, string>(producerConfig).Build();

            // Start consumer
            _consumeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _consumeTask = Task.Run(() => ConsumeLoop(_consumeCts.Token));

            _logger.LogInformation("Kafka connected to {Servers}, topic: {Topic}",
                _config.KafkaBootstrapServers, _config.KafkaTopic);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kafka connect failed");
            throw;
        }
    }

    private async Task ConsumeLoop(CancellationToken ct)
    {
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _config.KafkaBootstrapServers,
            GroupId = _config.KafkaGroupId ?? "eap-group",
            AutoOffsetReset = AutoOffsetReset.Latest,
            EnableAutoCommit = true,
        };

        using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        consumer.Subscribe(_config.KafkaTopic);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = consumer.Consume(ct);
                if (result?.Message?.Value != null)
                    MessageReceived?.Invoke(this, result.Message.Value);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kafka consume error");
        }
    }

    public async Task SendAsync(string message, CancellationToken ct)
    {
        if (_producer == null)
            throw new InvalidOperationException("Not connected");

        var msg = new Message<string, string> { Value = message };
        await _producer.ProduceAsync(_config.KafkaTopic, msg, ct);
        _logger.LogDebug("Kafka produced to {Topic}", _config.KafkaTopic);
    }

    public async Task DisconnectAsync()
    {
        _consumeCts?.Cancel();
        if (_consumeTask != null)
        {
            try { await _consumeTask; } catch { }
        }
        _producer?.Dispose();
        _producer = null;
        Disconnected?.Invoke(this, "Disconnected");
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _consumeCts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
