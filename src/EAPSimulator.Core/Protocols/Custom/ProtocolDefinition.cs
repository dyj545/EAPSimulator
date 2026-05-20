using Newtonsoft.Json;

namespace EAPSimulator.Core.Protocols.Custom;

/// <summary>
/// Defines the framing strategy for custom protocol messages.
/// </summary>
public enum FramingType
{
    LengthPrefix,   // 4-byte length prefix (big-endian)
    Delimiter,      // Fixed delimiter (e.g., "\r\n")
}

/// <summary>
/// Defines a custom protocol via JSON configuration.
/// </summary>
public class ProtocolDefinition
{
    [JsonProperty("name")]
    public string Name { get; set; } = "CustomProtocol";

    [JsonProperty("framing")]
    public FramingType Framing { get; set; } = FramingType.Delimiter;

    [JsonProperty("delimiter")]
    public string Delimiter { get; set; } = "\r\n";

    [JsonProperty("encoding")]
    public string Encoding { get; set; } = "UTF-8";

    [JsonProperty("messages")]
    public List<MessageDefinition> Messages { get; set; } = new();

    public MessageDefinition? GetMessageById(string id) =>
        Messages.FirstOrDefault(m => m.Id == id, null);

    public MessageDefinition? GetMessageByName(string name) =>
        Messages.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase), null);

    public static ProtocolDefinition LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);
        return JsonConvert.DeserializeObject<ProtocolDefinition>(json)
            ?? throw new InvalidOperationException($"Failed to load protocol definition from {path}");
    }

    public void SaveToFile(string path)
    {
        var json = JsonConvert.SerializeObject(this, Formatting.Indented);
        File.WriteAllText(path, json);
    }
}

/// <summary>
/// Defines a single message type in the custom protocol.
/// </summary>
public class MessageDefinition
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("direction")]
    public MessageDirectionType Direction { get; set; } = MessageDirectionType.Both;

    [JsonProperty("fields")]
    public List<FieldDefinition> Fields { get; set; } = new();

    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;
}

public enum MessageDirectionType
{
    Send,
    Receive,
    Both,
}

/// <summary>
/// Defines a single field within a message.
/// </summary>
public class FieldDefinition
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("type")]
    public FieldType Type { get; set; } = FieldType.String;

    [JsonProperty("length")]
    public int Length { get; set; }

    [JsonProperty("defaultValue")]
    public string DefaultValue { get; set; } = string.Empty;

    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;
}

public enum FieldType
{
    String,
    Integer,
    Float,
    Boolean,
    Hex,
    Fixed,  // Fixed-length string field
}
