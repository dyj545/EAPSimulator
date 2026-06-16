using Newtonsoft.Json;

namespace EAPSimulator.Core.Protocols.SecsGem.AutoReply;

/// <summary>
/// Root config containing quick-reply rules and scenarios.
/// </summary>
public class AutoReplyConfig
{
    [JsonProperty("quickReplies")]
    public List<AutoReplyRule> QuickReplies { get; set; } = [];

    [JsonProperty("scenarios")]
    public List<ScenarioDefinition> Scenarios { get; set; } = [];

    public static AutoReplyConfig LoadFromFile(string path)
    {
        if (!File.Exists(path))
            return new AutoReplyConfig();

        var json = File.ReadAllText(path);
        var cfg = JsonConvert.DeserializeObject<AutoReplyConfig>(json) ?? new AutoReplyConfig();
        // Convert legacy (Receive carrying parked actionTemplateName) into Receive+Reply pairs.
        ScenarioMigration.MigrateLegacy(cfg.Scenarios);
        return cfg;
    }

    public void SaveToFile(string path)
    {
        var json = JsonConvert.SerializeObject(this, Formatting.Indented);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, json);
    }

    public static string GetDefaultPath()
    {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "auto_reply_rules.json");
    }
}
