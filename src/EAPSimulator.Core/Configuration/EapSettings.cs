namespace EAPSimulator.Core.Configuration;

public class EapSettings
{
    public HsmsSettings Hsms { get; set; } = new();
    public CustomProtocolSettings Custom { get; set; } = new();
    public string LogPath { get; set; } = "logs/eap.log";
}

public enum ConnectionMode
{
    Passive,
    Active,
    Alternating
}

public class HsmsSettings
{
    public string LocalHost { get; set; } = "0.0.0.0";
    public int LocalPort { get; set; } = 5000;
    public string RemoteHost { get; set; } = "127.0.0.1";
    public int RemotePort { get; set; } = 5000;
    public ushort DeviceId { get; set; } = 1;
    public ConnectionMode ConnectionMode { get; set; } = ConnectionMode.Passive;

    public int T3Timeout { get; set; } = 45000;
    public int T5Timeout { get; set; } = 10000;
    public int T6Timeout { get; set; } = 5000;
    public int T7Timeout { get; set; } = 10000;
    public int T8Timeout { get; set; } = 5000;

    [System.Obsolete("Use ConnectionMode instead")]
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsActive
    {
        get => ConnectionMode == ConnectionMode.Active;
        set => ConnectionMode = value ? ConnectionMode.Active : ConnectionMode.Passive;
    }

    [System.Obsolete("Use LocalHost/RemoteHost instead")]
    [System.Text.Json.Serialization.JsonIgnore]
    public string Host
    {
        get => ConnectionMode == ConnectionMode.Active ? RemoteHost : LocalHost;
        set
        {
            if (ConnectionMode == ConnectionMode.Active)
                RemoteHost = value;
            else
                LocalHost = value;
        }
    }

    [System.Obsolete("Use LocalPort/RemotePort instead")]
    [System.Text.Json.Serialization.JsonIgnore]
    public int Port
    {
        get => ConnectionMode == ConnectionMode.Active ? RemotePort : LocalPort;
        set
        {
            if (ConnectionMode == ConnectionMode.Active)
                RemotePort = value;
            else
                LocalPort = value;
        }
    }
}

public class CustomProtocolSettings
{
    public string ConfigPath { get; set; } = "custom_protocol.json";
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 6000;
}
