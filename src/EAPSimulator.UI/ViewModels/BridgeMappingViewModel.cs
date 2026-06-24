using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EAPSimulator.Core.Protocols.Bridge;
using EAPSimulator.Core.Protocols.HostProtocol;
using EAPSimulator.Core.Protocols.SecsGem;

namespace EAPSimulator.UI.ViewModels;

/// <summary>
/// Top-level VM for the Bridge Mapping editor: a list of <see cref="MappingGroupViewModel"/>
/// plus load/save commands. Wires into <see cref="EapBridge.Mapper"/> when MainViewModel
/// passes the live bridge in so edits take effect immediately on the running protocol.
/// </summary>
public partial class BridgeMappingViewModel : ObservableObject
{
    public BridgeMappingViewModel()
    {
        // Auto-load on construction so the user sees their saved groups immediately. Missing
        // file is fine — the config returns empty Groups.
        try
        {
            LoadConfigCommand.Execute(null);
        }
        catch { /* never block startup on a config issue */ }
    }

    public ObservableCollection<MappingGroupViewModel> Groups { get; } = [];

    [ObservableProperty]
    private MappingGroupViewModel? _selectedGroup;

    [ObservableProperty]
    private FieldMappingViewModel? _selectedMapping;

    [ObservableProperty]
    private string _statusMessage = "";

    /// <summary>The path the user last loaded from / will save to. Defaults to next-to-exe.</summary>
    [ObservableProperty]
    private string _configPath = MappingConfig.GetDefaultPath();

    /// <summary>
    /// Bound to the running <see cref="EapBridge"/> by MainViewModel — null when no protocol
    /// is connected yet. Edits stay in the VM until applied; saving always pushes to bridge.
    /// </summary>
    private EapBridge? _bridge;

    /// <summary>SECS template name suggestions for the Group editor.</summary>
    public ObservableCollection<string> SecsTemplateNames { get; } = [];

    /// <summary>Host template name suggestions for the Group editor.</summary>
    public ObservableCollection<string> HostTemplateNames { get; } = [];

    /// <summary>
    /// Lookup of SECS templates by name — fed by MainViewModel after loading the template
    /// file. The canvas resolves <see cref="MappingGroupViewModel.SecsTemplate"/> via this
    /// so it can render the item tree without re-parsing JSON each time.
    /// </summary>
    public Dictionary<string, SecsMessageTemplate> SecsTemplateLookup { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Host template lookup, same purpose as <see cref="SecsTemplateLookup"/>.</summary>
    public Dictionary<string, HostMessageTemplate> HostTemplateLookup { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True = show the drag-and-drop canvas; false = show the table editor.</summary>
    [ObservableProperty]
    private bool _isCanvasView;

    public bool IsTableView => !IsCanvasView;
    partial void OnIsCanvasViewChanged(bool value) => OnPropertyChanged(nameof(IsTableView));

    /// <summary>Conversion options exposed to the detail combo-box.</summary>
    public string[] ConversionNames => Enum.GetNames<DataConversion>();

    public void AttachBridge(EapBridge bridge) => _bridge = bridge;

    [RelayCommand]
    private void AddGroup()
    {
        var g = new MappingGroupViewModel
        {
            Name = $"Group{Groups.Count + 1}",
            Enabled = true,
        };
        Groups.Add(g);
        SelectedGroup = g;
    }

    [RelayCommand]
    private void DeleteGroup()
    {
        if (SelectedGroup == null) return;
        var idx = Groups.IndexOf(SelectedGroup);
        Groups.Remove(SelectedGroup);
        SelectedGroup = Groups.Count > 0 ? Groups[Math.Min(idx, Groups.Count - 1)] : null;
    }

    [RelayCommand]
    private void AddMapping()
    {
        if (SelectedGroup == null) return;
        var m = new FieldMappingViewModel
        {
            Source = FieldMappingSource.Secs,
            Target = FieldMappingTarget.Host,
            SecsPath = "1/0",
            HostFieldName = "field1",
        };
        SelectedGroup.Mappings.Add(m);
        SelectedMapping = m;
    }

    [RelayCommand]
    private void DeleteMapping()
    {
        if (SelectedGroup == null || SelectedMapping == null) return;
        SelectedGroup.Mappings.Remove(SelectedMapping);
        SelectedMapping = null;
    }

    [RelayCommand]
    private void SaveConfig()
    {
        var cfg = ToModel();
        cfg.Save(ConfigPath);
        if (_bridge != null) cfg.ApplyTo(_bridge.Mapper);
        StatusMessage = $"已保存 {Groups.Count} 组映射到 {Path.GetFileName(ConfigPath)}";
    }

    [RelayCommand]
    private void LoadConfig()
    {
        var cfg = MappingConfig.Load(ConfigPath);
        Groups.Clear();
        foreach (var g in cfg.Groups)
            Groups.Add(MappingGroupViewModel.FromModel(g));
        SelectedGroup = Groups.FirstOrDefault();
        if (_bridge != null) cfg.ApplyTo(_bridge.Mapper);
        StatusMessage = $"已加载 {Groups.Count} 组映射";
    }

    public MappingConfig ToModel() => new()
    {
        Groups = Groups.Select(g => g.ToModel()).ToList(),
    };
}

/// <summary>
/// One named mapping group editing surface. Bridges the on-disk <see cref="MappingGroup"/> to
/// the UI and emits change notifications so the canvas / list views refresh as users type.
/// </summary>
public partial class MappingGroupViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _secsTemplate = "";

    [ObservableProperty]
    private string _hostTemplate = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private bool _enabled = true;

    public ObservableCollection<FieldMappingViewModel> Mappings { get; } = [];

    public MappingGroup ToModel() => new()
    {
        Name = Name,
        SecsTemplate = SecsTemplate,
        HostTemplate = HostTemplate,
        Description = Description,
        Enabled = Enabled,
        Mappings = Mappings.Select(m => m.ToModel()).ToList(),
    };

    public static MappingGroupViewModel FromModel(MappingGroup g)
    {
        var vm = new MappingGroupViewModel
        {
            Name = g.Name,
            SecsTemplate = g.SecsTemplate,
            HostTemplate = g.HostTemplate,
            Description = g.Description,
            Enabled = g.Enabled,
        };
        foreach (var m in g.Mappings)
            vm.Mappings.Add(FieldMappingViewModel.FromModel(m));
        return vm;
    }
}

/// <summary>
/// Editing surface for one <see cref="FieldMapping"/> row. Mirrors the data model 1:1; the
/// detail panel binds directly to these properties.
/// </summary>
public partial class FieldMappingViewModel : ObservableObject
{
    [ObservableProperty]
    private FieldMappingSource _source = FieldMappingSource.Secs;

    [ObservableProperty]
    private FieldMappingTarget _target = FieldMappingTarget.Host;

    [ObservableProperty]
    private string _secsPath = "";

    [ObservableProperty]
    private string _hostFieldName = "";

    [ObservableProperty]
    private DataConversion _conversion = DataConversion.None;

    [ObservableProperty]
    private string _description = "";

    public string DisplayText => $"{(Source == FieldMappingSource.Secs ? "SECS" : "Host")}" +
        $"[{(Source == FieldMappingSource.Secs ? SecsPath : HostFieldName)}] → " +
        $"{(Target == FieldMappingTarget.Host ? "Host" : "SECS")}" +
        $"[{(Target == FieldMappingTarget.Host ? HostFieldName : SecsPath)}]" +
        (Conversion == DataConversion.None ? "" : $" ({Conversion})");

    partial void OnSourceChanged(FieldMappingSource value) => OnPropertyChanged(nameof(DisplayText));
    partial void OnTargetChanged(FieldMappingTarget value) => OnPropertyChanged(nameof(DisplayText));
    partial void OnSecsPathChanged(string value) => OnPropertyChanged(nameof(DisplayText));
    partial void OnHostFieldNameChanged(string value) => OnPropertyChanged(nameof(DisplayText));
    partial void OnConversionChanged(DataConversion value) => OnPropertyChanged(nameof(DisplayText));

    public string SourceName
    {
        get => Source.ToString();
        set { if (Enum.TryParse<FieldMappingSource>(value, true, out var s)) Source = s; }
    }
    public string TargetName
    {
        get => Target.ToString();
        set { if (Enum.TryParse<FieldMappingTarget>(value, true, out var t)) Target = t; }
    }
    public string ConversionName
    {
        get => Conversion.ToString();
        set { if (Enum.TryParse<DataConversion>(value, true, out var c)) Conversion = c; }
    }

    public string[] SourceNames => Enum.GetNames<FieldMappingSource>();
    public string[] TargetNames => Enum.GetNames<FieldMappingTarget>();
    public string[] ConversionNames => Enum.GetNames<DataConversion>();

    public FieldMapping ToModel() => new()
    {
        Source = Source,
        Target = Target,
        SecsPath = SecsPath,
        HostFieldName = HostFieldName,
        Conversion = Conversion,
        Description = Description,
    };

    public static FieldMappingViewModel FromModel(FieldMapping m) => new()
    {
        Source = m.Source,
        Target = m.Target,
        SecsPath = m.SecsPath,
        HostFieldName = m.HostFieldName,
        Conversion = m.Conversion,
        Description = m.Description,
    };
}
