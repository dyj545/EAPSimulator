using System.Text.Json.Serialization;

namespace EAPSimulator.Core.Protocols.HostProtocol;

/// <summary>
/// Configuration for one named Host channel (MES, RMS, WMS, etc.).
/// Each channel gets its own transport instance and can be independently
/// started/stopped.
/// </summary>
public class HostChannelConfig
{
    public string Name { get; set; } = "MES";

    /// <summary>HttpPost, Tcp, Mqtt, Kafka, RabbitMq, ActiveMq, Grpc, OpcUa.</summary>
    public string TransportType { get; set; } = "HttpPost";

    public bool IsActiveMode { get; set; } = true;

    /// <summary>Default body format: Json = serialize fields, Raw = free text.</summary>
    public string BodyFormat { get; set; } = "Json";

    /// <summary>Filesystem path to the channel's message template file.
    /// Empty = use the global host_message_templates.json.</summary>
    public string TemplatePath { get; set; } = "";

    // ─── HTTP ───
    public string HttpUrl { get; set; } = "";
    public string ContentType { get; set; } = "application/json";
    public Dictionary<string, string> HttpHeaders { get; set; } = new();

    // ─── TCP ───
    public string RemoteHost { get; set; } = "127.0.0.1";
    public int RemotePort { get; set; } = 8080;
    public string LocalHost { get; set; } = "0.0.0.0";
    public int LocalPort { get; set; } = 0;

    // ─── MQTT ───
    public string MqttBroker { get; set; } = "localhost";
    public int MqttPort { get; set; } = 1883;
    public string MqttTopic { get; set; } = "eap/mes/messages";
    public string MqttClientId { get; set; } = "";

    // ─── Kafka ───
    public string KafkaBootstrapServers { get; set; } = "localhost:9092";
    public string KafkaTopic { get; set; } = "eap-mes-topic";
    public string KafkaGroupId { get; set; } = "eap-group";

    // ─── RabbitMQ ───
    public string RabbitMqHost { get; set; } = "localhost";
    public int RabbitMqPort { get; set; } = 5672;
    public string RabbitMqExchange { get; set; } = "eap.exchange";
    public string RabbitMqRoutingKey { get; set; } = "eap.mes";
    public string RabbitMqQueue { get; set; } = "eap.mes.queue";

    // ─── ActiveMQ ───
    public string ActiveMqBrokerUri { get; set; } = "tcp://localhost:61616";
    public string ActiveMqQueue { get; set; } = "eap.mes.queue";
    public string ActiveMqResponseQueue { get; set; } = "eap.response.queue";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    // ─── gRPC ───
    public string GrpcEndpoint { get; set; } = "https://localhost:5001";

    // ─── OPC UA ───
    public string OpcUaEndpoint { get; set; } = "opc.tcp://localhost:4840";

    public HostTransportConfig ToTransportConfig()
    {
        if (!Enum.TryParse<TransportType>(TransportType, true, out var tt))
            tt = EAPSimulator.Core.Protocols.HostProtocol.TransportType.HttpPost;

        return new HostTransportConfig
        {
            TransportType = tt,
            IsActiveMode = IsActiveMode,
            // HTTP
            HttpUrl = HttpUrl,
            HttpHeaders = HttpHeaders,
            // TCP
            RemoteHost = RemoteHost,
            RemotePort = RemotePort,
            LocalHost = LocalHost,
            LocalPort = LocalPort,
            // MQTT
            MqttBroker = MqttBroker,
            MqttPort = MqttPort,
            MqttTopic = MqttTopic,
            MqttClientId = string.IsNullOrEmpty(MqttClientId) ? null : MqttClientId,
            // Kafka
            KafkaBootstrapServers = KafkaBootstrapServers,
            KafkaTopic = KafkaTopic,
            KafkaGroupId = string.IsNullOrEmpty(KafkaGroupId) ? null : KafkaGroupId,
            // RabbitMQ
            RabbitMqHost = RabbitMqHost,
            RabbitMqPort = RabbitMqPort,
            RabbitMqExchange = RabbitMqExchange,
            RabbitMqRoutingKey = RabbitMqRoutingKey,
            RabbitMqQueue = RabbitMqQueue,
            // ActiveMQ
            BrokerUri = ActiveMqBrokerUri,
            QueueName = ActiveMqQueue,
            ResponseQueueName = ActiveMqResponseQueue,
            Username = string.IsNullOrEmpty(Username) ? null : Username,
            Password = string.IsNullOrEmpty(Password) ? null : Password,
            // gRPC
            GrpcEndpoint = GrpcEndpoint,
            // OPC UA
            OpcUaEndpoint = OpcUaEndpoint,
        };
    }
}

/// <summary>
/// Persistence wrapper for the channel list.
/// </summary>
public class HostChannelCollection
{
    public List<HostChannelConfig> Channels { get; set; } = [];

    public static HostChannelCollection LoadFromFile(string path)
    {
        if (!File.Exists(path)) return new HostChannelCollection();
        var json = File.ReadAllText(path);
        return System.Text.Json.JsonSerializer.Deserialize<HostChannelCollection>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new HostChannelCollection();
    }

    public void SaveToFile(string path)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(this,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        File.WriteAllText(path, json);
    }
}
