using System.Text;
using System.Text.Json;

namespace EAPSimulator.Core.Protocols.HostProtocol;

/// <summary>
/// Host message for MES communication.
/// Similar to SECS message but used for host/MES side.
/// </summary>
public class HostMessage
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public HostMessageDirection Direction { get; set; } = HostMessageDirection.Send;
    public Dictionary<string, HostField> Fields { get; set; } = new();

    /// <summary>
    /// Create a reply message from this message (flip direction).
    /// </summary>
    public HostMessage CreateReply()
    {
        var reply = new HostMessage
        {
            Name = $"{Name}_Reply",
            Direction = Direction == HostMessageDirection.Send
                ? HostMessageDirection.Receive
                : HostMessageDirection.Send,
        };
        return reply;
    }

    /// <summary>
    /// Get field value as string.
    /// </summary>
    public string? GetFieldValue(string fieldName)
    {
        return Fields.TryGetValue(fieldName, out var field) ? field.Value : null;
    }

    /// <summary>
    /// Set field value.
    /// </summary>
    public void SetFieldValue(string fieldName, string value)
    {
        if (Fields.TryGetValue(fieldName, out var field))
        {
            field.Value = value;
        }
    }

    /// <summary>
    /// Serialize to JSON.
    /// </summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Deserialize from JSON.
    /// </summary>
    public static HostMessage? FromJson(string json)
    {
        return JsonSerializer.Deserialize<HostMessage>(json);
    }

    /// <summary>
    /// Convert to protocol message for generic handling.
    /// </summary>
    public ProtocolMessage ToProtocolMessage()
    {
        var msg = new ProtocolMessage
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Name = Name,
            Description = Description,
        };
        foreach (var (key, field) in Fields)
            msg.SetField(key, field.Value);
        return msg;
    }
}

/// <summary>
/// A field in a host message.
/// </summary>
public class HostField
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public HostFieldType Type { get; set; } = HostFieldType.ASCII;
    public string Description { get; set; } = string.Empty;
    public bool Required { get; set; }
}

/// <summary>
/// Direction of host message.
/// </summary>
public enum HostMessageDirection
{
    /// <summary>Message sent to MES.</summary>
    Send,
    /// <summary>Message received from MES.</summary>
    Receive
}

/// <summary>
/// Field types for host messages.
/// </summary>
public enum HostFieldType
{
    ASCII,
    Binary,
    Boolean,
    U1, U2, U4, U8,
    I1, I2, I4, I8,
    F4, F8
}

/// <summary>
/// Host message template definition.
/// </summary>
public class HostMessageTemplate
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public HostMessageDirection Direction { get; set; } = HostMessageDirection.Send;
    public List<HostFieldTemplate> Fields { get; set; } = [];

    /// <summary>
    /// Build a HostMessage from this template.
    /// </summary>
    public HostMessage BuildMessage()
    {
        var msg = new HostMessage
        {
            Name = Name,
            Description = Description,
            Direction = Direction,
        };
        foreach (var fieldDef in Fields)
        {
            msg.Fields[fieldDef.Name] = new HostField
            {
                Name = fieldDef.Name,
                Value = fieldDef.DefaultValue,
                Type = fieldDef.Type,
                Description = fieldDef.Description,
                Required = fieldDef.Required,
            };
        }
        return msg;
    }
}

/// <summary>
/// Field definition in a host message template.
/// </summary>
public class HostFieldTemplate
{
    public string Name { get; set; } = string.Empty;
    public HostFieldType Type { get; set; } = HostFieldType.ASCII;
    public string DefaultValue { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Required { get; set; }
}

/// <summary>
/// Collection of host message templates loaded from JSON.
/// </summary>
public class HostMessageTemplateCollection
{
    public List<HostMessageTemplate> Templates { get; set; } = [];

    /// <summary>
    /// Load templates from JSON file.
    /// </summary>
    public static HostMessageTemplateCollection LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<HostMessageTemplateCollection>(json)
            ?? new HostMessageTemplateCollection();
    }

    /// <summary>
    /// Save templates to JSON file.
    /// </summary>
    public void SaveToFile(string path)
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Find template by name.
    /// </summary>
    public HostMessageTemplate? FindByName(string name)
    {
        return Templates.FirstOrDefault(t => t.Name == name);
    }
}
