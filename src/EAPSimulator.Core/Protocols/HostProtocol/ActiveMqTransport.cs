using Apache.NMS;
using Apache.NMS.ActiveMQ;
using Microsoft.Extensions.Logging;

namespace EAPSimulator.Core.Protocols.HostProtocol;

/// <summary>
/// Apache ActiveMQ transport for Host/MES communication.
/// Uses NMS API with ActiveMQ provider.
/// </summary>
public class ActiveMqTransport : IHostTransport
{
    private readonly ILogger _logger;
    private HostTransportConfig _config = new();
    private IConnection? _connection;
    private ISession? _session;
    private IMessageProducer? _producer;
    private IMessageConsumer? _consumer;
    private IDestination? _responseDestination;

    public TransportType TransportType => TransportType.ActiveMq;
    public bool IsConnected => _connection != null;
    public string? Endpoint => _config.BrokerUri;

    public event EventHandler<string>? MessageReceived;
    public event EventHandler<string?>? Disconnected;

    public ActiveMqTransport(ILogger logger)
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
            var factory = new ConnectionFactory(_config.BrokerUri);
            _connection = !string.IsNullOrEmpty(_config.Username)
                ? factory.CreateConnection(_config.Username, _config.Password)
                : factory.CreateConnection();
            _connection.Start();
            _session = _connection.CreateSession(AcknowledgementMode.AutoAcknowledge);

            var queue = _session.GetQueue(_config.QueueName);
            _producer = _session.CreateProducer(queue);

            // Subscribe to response queue
            if (!string.IsNullOrEmpty(_config.ResponseQueueName))
            {
                _responseDestination = _session.GetQueue(_config.ResponseQueueName);
                _consumer = _session.CreateConsumer(_responseDestination);
                _consumer.Listener += OnMessageReceived;
            }

            _logger.LogInformation("ActiveMQ connected to {Broker}, queue: {Queue}", _config.BrokerUri, _config.QueueName);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ActiveMQ connect failed");
            throw;
        }
    }

    private void OnMessageReceived(IMessage message)
    {
        try
        {
            if (message is ITextMessage textMsg)
                MessageReceived?.Invoke(this, textMsg.Text);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ActiveMQ message receive error");
        }
    }

    public async Task SendAsync(string message, CancellationToken ct)
    {
        if (_producer == null)
            throw new InvalidOperationException("Not connected");

        try
        {
            var textMsg = _session!.CreateTextMessage(message);
            _producer.Send(textMsg);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Disconnected?.Invoke(this, ex.Message);
            throw;
        }
        _logger.LogDebug("ActiveMQ sent message to {Queue}", _config.QueueName);
        await Task.CompletedTask;
    }

    public async Task DisconnectAsync()
    {
        _consumer?.Close();
        _consumer?.Dispose();
        _producer?.Close();
        _producer?.Dispose();
        _session?.Close();
        _session?.Dispose();
        _connection?.Stop();
        _connection?.Close();
        _connection?.Dispose();
        _consumer = null;
        _producer = null;
        _session = null;
        _connection = null;
        Disconnected?.Invoke(this, "Disconnected");
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        GC.SuppressFinalize(this);
    }
}
