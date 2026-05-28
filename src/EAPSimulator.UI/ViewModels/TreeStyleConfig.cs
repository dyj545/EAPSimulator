using System.Text.Json;
using System.Text.Json.Serialization;

namespace EAPSimulator.UI.ViewModels;

/// <summary>
/// Configurable style settings for the message tree view.
/// Persisted to tree_style.json in the app's working directory.
/// </summary>
public class TreeStyleConfig
{
    private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tree_style.json");
    private static TreeStyleConfig? _instance;

    // ─── Font ───
    public string FontFamily { get; set; } = "Microsoft YaHei";
    public double FontSize { get; set; } = 12;

    // ─── Colors ───
    public string TypeNameColor { get; set; } = "#569CD6";
    public string ValueColor { get; set; } = "#E0E0E0";
    public string AliasColor { get; set; } = "#9CDCFE";
    public string ListInfoColor { get; set; } = "#B5CEA8";
    public string GroupColor { get; set; } = "#E0E0E0";
    public string PairTitleColor { get; set; } = "#C586C0";
    public string PairDescColor { get; set; } = "#DCDCAA";
    public string StreamFuncColor { get; set; } = "#B5CEA8";
    public string StreamFuncPrefixColor { get; set; } = "#569CD6";
    public string WBitColor { get; set; } = "#FF9800";
    public string MessageNameColor { get; set; } = "#DCDCAA";
    public string SelectedColor { get; set; } = "#FFFFFF";

    // ─── Font Weight ───
    public string GroupFontWeight { get; set; } = "Bold";
    public string PairTitleFontWeight { get; set; } = "SemiBold";
    public string WBitFontWeight { get; set; } = "Bold";

    public static TreeStyleConfig Instance => _instance ??= Load();

    public static TreeStyleConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize(json, TreeStyleConfigContext.Default.TreeStyleConfig) ?? new TreeStyleConfig();
            }
        }
        catch { }
        return new TreeStyleConfig();
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, TreeStyleConfigContext.Default.TreeStyleConfig);
            File.WriteAllText(ConfigPath, json);
        }
        catch { }
    }

    public void ResetToDefaults()
    {
        FontFamily = "Microsoft YaHei";
        FontSize = 12;
        TypeNameColor = "#569CD6";
        ValueColor = "#E0E0E0";
        AliasColor = "#9CDCFE";
        ListInfoColor = "#B5CEA8";
        GroupColor = "#E0E0E0";
        PairTitleColor = "#C586C0";
        PairDescColor = "#DCDCAA";
        StreamFuncColor = "#B5CEA8";
        StreamFuncPrefixColor = "#569CD6";
        WBitColor = "#FF9800";
        MessageNameColor = "#DCDCAA";
        SelectedColor = "#FFFFFF";
        GroupFontWeight = "Bold";
        PairTitleFontWeight = "SemiBold";
        WBitFontWeight = "Bold";
    }

    // ─── Preset Styles ───

    private static readonly Dictionary<string, Dictionary<string, string>> PresetStyles = new()
    {
        ["VS Code Dark"] = new()
        {
            ["FontFamily"] = "Microsoft YaHei", ["FontSize"] = "12",
            ["TypeNameColor"] = "#569CD6", ["ValueColor"] = "#E0E0E0", ["AliasColor"] = "#9CDCFE",
            ["ListInfoColor"] = "#B5CEA8", ["GroupColor"] = "#E0E0E0", ["PairTitleColor"] = "#C586C0",
            ["PairDescColor"] = "#DCDCAA", ["StreamFuncColor"] = "#B5CEA8", ["StreamFuncPrefixColor"] = "#569CD6",
            ["WBitColor"] = "#FF9800", ["MessageNameColor"] = "#DCDCAA", ["SelectedColor"] = "#FFFFFF",
            ["GroupFontWeight"] = "Bold", ["PairTitleFontWeight"] = "SemiBold", ["WBitFontWeight"] = "Bold",
        },
        ["Monokai"] = new()
        {
            ["FontFamily"] = "Consolas", ["FontSize"] = "12",
            ["TypeNameColor"] = "#F92672", ["ValueColor"] = "#F8F8F2", ["AliasColor"] = "#A6E22E",
            ["ListInfoColor"] = "#E6DB74", ["GroupColor"] = "#F8F8F2", ["PairTitleColor"] = "#66D9EF",
            ["PairDescColor"] = "#E6DB74", ["StreamFuncColor"] = "#A6E22E", ["StreamFuncPrefixColor"] = "#F92672",
            ["WBitColor"] = "#FD971F", ["MessageNameColor"] = "#E6DB74", ["SelectedColor"] = "#FFFFFF",
            ["GroupFontWeight"] = "Bold", ["PairTitleFontWeight"] = "SemiBold", ["WBitFontWeight"] = "Bold",
        },
        ["Solarized Dark"] = new()
        {
            ["FontFamily"] = "Consolas", ["FontSize"] = "12",
            ["TypeNameColor"] = "#268BD2", ["ValueColor"] = "#839496", ["AliasColor"] = "#2AA198",
            ["ListInfoColor"] = "#B58900", ["GroupColor"] = "#93A1A1", ["PairTitleColor"] = "#6C71C4",
            ["PairDescColor"] = "#CB4B16", ["StreamFuncColor"] = "#859900", ["StreamFuncPrefixColor"] = "#268BD2",
            ["WBitColor"] = "#D33682", ["MessageNameColor"] = "#B58900", ["SelectedColor"] = "#FDF6E3",
            ["GroupFontWeight"] = "Bold", ["PairTitleFontWeight"] = "SemiBold", ["WBitFontWeight"] = "Bold",
        },
        ["One Dark Pro"] = new()
        {
            ["FontFamily"] = "Microsoft YaHei", ["FontSize"] = "12",
            ["TypeNameColor"] = "#61AFEF", ["ValueColor"] = "#ABB2BF", ["AliasColor"] = "#98C379",
            ["ListInfoColor"] = "#E5C07B", ["GroupColor"] = "#ABB2BF", ["PairTitleColor"] = "#C678DD",
            ["PairDescColor"] = "#E5C07B", ["StreamFuncColor"] = "#98C379", ["StreamFuncPrefixColor"] = "#61AFEF",
            ["WBitColor"] = "#E06C75", ["MessageNameColor"] = "#E5C07B", ["SelectedColor"] = "#FFFFFF",
            ["GroupFontWeight"] = "Bold", ["PairTitleFontWeight"] = "SemiBold", ["WBitFontWeight"] = "Bold",
        },
        ["Dracula"] = new()
        {
            ["FontFamily"] = "Consolas", ["FontSize"] = "12",
            ["TypeNameColor"] = "#BD93F9", ["ValueColor"] = "#F8F8F2", ["AliasColor"] = "#50FA7B",
            ["ListInfoColor"] = "#F1FA8C", ["GroupColor"] = "#F8F8F2", ["PairTitleColor"] = "#FF79C6",
            ["PairDescColor"] = "#F1FA8C", ["StreamFuncColor"] = "#50FA7B", ["StreamFuncPrefixColor"] = "#BD93F9",
            ["WBitColor"] = "#FFB86C", ["MessageNameColor"] = "#F1FA8C", ["SelectedColor"] = "#FFFFFF",
            ["GroupFontWeight"] = "Bold", ["PairTitleFontWeight"] = "SemiBold", ["WBitFontWeight"] = "Bold",
        },
        ["Nord"] = new()
        {
            ["FontFamily"] = "Consolas", ["FontSize"] = "12",
            ["TypeNameColor"] = "#81A1C1", ["ValueColor"] = "#D8DEE9", ["AliasColor"] = "#A3BE8C",
            ["ListInfoColor"] = "#EBCB8B", ["GroupColor"] = "#ECEFF4", ["PairTitleColor"] = "#B48EAD",
            ["PairDescColor"] = "#EBCB8B", ["StreamFuncColor"] = "#A3BE8C", ["StreamFuncPrefixColor"] = "#81A1C1",
            ["WBitColor"] = "#D08770", ["MessageNameColor"] = "#EBCB8B", ["SelectedColor"] = "#FFFFFF",
            ["GroupFontWeight"] = "Bold", ["PairTitleFontWeight"] = "SemiBold", ["WBitFontWeight"] = "Bold",
        },
        ["Gruvbox Dark"] = new()
        {
            ["FontFamily"] = "Microsoft YaHei", ["FontSize"] = "12",
            ["TypeNameColor"] = "#83A598", ["ValueColor"] = "#EBDBB2", ["AliasColor"] = "#B8BB26",
            ["ListInfoColor"] = "#FABD2F", ["GroupColor"] = "#EBDBB2", ["PairTitleColor"] = "#D3869B",
            ["PairDescColor"] = "#FABD2F", ["StreamFuncColor"] = "#B8BB26", ["StreamFuncPrefixColor"] = "#83A598",
            ["WBitColor"] = "#FE8019", ["MessageNameColor"] = "#FABD2F", ["SelectedColor"] = "#FBF1C7",
            ["GroupFontWeight"] = "Bold", ["PairTitleFontWeight"] = "SemiBold", ["WBitFontWeight"] = "Bold",
        },
        ["Material Ocean"] = new()
        {
            ["FontFamily"] = "Microsoft YaHei", ["FontSize"] = "12",
            ["TypeNameColor"] = "#82AAFF", ["ValueColor"] = "#8F93A2", ["AliasColor"] = "#C3E88D",
            ["ListInfoColor"] = "#FFCB6B", ["GroupColor"] = "#A6ACCD", ["PairTitleColor"] = "#C792EA",
            ["PairDescColor"] = "#FFCB6B", ["StreamFuncColor"] = "#C3E88D", ["StreamFuncPrefixColor"] = "#82AAFF",
            ["WBitColor"] = "#FF5370", ["MessageNameColor"] = "#FFCB6B", ["SelectedColor"] = "#FFFFFF",
            ["GroupFontWeight"] = "Bold", ["PairTitleFontWeight"] = "SemiBold", ["WBitFontWeight"] = "Bold",
        },
    };

    public static IEnumerable<string> GetPresetStyleNames() => PresetStyles.Keys;

    public void ApplyPreset(string presetName)
    {
        if (!PresetStyles.TryGetValue(presetName, out var preset)) return;
        if (preset.TryGetValue("FontFamily", out var ff)) FontFamily = ff;
        if (preset.TryGetValue("FontSize", out var fs) && double.TryParse(fs, out var fsVal)) FontSize = fsVal;
        if (preset.TryGetValue("TypeNameColor", out var c1)) TypeNameColor = c1;
        if (preset.TryGetValue("ValueColor", out var c2)) ValueColor = c2;
        if (preset.TryGetValue("AliasColor", out var c3)) AliasColor = c3;
        if (preset.TryGetValue("ListInfoColor", out var c4)) ListInfoColor = c4;
        if (preset.TryGetValue("GroupColor", out var c5)) GroupColor = c5;
        if (preset.TryGetValue("PairTitleColor", out var c6)) PairTitleColor = c6;
        if (preset.TryGetValue("PairDescColor", out var c7)) PairDescColor = c7;
        if (preset.TryGetValue("StreamFuncColor", out var c8)) StreamFuncColor = c8;
        if (preset.TryGetValue("StreamFuncPrefixColor", out var c9)) StreamFuncPrefixColor = c9;
        if (preset.TryGetValue("WBitColor", out var c10)) WBitColor = c10;
        if (preset.TryGetValue("MessageNameColor", out var c11)) MessageNameColor = c11;
        if (preset.TryGetValue("SelectedColor", out var c12)) SelectedColor = c12;
        if (preset.TryGetValue("GroupFontWeight", out var w1)) GroupFontWeight = w1;
        if (preset.TryGetValue("PairTitleFontWeight", out var w2)) PairTitleFontWeight = w2;
        if (preset.TryGetValue("WBitFontWeight", out var w3)) WBitFontWeight = w3;
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(TreeStyleConfig))]
internal partial class TreeStyleConfigContext : JsonSerializerContext { }
