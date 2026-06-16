using EAPSimulator.Core.Protocols.SecsGem.SecsII;
using Newtonsoft.Json;

namespace EAPSimulator.Core.Protocols.SecsGem;

/// <summary>
/// A pre-defined SECS message template that can be loaded from JSON and sent with one click.
/// </summary>
public class SecsMessageTemplate
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("stream")]
    public byte Stream { get; set; }

    [JsonProperty("function")]
    public byte Function { get; set; }

    [JsonProperty("wbit")]
    public bool WBit { get; set; } = true;

    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Optional XML-like item definition. If empty, sends message with no items.
    /// Supported format: &lt;L&gt;&lt;A&gt;text&lt;/A&gt;&lt;U1&gt;123&lt;/U1&gt;&lt;/L&gt;
    /// </summary>
    [JsonProperty("itemXml")]
    public string ItemXml { get; set; } = string.Empty;

    /// <summary>
    /// Field metadata keyed by tree path (e.g. "0/1/2").
    /// </summary>
    [JsonProperty("fieldMetadata")]
    public Dictionary<string, FieldMetadata>? FieldMetadata { get; set; }

    /// <summary>
    /// Build the SecsMessage from this template.
    /// </summary>
    public SecsMessage BuildMessage()
    {
        SecsItem? rootItem = null;
        if (!string.IsNullOrWhiteSpace(ItemXml))
        {
            rootItem = ParseItemXml(ItemXml);
        }
        return new SecsMessage(Stream, Function, WBit, rootItem);
    }

    /// <summary>
    /// Simple XML-like parser for SECS item definitions.
    /// Supports: &lt;L&gt;, &lt;A&gt;, &lt;B&gt;, &lt;U1&gt;~&lt;U8&gt;, &lt;I1&gt;~&lt;I8&gt;, &lt;F4&gt;, &lt;F8&gt;, &lt;Boolean&gt;
    /// Multiple values in one tag: &lt;U1&gt;1,2,3&lt;/U1&gt;
    /// </summary>
    private static SecsItem? ParseItemXml(string xml)
    {
        xml = xml.Trim();
        if (string.IsNullOrEmpty(xml)) return null;

        var items = ParseItems(xml, 0, out _);
        return items.Count == 1 ? items[0] : (items.Count > 1 ? SecsItem.L(items.ToArray()) : null);
    }

    private static List<SecsItem> ParseItems(string xml, int start, out int end)
    {
        var items = new List<SecsItem>();
        var pos = start;

        while (pos < xml.Length)
        {
            // Skip whitespace
            while (pos < xml.Length && char.IsWhiteSpace(xml[pos])) pos++;
            if (pos >= xml.Length || xml[pos] != '<') { end = pos; return items; }

            // Stop at closing tag — let caller consume it via SkipClosingTag.
            // Without this, "<L></L>" recurses into the L body, sees "</L>" as a
            // tag named "/L", falls through ParseValueItem's switch default, and
            // synthesizes a phantom empty A item.
            if (pos + 1 < xml.Length && xml[pos + 1] == '/') { end = pos; return items; }

            // Find tag name
            var tagStart = pos + 1;
            var tagEnd = xml.IndexOf('>', tagStart);
            if (tagEnd < 0) { end = pos; return items; }

            var tagName = xml.Substring(tagStart, tagEnd - tagStart).Trim().ToUpper();
            pos = tagEnd + 1;

            if (tagName == "L")
            {
                // List: parse children until </L>
                var children = ParseItems(xml, pos, out pos);
                // Skip closing tag
                pos = SkipClosingTag(xml, pos, "L");
                items.Add(SecsItem.L(children.ToArray()));
            }
            else
            {
                // Value type: read text until closing tag
                var closeTag = $"</{tagName}>";
                var closeIdx = xml.IndexOf(closeTag, pos, StringComparison.OrdinalIgnoreCase);
                if (closeIdx < 0) closeIdx = xml.IndexOf($"</{tagName.ToLower()}>", pos, StringComparison.OrdinalIgnoreCase);
                if (closeIdx < 0) closeIdx = xml.IndexOf($"</{tagName.Substring(0, 1)}{tagName.Substring(1).ToLower()}>", pos, StringComparison.OrdinalIgnoreCase);

                if (closeIdx >= 0)
                {
                    var valueStr = xml.Substring(pos, closeIdx - pos).Trim();
                    // If value contains nested XML tags (e.g. <A><L>...</L></A>),
                    // treat inner content as list items instead of plain text
                    if (valueStr.Length > 0 && valueStr[0] == '<')
                    {
                        var innerItems = ParseItems(valueStr, 0, out _);
                        if (innerItems.Count > 0)
                        {
                            items.Add(innerItems.Count == 1 ? innerItems[0] : SecsItem.L(innerItems.ToArray()));
                        }
                        else
                        {
                            items.Add(ParseValueItem(tagName, valueStr));
                        }
                    }
                    else
                    {
                        items.Add(ParseValueItem(tagName, valueStr));
                    }
                    pos = closeIdx + closeTag.Length;
                }
                else
                {
                    var valueStr = xml.Substring(pos).Trim();
                    items.Add(ParseValueItem(tagName, valueStr));
                    pos = xml.Length;
                }
            }
        }

        end = pos;
        return items;
    }

    private static int SkipClosingTag(string xml, int pos, string tagName)
    {
        while (pos < xml.Length && char.IsWhiteSpace(xml[pos])) pos++;
        var closeTag = $"</{tagName}>";
        if (pos + closeTag.Length <= xml.Length &&
            xml.Substring(pos, closeTag.Length).Equals(closeTag, StringComparison.OrdinalIgnoreCase))
        {
            return pos + closeTag.Length;
        }
        // Also try lowercase
        closeTag = $"</{tagName.ToLower()}>";
        if (pos + closeTag.Length <= xml.Length &&
            xml.Substring(pos, closeTag.Length).Equals(closeTag, StringComparison.OrdinalIgnoreCase))
        {
            return pos + closeTag.Length;
        }
        return pos;
    }

    private static SecsItem ParseValueItem(string tag, string value)
    {
        return tag switch
        {
            "A" => SecsItem.A(value),
            "B" => SecsItem.B(ParseHexBytes(value)),
            "BOOLEAN" => SecsItem.Boolean(value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase)),
            "U1" => SecsItem.U1(value.Split(',').Select(s => byte.Parse(s.Trim())).ToArray()),
            "U2" => SecsItem.U2(value.Split(',').Select(s => ushort.Parse(s.Trim())).ToArray()),
            "U4" => SecsItem.U4(value.Split(',').Select(s => uint.Parse(s.Trim())).ToArray()),
            "U8" => SecsItem.U8(value.Split(',').Select(s => ulong.Parse(s.Trim())).ToArray()),
            "I1" => SecsItem.I1(value.Split(',').Select(s => sbyte.Parse(s.Trim())).ToArray()),
            "I2" => SecsItem.I2(value.Split(',').Select(s => short.Parse(s.Trim())).ToArray()),
            "I4" => SecsItem.I4(value.Split(',').Select(s => int.Parse(s.Trim())).ToArray()),
            "I8" => SecsItem.I8(value.Split(',').Select(s => long.Parse(s.Trim())).ToArray()),
            "F4" => SecsItem.F4(value.Split(',').Select(s => float.Parse(s.Trim())).ToArray()),
            "F8" => SecsItem.F8(value.Split(',').Select(s => double.Parse(s.Trim())).ToArray()),
            _ => SecsItem.A(value),
        };
    }

    private static byte[] ParseHexBytes(string hex)
    {
        hex = hex.Replace(" ", "").Replace("-", "");
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }

    public override string ToString() => $"S{Stream}F{Function}{(WBit ? "W" : "")} - {Name}";
}

/// <summary>
/// Metadata for a single field in a SECS message.
/// </summary>
public class FieldMetadata
{
    [JsonProperty("alias")]
    public string? Alias { get; set; }

    [JsonProperty("description")]
    public string? Description { get; set; }

    [JsonProperty("format")]
    public string? Format { get; set; }

    [JsonProperty("nlb")]
    public string? Nlb { get; set; }

    [JsonProperty("defaultValue")]
    public string? DefaultValue { get; set; }

    [JsonProperty("valueMappings")]
    public Dictionary<string, string>? ValueMappings { get; set; }
}

/// <summary>
/// Root object for the JSON template file.
/// </summary>
public class SecsMessageTemplateFile
{
    [JsonProperty("messages")]
    public List<SecsMessageTemplate> Messages { get; set; } = new();

    public static SecsMessageTemplateFile LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);
        return JsonConvert.DeserializeObject<SecsMessageTemplateFile>(json)
            ?? throw new InvalidOperationException($"Failed to load templates from {path}");
    }
}
