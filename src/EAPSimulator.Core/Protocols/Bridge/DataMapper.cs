using EAPSimulator.Core.Protocols.HostProtocol;
using EAPSimulator.Core.Protocols.SecsGem.SecsII;

namespace EAPSimulator.Core.Protocols.Bridge;

/// <summary>
/// Maps data between SECS messages and Host messages.
/// Supports field-level mapping with data conversion.
/// </summary>
public class DataMapper
{
    private readonly List<FieldMapping> _mappings = [];

    /// <summary>
    /// Add a field mapping.
    /// </summary>
    public void AddMapping(FieldMapping mapping)
    {
        _mappings.Add(mapping);
    }

    /// <summary>
    /// Remove all mappings.
    /// </summary>
    public void ClearMappings()
    {
        _mappings.Clear();
    }

    /// <summary>
    /// Get all mappings.
    /// </summary>
    public IReadOnlyList<FieldMapping> Mappings => _mappings;

    /// <summary>
    /// Map data from a SECS message to a Host message.
    /// </summary>
    public HostMessage MapSecsToHost(SecsMessage secsMsg, HostMessageTemplate hostTemplate)
    {
        var hostMsg = hostTemplate.BuildMessage();

        foreach (var mapping in _mappings)
        {
            if (mapping.Source == FieldMappingSource.Secs && mapping.Target == FieldMappingTarget.Host)
            {
                var value = ExtractSecsFieldValue(secsMsg, mapping.SecsPath);
                if (value != null)
                {
                    var converted = ConvertValue(value, mapping.Conversion);
                    hostMsg.SetFieldValue(mapping.HostFieldName, converted);
                }
            }
        }

        return hostMsg;
    }

    /// <summary>
    /// Map data from a Host message to a SECS message template.
    /// Returns a dictionary of field path -> value for building the SECS message.
    /// </summary>
    public Dictionary<string, string> MapHostToSecs(HostMessage hostMsg)
    {
        var result = new Dictionary<string, string>();

        foreach (var mapping in _mappings)
        {
            if (mapping.Source == FieldMappingSource.Host && mapping.Target == FieldMappingTarget.Secs)
            {
                var value = hostMsg.GetFieldValue(mapping.HostFieldName);
                if (value != null)
                {
                    var converted = ConvertValue(value, mapping.Conversion);
                    result[mapping.SecsPath] = converted;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Extract a field value from a SECS message by path (e.g., "0/1/2").
    /// </summary>
    private static string? ExtractSecsFieldValue(SecsMessage msg, string path)
    {
        if (msg.RootItem == null) return null;

        var parts = path.Split('/');
        SecsItem? current = msg.RootItem;

        foreach (var part in parts)
        {
            if (current == null) return null;
            if (current.Format != SecsFormat.List) return null;

            if (current is SecsList list && int.TryParse(part, out var index))
            {
                if (index < 0 || index >= list.Items.Length) return null;
                current = list.Items[index];
            }
            else
            {
                return null;
            }
        }

        return current?.ToString();
    }

    /// <summary>
    /// Convert a value based on the conversion type.
    /// </summary>
    private static string ConvertValue(string value, DataConversion conversion)
    {
        return conversion switch
        {
            DataConversion.None => value,
            DataConversion.ToUpper => value.ToUpperInvariant(),
            DataConversion.ToLower => value.ToLowerInvariant(),
            DataConversion.Trim => value.Trim(),
            DataConversion.IntToHex => int.TryParse(value, out var i) ? $"0x{i:X}" : value,
            DataConversion.HexToInt => value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(value[2..], System.Globalization.NumberStyles.HexNumber, null, out var h)
                ? h.ToString() : value,
            _ => value,
        };
    }
}

/// <summary>
/// Defines a mapping between a SECS field and a Host field.
/// </summary>
public class FieldMapping
{
    /// <summary>Source of the data.</summary>
    public FieldMappingSource Source { get; set; }

    /// <summary>Target of the data.</summary>
    public FieldMappingTarget Target { get; set; }

    /// <summary>Path in SECS message (e.g., "0/1/2").</summary>
    public string SecsPath { get; set; } = "";

    /// <summary>Field name in Host message.</summary>
    public string HostFieldName { get; set; } = "";

    /// <summary>Data conversion to apply.</summary>
    public DataConversion Conversion { get; set; } = DataConversion.None;

    /// <summary>Description of this mapping.</summary>
    public string Description { get; set; } = "";
}

/// <summary>
/// Source of field mapping.
/// </summary>
public enum FieldMappingSource
{
    Secs,
    Host
}

/// <summary>
/// Target of field mapping.
/// </summary>
public enum FieldMappingTarget
{
    Secs,
    Host
}

/// <summary>
/// Data conversion types.
/// </summary>
public enum DataConversion
{
    None,
    ToUpper,
    ToLower,
    Trim,
    IntToHex,
    HexToInt
}
