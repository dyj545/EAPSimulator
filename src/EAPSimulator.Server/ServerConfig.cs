using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EAPSimulator.Server;

/// <summary>
/// Server configuration loaded from server_config.json.
/// Supports inheritance via "baseConfig" field — the base is loaded first,
/// then all fields from the current file are deep-merged on top.
/// </summary>
public class ServerConfig
{
    /// <summary>
    /// Optional path to a base config file. Fields not present in this file
    /// will be inherited from the base.
    /// </summary>
    [JsonProperty("baseConfig")]
    public string? BaseConfig { get; set; }

    [JsonProperty("secsGem")]
    public SecsGemConfig SecsGem { get; set; } = new();

    [JsonProperty("host")]
    public HostConfig Host { get; set; } = new();

    [JsonProperty("files")]
    public FilePathsConfig Files { get; set; } = new();

    [JsonProperty("logging")]
    public LoggingConfig Logging { get; set; } = new();

    /// <summary>
    /// Load config with optional inheritance. If the file contains "baseConfig",
    /// the base file is loaded first and all fields are deep-merged.
    /// </summary>
    public static ServerConfig LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);
        var jObj = JObject.Parse(json);

        // Recursively resolve base config chain
        if (jObj.TryGetValue("baseConfig", out var baseToken) && baseToken.Type == JTokenType.String)
        {
            var basePath = baseToken.ToString();
            if (!Path.IsPathRooted(basePath))
                basePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, basePath);

            if (File.Exists(basePath))
            {
                var baseConfig = LoadFromFile(basePath);
                var baseJObj = JObject.FromObject(baseConfig);

                // Deep merge: override fields take precedence over base
                baseJObj.Merge(jObj, new JsonMergeSettings
                {
                    MergeArrayHandling = MergeArrayHandling.Replace,
                    MergeNullValueHandling = MergeNullValueHandling.Merge
                });

                // Remove baseConfig from merged result to avoid re-inheritance
                baseJObj.Remove("baseConfig");

                return baseJObj.ToObject<ServerConfig>()
                    ?? throw new InvalidOperationException($"Failed to deserialize merged config from {path}");
            }
            else
            {
                Console.WriteLine($"Warning: baseConfig file not found: {basePath}, loading current file only");
            }
        }

        return JsonConvert.DeserializeObject<ServerConfig>(json)
            ?? throw new InvalidOperationException($"Failed to load server config from {path}");
    }
}

public class SecsGemConfig
{
    [JsonProperty("connectionMode")]
    public string ConnectionMode { get; set; } = "Passive";

    [JsonProperty("remoteHost")]
    public string RemoteHost { get; set; } = "127.0.0.1";

    [JsonProperty("remotePort")]
    public int RemotePort { get; set; } = 5000;

    [JsonProperty("localHost")]
    public string LocalHost { get; set; } = "0.0.0.0";

    [JsonProperty("localPort")]
    public int LocalPort { get; set; } = 5000;

    [JsonProperty("deviceId")]
    public int DeviceId { get; set; } = 1;

    [JsonProperty("acceptCommunication")]
    public bool AcceptCommunication { get; set; } = true;

    // Timeouts in milliseconds
    [JsonProperty("t3Timeout")]
    public int T3Timeout { get; set; } = 45000;

    [JsonProperty("t5Timeout")]
    public int T5Timeout { get; set; } = 10000;

    [JsonProperty("t6Timeout")]
    public int T6Timeout { get; set; } = 5000;

    [JsonProperty("t7Timeout")]
    public int T7Timeout { get; set; } = 10000;

    [JsonProperty("t8Timeout")]
    public int T8Timeout { get; set; } = 5000;
}

public class HostConfig
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; }

    [JsonProperty("transportType")]
    public string TransportType { get; set; } = "Tcp";

    [JsonProperty("isActiveMode")]
    public bool IsActiveMode { get; set; } = true;

    // TCP
    [JsonProperty("remoteHost")]
    public string RemoteHost { get; set; } = "127.0.0.1";

    [JsonProperty("remotePort")]
    public int RemotePort { get; set; } = 5000;

    [JsonProperty("localHost")]
    public string LocalHost { get; set; } = "0.0.0.0";

    [JsonProperty("localPort")]
    public int LocalPort { get; set; } = 5000;

    // HTTP POST
    [JsonProperty("httpUrl")]
    public string HttpUrl { get; set; } = "";

    [JsonProperty("httpHeaders")]
    public Dictionary<string, string> HttpHeaders { get; set; } = new();

    // ActiveMQ
    [JsonProperty("brokerUri")]
    public string BrokerUri { get; set; } = "tcp://localhost:61616";

    [JsonProperty("queueName")]
    public string QueueName { get; set; } = "eap.mes.queue";

    [JsonProperty("responseQueueName")]
    public string ResponseQueueName { get; set; } = "eap.response.queue";

    [JsonProperty("username")]
    public string? Username { get; set; }

    [JsonProperty("password")]
    public string? Password { get; set; }

    // RabbitMQ
    [JsonProperty("rabbitMqHost")]
    public string RabbitMqHost { get; set; } = "localhost";

    [JsonProperty("rabbitMqPort")]
    public int RabbitMqPort { get; set; } = 5672;

    [JsonProperty("rabbitMqExchange")]
    public string RabbitMqExchange { get; set; } = "eap.exchange";

    [JsonProperty("rabbitMqRoutingKey")]
    public string RabbitMqRoutingKey { get; set; } = "eap.mes";

    [JsonProperty("rabbitMqQueue")]
    public string RabbitMqQueue { get; set; } = "eap.mes.queue";

    // Kafka
    [JsonProperty("kafkaBootstrapServers")]
    public string KafkaBootstrapServers { get; set; } = "localhost:9092";

    [JsonProperty("kafkaTopic")]
    public string KafkaTopic { get; set; } = "eap-mes-topic";

    [JsonProperty("kafkaGroupId")]
    public string? KafkaGroupId { get; set; } = "eap-group";

    // gRPC
    [JsonProperty("grpcEndpoint")]
    public string GrpcEndpoint { get; set; } = "https://localhost:5001";

    // MQTT
    [JsonProperty("mqttBroker")]
    public string MqttBroker { get; set; } = "localhost";

    [JsonProperty("mqttPort")]
    public int MqttPort { get; set; } = 1883;

    [JsonProperty("mqttTopic")]
    public string MqttTopic { get; set; } = "eap/mes/messages";

    [JsonProperty("mqttClientId")]
    public string? MqttClientId { get; set; }
}

public class FilePathsConfig
{
    [JsonProperty("secsMessageTemplates")]
    public string SecsMessageTemplates { get; set; } = "secs_message_templates.json";

    [JsonProperty("hostMessageTemplates")]
    public string HostMessageTemplates { get; set; } = "host_message_templates.json";

    [JsonProperty("autoReplyRules")]
    public string AutoReplyRules { get; set; } = "auto_reply_rules.json";
}

public class LoggingConfig
{
    [JsonProperty("console")]
    public bool Console { get; set; } = true;

    [JsonProperty("file")]
    public bool File { get; set; } = true;

    [JsonProperty("filePath")]
    public string FilePath { get; set; } = "logs/eap-server-.log";

    [JsonProperty("rollingInterval")]
    public string RollingInterval { get; set; } = "Day";

    [JsonProperty("minimumLevel")]
    public string MinimumLevel { get; set; } = "Information";
}
