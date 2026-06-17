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

    /// <summary>Channel this message belongs to (set by template; empty = default).</summary>
    public string ChannelName { get; set; } = "";

    /// <summary>"Json" or "Raw". Raw bypasses field-based JSON serialization.</summary>
    public string BodyFormat { get; set; } = "Json";

    /// <summary>Raw body template; supports {fieldName} substitution from Fields when BodyFormat=Raw.</summary>
    public string RawBody { get; set; } = "";

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
    /// Render the wire body for this message. For BodyFormat="Raw" returns RawBody with
    /// {fieldName} placeholders substituted from Fields. For "Json" returns a flat JSON
    /// object with field names as keys (NOT the full <see cref="ToJson"/> envelope), which
    /// matches the MES interface examples like {"trxId":"FBPBISTOL","actionFlg":"UC",...}.
    /// </summary>
    public string ToWireBody()
    {
        if (string.Equals(BodyFormat, "Raw", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(RawBody))
        {
            var body = RawBody;
            foreach (var (k, v) in Fields)
                body = body.Replace("{" + k + "}", v.Value ?? "");
            return body;
        }

        // Default: build a flat JSON object from Fields (recursive for Object/ArrayList).
        var dict = FieldsToObject(Fields);
        return JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = false });
    }

    private static object FieldsToObject(Dictionary<string, HostField> fields)
    {
        var o = new Dictionary<string, object?>();
        foreach (var (k, f) in fields)
            o[k] = ConvertField(f);
        return o;
    }

    private static object? ConvertField(HostField f)
    {
        switch (f.Type)
        {
            case HostFieldType.ArrayList:
                // Each child IS an array element. If child is itself an Object, expand its sub-fields.
                var list = new List<object?>();
                foreach (var child in f.Children)
                {
                    if (child.Type == HostFieldType.Object && child.Children.Count > 0)
                    {
                        var inner = new Dictionary<string, object?>();
                        foreach (var grand in child.Children) inner[grand.Name] = ConvertField(grand);
                        list.Add(inner);
                    }
                    else
                    {
                        list.Add(ConvertField(child));
                    }
                }
                return list;
            case HostFieldType.Object:
                var obj = new Dictionary<string, object?>();
                foreach (var child in f.Children) obj[child.Name] = ConvertField(child);
                return obj;
            case HostFieldType.Boolean:
                return f.Value == "1" || string.Equals(f.Value, "true", StringComparison.OrdinalIgnoreCase);
            case HostFieldType.Integer:
            case HostFieldType.U1: case HostFieldType.U2: case HostFieldType.U4: case HostFieldType.U8:
            case HostFieldType.I1: case HostFieldType.I2: case HostFieldType.I4: case HostFieldType.I8:
                return long.TryParse(f.Value, out var l) ? l : f.Value;
            case HostFieldType.F4: case HostFieldType.F8:
                return double.TryParse(f.Value, out var d) ? d : f.Value;
            default:
                return f.Value ?? "";
        }
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
/// A field in a host message. Leaf fields use <see cref="Value"/>;
/// nested fields (Object / ArrayList) use <see cref="Children"/> instead — for ArrayList
/// each child is one element; for Object each child is a named property.
/// </summary>
public class HostField
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public HostFieldType Type { get; set; } = HostFieldType.ASCII;
    public string Description { get; set; } = string.Empty;
    public bool Required { get; set; }

    /// <summary>Nested fields. Used when <see cref="Type"/> is Object or ArrayList.</summary>
    public List<HostField> Children { get; set; } = [];
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
    F4, F8,

    /// <summary>Nested object — Children is a list of named properties.</summary>
    Object,
    /// <summary>Ordered list — Children is a list of elements (often Objects themselves).</summary>
    ArrayList,

    /// <summary>Generic string (alias for ASCII; matches xlsx wording).</summary>
    String,
    Integer,
    Timestamp,

    /// <summary>Date/time value.</summary>
    DateTime,
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
    /// Channel name this template binds to (e.g. "MES", "RMS"). Empty = any/default channel.
    /// Lets the same template name exist on multiple channels.
    /// </summary>
    public string ChannelName { get; set; } = "";

    /// <summary>
    /// Body format. "Json" (default) builds JSON from Fields. "Raw" sends RawBody verbatim
    /// (after substituting {field} placeholders). Lets the user model arbitrary HTTP/MQ
    /// payload shapes (XML, form-encoded, custom protocols).
    /// </summary>
    public string BodyFormat { get; set; } = "Json";

    /// <summary>Raw body template. Supports {fieldName} substitution from Fields.</summary>
    public string RawBody { get; set; } = "";

    /// <summary>
    /// Build a HostMessage from this template, recursing into nested Object/ArrayList fields.
    /// </summary>
    public HostMessage BuildMessage()
    {
        var msg = new HostMessage
        {
            Name = Name,
            Description = Description,
            Direction = Direction,
            ChannelName = ChannelName,
            BodyFormat = BodyFormat,
            RawBody = RawBody,
        };
        foreach (var fieldDef in Fields)
            msg.Fields[fieldDef.Name] = BuildField(fieldDef);
        return msg;
    }

    private static HostField BuildField(HostFieldTemplate def)
    {
        var f = new HostField
        {
            Name = def.Name,
            Value = def.DefaultValue,
            Type = def.Type,
            Description = def.Description,
            Required = def.Required,
        };
        foreach (var c in def.Children)
            f.Children.Add(BuildField(c));
        return f;
    }
}

/// <summary>
/// Field definition in a host message template. Mirrors <see cref="HostField"/>'s shape so
/// nested Object / ArrayList types can describe the structure recursively.
/// </summary>
public class HostFieldTemplate
{
    public string Name { get; set; } = string.Empty;
    public HostFieldType Type { get; set; } = HostFieldType.ASCII;
    public string DefaultValue { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Required { get; set; }

    /// <summary>Nested children. Used when <see cref="Type"/> is Object or ArrayList.</summary>
    public List<HostFieldTemplate> Children { get; set; } = [];
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
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<HostMessageTemplateCollection>(json, opts)
            ?? new HostMessageTemplateCollection();
    }

    /// <summary>
    /// Save templates to JSON file.
    /// </summary>
    public void SaveToFile(string path)
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
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
