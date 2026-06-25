using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EAPSimulator.Core.Protocols.HostProtocol;
using Newtonsoft.Json;

namespace EAPSimulator.UI.ViewModels;

public partial class HostMessageEditorViewModel : ObservableObject
{
    private string _filePath = "host_message_templates.json";

    public ObservableCollection<HostMessageTemplateViewModel> Templates { get; } = [];

    public ObservableCollection<HostMessageTemplateViewModel> FilteredTemplates { get; } = [];

    public ObservableCollection<string> GroupNames { get; } = ["全部"];

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _selectedGroup = "全部";

    public string TemplateCountSummary => string.IsNullOrWhiteSpace(SearchText) && SelectedGroup == "全部"
        ? $"{Templates.Count} 个模板"
        : $"{FilteredTemplates.Count}/{Templates.Count} 个模板";

    [ObservableProperty]
    private HostMessageTemplateViewModel? _selectedTemplate;

    [ObservableProperty]
    private HostFieldViewModel? _selectedField;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private string _jsonPreview = "";

    public IEnumerable<string> TemplateNames => Templates.Select(t => t.Name);

    public string[] DirectionNames { get; } = ["Send", "Receive"];

    /// <summary>Channel names available for templates to bind to. Set by MainViewModel from Config.</summary>
    public ObservableCollection<string> ChannelNames { get; } = [];

    public bool IsRawBody => SelectedTemplate != null
        && string.Equals(SelectedTemplate.BodyFormat, "Raw", StringComparison.OrdinalIgnoreCase);

    /// <summary>Raised when the user clicks "发送测试" (or Ctrl+Enter). Caller wires to protocol.</summary>
    public event Func<HostMessageTemplate, Task>? SendRequested;

    public HostMessageEditorViewModel()
    {
        Templates.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(TemplateNames));
            RebuildGroups();
            RefreshFilteredTemplates();
        };
    }

    public void LoadFromPath(string path)
    {
        _filePath = path;
        if (!File.Exists(path))
        {
            StatusMessage = $"未找到模板文件: {path}";
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            var coll = JsonConvert.DeserializeObject<HostMessageTemplateCollection>(json)
                ?? new HostMessageTemplateCollection();

            Templates.Clear();
            foreach (var t in coll.Templates)
                Templates.Add(HostMessageTemplateViewModel.FromModel(t));
            RebuildGroups();
            RefreshFilteredTemplates();

            if (FilteredTemplates.Count > 0) SelectedTemplate = FilteredTemplates[0];
            else SelectedTemplate = null;
            StatusMessage = $"已加载 {Templates.Count} 个 Host 消息模板";
            RefreshPreview();
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败: {ex.Message}";
        }
    }

    public void LoadDefault() => LoadFromPath(_filePath);

    partial void OnSearchTextChanged(string value) => RefreshFilteredTemplates();

    partial void OnSelectedGroupChanged(string value) => RefreshFilteredTemplates();

    private void RebuildGroups()
    {
        var current = SelectedGroup;
        GroupNames.Clear();
        GroupNames.Add("全部");
        foreach (var g in Templates.Select(t => t.GroupName).Where(g => !string.IsNullOrEmpty(g)).Distinct().OrderBy(g => g))
            GroupNames.Add(g);
        SelectedGroup = GroupNames.Contains(current) ? current : "全部";
        OnPropertyChanged(nameof(TemplateCountSummary));
    }

    private void RefreshFilteredTemplates()
    {
        var search = SearchText?.Trim() ?? "";
        var group = SelectedGroup ?? "全部";
        var filtered = Templates.Where(t =>
            (group == "全部" || t.GroupName == group) &&
            (string.IsNullOrEmpty(search)
             || t.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
             || t.Description.Contains(search, StringComparison.OrdinalIgnoreCase)
             || t.DirectionName.Contains(search, StringComparison.OrdinalIgnoreCase)
             || t.GroupName.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        FilteredTemplates.Clear();
        foreach (var t in filtered)
            FilteredTemplates.Add(t);

        if (SelectedTemplate != null && !FilteredTemplates.Contains(SelectedTemplate))
            SelectedTemplate = FilteredTemplates.FirstOrDefault();
        else if (SelectedTemplate == null && FilteredTemplates.Count > 0)
            SelectedTemplate = FilteredTemplates[0];

        OnPropertyChanged(nameof(TemplateCountSummary));
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = "";
        SelectedGroup = "全部";
    }

    public void RefreshPreview()
    {
        try
        {
            if (SelectedTemplate != null)
            {
                var single = new HostMessageTemplateCollection
                {
                    Templates = [SelectedTemplate.ToModel()],
                };
                JsonPreview = JsonConvert.SerializeObject(single, Formatting.Indented);
            }
            else
            {
                JsonPreview = "(请先选择一个模板)";
            }
        }
        catch { JsonPreview = "预览生成失败"; }
    }

    [RelayCommand]
    private void Save()
    {
        try
        {
            var coll = new HostMessageTemplateCollection
            {
                Templates = Templates.Select(t => t.ToModel()).ToList(),
            };
            var json = JsonConvert.SerializeObject(coll, Formatting.Indented);
            File.WriteAllText(_filePath, json);
            StatusMessage = $"已保存 {Templates.Count} 个模板到 {Path.GetFileName(_filePath)}";
            RefreshPreview();
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private void AddTemplate()
    {
        var t = new HostMessageTemplateViewModel { Name = "NEW_MESSAGE", Direction = HostMessageDirection.Send };
        Templates.Add(t);
        SelectedTemplate = t;
        RefreshPreview();
    }

    [RelayCommand]
    private void DeleteTemplate()
    {
        if (SelectedTemplate == null) return;
        Templates.Remove(SelectedTemplate);
        SelectedTemplate = Templates.FirstOrDefault();
        RefreshPreview();
    }

    [RelayCommand]
    private void CloneTemplate()
    {
        if (SelectedTemplate == null) return;
        var clone = HostMessageTemplateViewModel.FromModel(SelectedTemplate.ToModel());
        clone.Name += " (副本)";
        Templates.Add(clone);
        SelectedTemplate = clone;
        RefreshPreview();
    }

    [RelayCommand]
    private async Task SendTest()
    {
        if (SelectedTemplate == null)
        {
            StatusMessage = "请先选择一个模板";
            return;
        }
        if (SendRequested != null)
        {
            await SendRequested.Invoke(SelectedTemplate.ToModel());
        }
        else
        {
            StatusMessage = "Host 协议未连接，请先连接 Host";
        }
    }

    partial void OnSelectedTemplateChanged(HostMessageTemplateViewModel? value)
    {
        SelectedField = null;
        if (value != null)
            value.PropertyChanged += SelectedTemplate_PropertyChanged;
        OnPropertyChanged(nameof(IsRawBody));
        RefreshPreview();
    }

    private void SelectedTemplate_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HostMessageTemplateViewModel.BodyFormat))
            OnPropertyChanged(nameof(IsRawBody));
        if (e.PropertyName == nameof(HostMessageTemplateViewModel.Name)
            || e.PropertyName == nameof(HostMessageTemplateViewModel.Description)
            || e.PropertyName == nameof(HostMessageTemplateViewModel.DirectionName))
        {
            RebuildGroups();
            RefreshFilteredTemplates();
        }
    }
}

public partial class HostMessageTemplateViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private HostMessageDirection _direction = HostMessageDirection.Send;

    /// <summary>Channel this template targets (e.g. "MES", "RMS"). Empty = any/default.</summary>
    [ObservableProperty]
    private string _channelName = "";

    /// <summary>"Json" (default — build from Fields) or "Raw" (send RawBody verbatim).</summary>
    [ObservableProperty]
    private string _bodyFormat = "Json";

    [ObservableProperty]
    private string _rawBody = "";

    public string[] BodyFormats { get; } = ["Json", "Raw"];

    public ObservableCollection<HostFieldViewModel> Fields { get; } = [];

    public string DirectionName
    {
        get => Direction.ToString();
        set
        {
            if (Enum.TryParse<HostMessageDirection>(value, true, out var d))
                Direction = d;
        }
    }

    public string GroupName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Name)) return "未命名";
            var idx = Name.IndexOf('_');
            return idx > 0 ? Name[..idx] : "未分组";
        }
    }

    partial void OnDirectionChanged(HostMessageDirection value)
        => OnPropertyChanged(nameof(DirectionName));

    partial void OnNameChanged(string value)
        => OnPropertyChanged(nameof(GroupName));

    [RelayCommand]
    private void AddField()
    {
        var f = new HostFieldViewModel { Name = "field", Type = HostFieldType.String };
        Fields.Add(f);
        NotifyPreview();
    }

    [RelayCommand]
    private void DeleteField(HostFieldViewModel? field)
    {
        if (field == null) return;
        RemoveFromParent(field, Fields);
        NotifyPreview();
    }

    [RelayCommand]
    private void MoveFieldUp(HostFieldViewModel? field)
    {
        if (field == null) return;
        var list = FindParentList(field, Fields);
        if (list == null) return;
        var idx = list.IndexOf(field);
        if (idx > 0) list.Move(idx, idx - 1);
        NotifyPreview();
    }

    [RelayCommand]
    private void MoveFieldDown(HostFieldViewModel? field)
    {
        if (field == null) return;
        var list = FindParentList(field, Fields);
        if (list == null) return;
        var idx = list.IndexOf(field);
        if (idx < list.Count - 1) list.Move(idx, idx + 1);
        NotifyPreview();
    }

    [RelayCommand]
    private void AddChildField(HostFieldViewModel? parent)
    {
        if (parent == null) return;
        if (!parent.IsContainer) parent.Type = HostFieldType.Object;
        parent.Children.Add(new HostFieldViewModel { Name = "child", Type = HostFieldType.String });
        NotifyPreview();
    }

    private void NotifyPreview()
    {
        OnPropertyChanged(nameof(Fields)); // cheap way to trigger UI refresh
    }

    private static void RemoveFromParent(HostFieldViewModel target, ObservableCollection<HostFieldViewModel> siblings)
    {
        if (siblings.Remove(target)) return;
        foreach (var s in siblings)
        {
            RemoveFromParent(target, s.Children);
        }
    }

    private static ObservableCollection<HostFieldViewModel>? FindParentList(HostFieldViewModel target, ObservableCollection<HostFieldViewModel> siblings)
    {
        if (siblings.Contains(target)) return siblings;
        foreach (var s in siblings)
        {
            var found = FindParentList(target, s.Children);
            if (found != null) return found;
        }
        return null;
    }

    public HostMessageTemplate ToModel() => new()
    {
        Name = Name,
        Description = Description,
        Direction = Direction,
        ChannelName = ChannelName,
        BodyFormat = BodyFormat,
        RawBody = RawBody,
        Fields = Fields.Select(f => f.ToModel()).ToList(),
    };

    public static HostMessageTemplateViewModel FromModel(HostMessageTemplate m)
    {
        var vm = new HostMessageTemplateViewModel
        {
            Name = m.Name,
            Description = m.Description,
            Direction = m.Direction,
            ChannelName = m.ChannelName,
            BodyFormat = string.IsNullOrEmpty(m.BodyFormat) ? "Json" : m.BodyFormat,
            RawBody = m.RawBody,
        };
        foreach (var f in m.Fields)
            vm.Fields.Add(HostFieldViewModel.FromModel(f));
        return vm;
    }
}

public partial class HostFieldViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private HostFieldType _type = HostFieldType.String;

    [ObservableProperty]
    private string _defaultValue = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private bool _required;

    public ObservableCollection<HostFieldViewModel> Children { get; } = [];

    public bool IsContainer => Type == HostFieldType.Object || Type == HostFieldType.ArrayList;
    public bool IsLeaf => !IsContainer;

    public string TypeName
    {
        get => Type.ToString();
        set
        {
            if (Enum.TryParse<HostFieldType>(value, true, out var t))
                Type = t;
        }
    }

    public static string[] TypeNames { get; } = Enum.GetNames<HostFieldType>();

    public string DisplayText => IsContainer
        ? $"{Type} {Name} [{Children.Count}]"
        : $"{Type} {Name}{(string.IsNullOrEmpty(DefaultValue) ? "" : $" = {DefaultValue}")}";

    partial void OnTypeChanged(HostFieldType value)
    {
        OnPropertyChanged(nameof(IsContainer));
        OnPropertyChanged(nameof(IsLeaf));
        OnPropertyChanged(nameof(TypeName));
        OnPropertyChanged(nameof(DisplayText));
        if (!IsContainer) Children.Clear();
    }

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(DisplayText));
    partial void OnDefaultValueChanged(string value) => OnPropertyChanged(nameof(DisplayText));

    public HostFieldViewModel()
    {
        Children.CollectionChanged += (_, _) => OnPropertyChanged(nameof(DisplayText));
    }

    public HostFieldTemplate ToModel() => new()
    {
        Name = Name,
        Type = Type,
        DefaultValue = DefaultValue,
        Description = Description,
        Required = Required,
        Children = Children.Select(c => c.ToModel()).ToList(),
    };

    public static HostFieldViewModel FromModel(HostFieldTemplate t)
    {
        var vm = new HostFieldViewModel
        {
            Name = t.Name,
            Type = t.Type,
            DefaultValue = t.DefaultValue,
            Description = t.Description,
            Required = t.Required,
        };
        foreach (var c in t.Children)
            vm.Children.Add(HostFieldViewModel.FromModel(c));
        return vm;
    }
}
