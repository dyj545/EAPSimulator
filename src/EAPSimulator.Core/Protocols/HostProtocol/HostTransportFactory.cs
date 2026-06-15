using Microsoft.Extensions.Logging;

namespace EAPSimulator.Core.Protocols.HostProtocol;

/// <summary>
/// Factory for creating IHostTransport instances based on TransportType.
/// </summary>
public static class HostTransportFactory
{
    /// <summary>
    /// Create a transport instance for the given type.
    /// </summary>
    public static IHostTransport Create(TransportType type, ILogger logger)
    {
        return type switch
        {
            TransportType.Tcp => new HostTcpTransport(logger),
            TransportType.HttpPost => new HttpPostTransport(logger),
            TransportType.ActiveMq => new ActiveMqTransport(logger),
            TransportType.RabbitMq => new RabbitMqTransport(logger),
            TransportType.Kafka => new KafkaTransport(logger),
            TransportType.Grpc => new GrpcTransport(logger),
            TransportType.Mqtt => new MqttTransport(logger),
            TransportType.OpcUa => new OpcUaTransport(logger),
            _ => throw new NotSupportedException($"Transport type '{type}' is not supported"),
        };
    }

    /// <summary>
    /// Get all supported transport type names.
    /// </summary>
    public static string[] GetSupportedTypes()
    {
        return Enum.GetNames<TransportType>();
    }
}
