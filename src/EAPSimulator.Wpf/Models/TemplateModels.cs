using Newtonsoft.Json;

namespace EAPSimulator.Wpf.Models;

public class TemplateData
{
    [JsonProperty("messages")]
    public List<MessageTemplate> Messages { get; set; } = [];
}

public class MessageTemplate
{
    [JsonProperty("name")]
    public string Name { get; set; } = "";

    [JsonProperty("stream")]
    public byte Stream { get; set; }

    [JsonProperty("function")]
    public byte Function { get; set; }

    [JsonProperty("wbit")]
    public bool WBit { get; set; }

    [JsonProperty("description")]
    public string Description { get; set; } = "";

    [JsonProperty("itemXml")]
    public string ItemXml { get; set; } = "";

    [JsonProperty("fieldMetadata")]
    public Dictionary<string, FieldMeta>? FieldMetadata { get; set; }
}

public class FieldMeta
{
    [JsonProperty("alias")]
    public string Alias { get; set; } = "";

    [JsonProperty("description")]
    public string Description { get; set; } = "";

    [JsonProperty("format")]
    public string Format { get; set; } = "";
}

/// <summary>
/// Simple ViewModel wrapper for displaying a message template in lists (used by AutoReply).
/// </summary>
public class MessageTemplateViewModel
{
    public string Name { get; }
    public byte Stream { get; }
    public byte Function { get; }
    public bool WBit { get; }
    public string Description { get; }
    public string DisplayName => $"S{Stream}F{Function} - {Name}";

    public MessageTemplateViewModel(MessageTemplate template)
    {
        Name = template.Name;
        Stream = template.Stream;
        Function = template.Function;
        WBit = template.WBit;
        Description = template.Description;
    }
}
