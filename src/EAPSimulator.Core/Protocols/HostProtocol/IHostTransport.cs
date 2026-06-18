namespace EAPSimulator.Core.Protocols.HostProtocol;

/// <summary>
/// High-level transport interface for Host/MES communication.
/// Works at the message level (string), unlike ITransport which is byte-level.
/// Each implementation handles its own serialization and connection management.
/// </summary>
public interface IHostTransport : IAsyncDisposable
{
    /// <summary>Transport type identifier.</summary>
    TransportType TransportType { get; }

    /// <summary>Whether the transport is currently connected/ready.</summary>
    bool IsConnected { get; }

    /// <summary>Human-readable endpoint description.</summary>
    string? Endpoint { get; }

    /// <summary>Configure the transport with connection parameters.</summary>
    void Configure(HostTransportConfig config);

    /// <summary>Establish connection.</summary>
    Task ConnectAsync(CancellationToken ct);

    /// <summary>Disconnect.</summary>
    Task DisconnectAsync();

    /// <summary>Send a message.</summary>
    Task SendAsync(string message, CancellationToken ct);

    /// <summary>Raised when a message is received.</summary>
    event EventHandler<string> MessageReceived;

    /// <summary>Raised when disconnected.</summary>
    event EventHandler<string?> Disconnected;
}

/// <summary>
/// Transport type for Host/MES communication.
/// </summary>
public enum TransportType
{
    /// <summary>TCP socket (JSON over TCP).</summary>
    Tcp,
    /// <summary>HTTP POST request/response.</summary>
    HttpPost,
    /// <summary>Apache ActiveMQ (NMS).</summary>
    ActiveMq,
    /// <summary>RabbitMQ (AMQP).</summary>
    RabbitMq,
    /// <summary>Apache Kafka.</summary>
    Kafka,
    /// <summary>gRPC (Protocol Buffers).</summary>
    Grpc,
    /// <summary>MQTT.</summary>
    Mqtt,
    /// <summary>OPC UA.</summary>
    OpcUa,
}

/// <summary>
/// Configuration for Host transport connection.
/// </summary>
public class HostTransportConfig
{
    /// <summary>Transport type to use.</summary>
    public TransportType TransportType { get; set; } = TransportType.Tcp;

    // --- TCP ---
    /// <summary>Active mode (client) or Passive mode (server).</summary>
    public bool IsActiveMode { get; set; } = true;
    public string RemoteHost { get; set; } = "127.0.0.1";
    public int RemotePort { get; set; } = 5000;
    public string LocalHost { get; set; } = "0.0.0.0";
    public int LocalPort { get; set; } = 5000;

    // --- HTTP POST ---
    public string HttpUrl { get; set; } = "";
    public Dictionary<string, string> HttpHeaders { get; set; } = new();

    /// <summary>
    /// (Passive HTTP only) Required Authorization header value for incoming POST requests.
    /// Empty = no check, accept all. Mismatch → respond 401 Unauthorized and skip
    /// dispatching the body to <see cref="IHostTransport.MessageReceived"/>.
    /// Compared verbatim (case-sensitive); the caller's stored value should already include
    /// the scheme prefix, e.g. <c>Bearer xxxxxx</c>.
    /// </summary>
    public string ExpectedAuthorization { get; set; } = "";

    // --- ActiveMQ ---
    public string BrokerUri { get; set; } = "tcp://localhost:61616";
    public string QueueName { get; set; } = "eap.mes.queue";
    public string ResponseQueueName { get; set; } = "eap.response.queue";
    public string? Username { get; set; }
    public string? Password { get; set; }

    // --- RabbitMQ ---
    public string RabbitMqHost { get; set; } = "localhost";
    public int RabbitMqPort { get; set; } = 5672;
    public string RabbitMqExchange { get; set; } = "eap.exchange";
    public string RabbitMqRoutingKey { get; set; } = "eap.mes";
    public string RabbitMqQueue { get; set; } = "eap.mes.queue";

    // --- Kafka ---
    public string KafkaBootstrapServers { get; set; } = "localhost:9092";
    public string KafkaTopic { get; set; } = "eap-mes-topic";
    public string? KafkaGroupId { get; set; } = "eap-group";

    // --- gRPC ---
    public string GrpcEndpoint { get; set; } = "https://localhost:5001";

    // --- MQTT ---
    public string MqttBroker { get; set; } = "localhost";
    public int MqttPort { get; set; } = 1883;
    public string MqttTopic { get; set; } = "eap/mes/messages";
    public string? MqttClientId { get; set; }

    // --- OPC UA ---
    public string OpcUaEndpoint { get; set; } = "opc.tcp://localhost:4840";
}
