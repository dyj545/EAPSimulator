using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace EAPSimulator.UI.ViewModels;

/// <summary>
/// ViewModel for the tree style configuration panel.
/// Changes are applied in real-time and can be saved or reset.
/// </summary>
public partial class TreeStyleViewModel : ObservableObject
{
    private readonly TreeStyleConfig _config;
    public event Action? StyleChanged;

    [ObservableProperty] private string _fontFamily;
    [ObservableProperty] private double _fontSize;
    [ObservableProperty] private string _typeNameColor;
    [ObservableProperty] private string _valueColor;
    [ObservableProperty] private string _aliasColor;
    [ObservableProperty] private string _listInfoColor;
    [ObservableProperty] private string _groupColor;
    [ObservableProperty] private string _pairTitleColor;
    [ObservableProperty] private string _pairDescColor;
    [ObservableProperty] private string _streamFuncColor;
    [ObservableProperty] private string _streamFuncPrefixColor;
    [ObservableProperty] private string _wbitColor;
    [ObservableProperty] private string _messageNameColor;
    [ObservableProperty] private string _selectedColor;
    [ObservableProperty] private string _groupFontWeight;
    [ObservableProperty] private string _pairTitleFontWeight;
    [ObservableProperty] private string _wbitFontWeight;

    public ObservableCollection<string> FontFamilies { get; } =
    [
        "Microsoft YaHei",
        "Consolas",
        "SimSun",
        "SimHei",
        "KaiTi",
        "Segoe UI",
        "Arial",
    ];

    public ObservableCollection<string> FontWeights { get; } =
    [
        "Normal",
        "Bold",
        "SemiBold",
        "Light",
    ];

    public ObservableCollection<double> FontSizes { get; } =
    [
        10, 11, 12, 13, 14, 15, 16,
    ];

    /// <summary>
    /// Curated preset color palette for color pickers.
    /// </summary>
    public static string[] PresetColors { get; } =
    [
        "#FFFFFF", "#E0E0E0", "#BDBDBD", "#9E9E9E", "#757575", "#616161",
        "#F44336", "#E91E63", "#9C27B0", "#673AB7", "#3F51B5", "#2196F3",
        "#03A9F4", "#00BCD4", "#009688", "#4CAF50", "#8BC34A", "#CDDC39",
        "#FFEB3B", "#FFC107", "#FF9800", "#FF5722", "#795548", "#607D8B",
        "#EF5350", "#EC407A", "#AB47BC", "#7E57C2", "#5C6BC0", "#42A5F5",
        "#29B6F6", "#26C6DA", "#26A69A", "#66BB6A", "#9CCC65", "#D4E157",
        "#FFEE58", "#FFCA28", "#FFA726", "#FF7043", "#8D6E63", "#78909C",
        "#569CD6", "#9CDCFE", "#B5CEA8", "#C586C0", "#DCDCAA", "#6A9955",
        "#4EC9B0", "#D7BA7D", "#CE9178", "#F44747", "#FF9800", "#4FC1FF",
        "#C678DD", "#E06C75", "#98C379", "#D19A66", "#61AFEF", "#56B6C2",
    ];

    public ObservableCollection<string> PresetStyleNames { get; } =
        new(TreeStyleConfig.GetPresetStyleNames());

    public TreeStyleViewModel(TreeStyleConfig config)
    {
        _config = config;
        _fontFamily = config.FontFamily;
        _fontSize = config.FontSize;
        _typeNameColor = config.TypeNameColor;
        _valueColor = config.ValueColor;
        _aliasColor = config.AliasColor;
        _listInfoColor = config.ListInfoColor;
        _groupColor = config.GroupColor;
        _pairTitleColor = config.PairTitleColor;
        _pairDescColor = config.PairDescColor;
        _streamFuncColor = config.StreamFuncColor;
        _streamFuncPrefixColor = config.StreamFuncPrefixColor;
        _wbitColor = config.WBitColor;
        _messageNameColor = config.MessageNameColor;
        _selectedColor = config.SelectedColor;
        _groupFontWeight = config.GroupFontWeight;
        _pairTitleFontWeight = config.PairTitleFontWeight;
        _wbitFontWeight = config.WBitFontWeight;
    }

    partial void OnFontFamilyChanged(string value) { _config.FontFamily = value; Notify(); }
    partial void OnFontSizeChanged(double value) { _config.FontSize = value; Notify(); }
    partial void OnTypeNameColorChanged(string value) { _config.TypeNameColor = value; Notify(); }
    partial void OnValueColorChanged(string value) { _config.ValueColor = value; Notify(); }
    partial void OnAliasColorChanged(string value) { _config.AliasColor = value; Notify(); }
    partial void OnListInfoColorChanged(string value) { _config.ListInfoColor = value; Notify(); }
    partial void OnGroupColorChanged(string value) { _config.GroupColor = value; Notify(); }
    partial void OnPairTitleColorChanged(string value) { _config.PairTitleColor = value; Notify(); }
    partial void OnPairDescColorChanged(string value) { _config.PairDescColor = value; Notify(); }
    partial void OnStreamFuncColorChanged(string value) { _config.StreamFuncColor = value; Notify(); }
    partial void OnStreamFuncPrefixColorChanged(string value) { _config.StreamFuncPrefixColor = value; Notify(); }
    partial void OnWbitColorChanged(string value) { _config.WBitColor = value; Notify(); }
    partial void OnMessageNameColorChanged(string value) { _config.MessageNameColor = value; Notify(); }
    partial void OnSelectedColorChanged(string value) { _config.SelectedColor = value; Notify(); }
    partial void OnGroupFontWeightChanged(string value) { _config.GroupFontWeight = value; Notify(); }
    partial void OnPairTitleFontWeightChanged(string value) { _config.PairTitleFontWeight = value; Notify(); }
    partial void OnWbitFontWeightChanged(string value) { _config.WBitFontWeight = value; Notify(); }

    private void Notify()
    {
        StyleChanged?.Invoke();
    }

    [RelayCommand]
    private void Save()
    {
        _config.Save();
    }

    [RelayCommand]
    private void ResetDefaults()
    {
        _config.ResetToDefaults();
        SyncFromConfig();
        Notify();
    }

    [RelayCommand]
    private void ApplyPreset(string? presetName)
    {
        if (string.IsNullOrEmpty(presetName)) return;
        _config.ApplyPreset(presetName);
        SyncFromConfig();
        Notify();
    }

    private void SyncFromConfig()
    {
        FontFamily = _config.FontFamily;
        FontSize = _config.FontSize;
        TypeNameColor = _config.TypeNameColor;
        ValueColor = _config.ValueColor;
        AliasColor = _config.AliasColor;
        ListInfoColor = _config.ListInfoColor;
        GroupColor = _config.GroupColor;
        PairTitleColor = _config.PairTitleColor;
        PairDescColor = _config.PairDescColor;
        StreamFuncColor = _config.StreamFuncColor;
        StreamFuncPrefixColor = _config.StreamFuncPrefixColor;
        WbitColor = _config.WBitColor;
        MessageNameColor = _config.MessageNameColor;
        SelectedColor = _config.SelectedColor;
        GroupFontWeight = _config.GroupFontWeight;
        PairTitleFontWeight = _config.PairTitleFontWeight;
        WbitFontWeight = _config.WBitFontWeight;
    }
}
