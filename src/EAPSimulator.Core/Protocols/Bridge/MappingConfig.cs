using Newtonsoft.Json;

namespace EAPSimulator.Core.Protocols.Bridge;

/// <summary>
/// A named collection of <see cref="FieldMapping"/> rules tied to a specific SECS / Host
/// template pair. Lets the user organise mappings by business event (e.g. "EventReport",
/// "RemoteCommand") instead of one flat list. Persistence target for the new UI editor.
/// </summary>
public class MappingGroup
{
    [JsonProperty("name")]
    public string Name { get; set; } = "";

    /// <summary>Name of the SECS template this group operates on (e.g. "S6F11").</summary>
    [JsonProperty("secsTemplate")]
    public string SecsTemplate { get; set; } = "";

    /// <summary>Name of the Host template this group operates on (e.g. "EventReport").</summary>
    [JsonProperty("hostTemplate")]
    public string HostTemplate { get; set; } = "";

    [JsonProperty("mappings")]
    public List<FieldMapping> Mappings { get; set; } = [];

    /// <summary>Human-readable description shown in the list.</summary>
    [JsonProperty("description")]
    public string Description { get; set; } = "";

    /// <summary>Whether this group's mappings are pushed into the live <see cref="DataMapper"/>.</summary>
    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Top-level JSON container for the mapping editor. One file holds every group; the bridge
/// loads them all on startup and the UI lets the user enable/disable individual groups
/// without deleting them.
/// </summary>
public class MappingConfig
{
    [JsonProperty("groups")]
    public List<MappingGroup> Groups { get; set; } = [];

    /// <summary>
    /// Default on-disk path next to the executable. Matches the convention used by the other
    /// config files (auto_reply_rules.json, host_message_templates.json, …).
    /// </summary>
    public static string GetDefaultPath()
    {
        var basePath = AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(basePath, "bridge_mappings.json");
    }

    public static MappingConfig Load(string? path = null)
    {
        var p = path ?? GetDefaultPath();
        if (!File.Exists(p)) return new MappingConfig();
        try
        {
            var json = File.ReadAllText(p);
            return JsonConvert.DeserializeObject<MappingConfig>(json) ?? new MappingConfig();
        }
        catch
        {
            // Malformed file shouldn't kill the app — start with an empty config and let the
            // user re-author. Loud failure would block startup for a non-critical feature.
            return new MappingConfig();
        }
    }

    public void Save(string? path = null)
    {
        var p = path ?? GetDefaultPath();
        var json = JsonConvert.SerializeObject(this, Formatting.Indented);
        File.WriteAllText(p, json);
    }

    /// <summary>
    /// Replace the live mapper's rules with the enabled groups' flattened mappings. Call this
    /// after loading the config or after the user clicks "Apply" in the editor.
    /// </summary>
    public void ApplyTo(DataMapper mapper)
    {
        mapper.ClearMappings();
        foreach (var g in Groups.Where(g => g.Enabled))
            foreach (var m in g.Mappings)
                mapper.AddMapping(m);
    }
}
