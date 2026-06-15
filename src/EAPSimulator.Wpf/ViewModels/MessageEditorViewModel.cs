using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EAPSimulator.Core.Protocols.SecsGem;

namespace EAPSimulator.Wpf.ViewModels;

public partial class MessageEditorViewModel : ObservableObject
{
    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private SecsMessageViewModel? _selectedMessage;
    [ObservableProperty] private object? _selectedTreeItem;
    [ObservableProperty] private string _statusMessage = "就绪";

    private object? _clipboard;
    private string _clipboardLevel = "";

    public ObservableCollection<SecsMessageViewModel> AllMessages { get; } = new();
    public ObservableCollection<MessageGroupViewModel> Groups { get; } = new();

    public event Action<SecsMessageViewModel>? SendMessageRequested;

    public MessageEditorViewModel()
    {
        AllMessages.CollectionChanged += (_, args) =>
        {
            if (args.NewItems != null)
                foreach (SecsMessageViewModel msg in args.NewItems)
                    SubscribeToMessageChanges(msg);
        };
    }

    private void SubscribeToMessageChanges(SecsMessageViewModel msg)
    {
        msg.PropertyChanged += (_, _) => { };
        if (msg.RootItem != null)
            SubscribeToItemChanges(msg.RootItem);
    }

    private void SubscribeToItemChanges(SecsItemViewModel item)
    {
        item.PropertyChanged += (_, _) => { };
        item.Children.CollectionChanged += (_, args) =>
        {
            if (args.NewItems != null)
                foreach (SecsItemViewModel child in args.NewItems)
                    SubscribeToItemChanges(child);
        };
        foreach (var child in item.Children)
            SubscribeToItemChanges(child);
    }

    // ─── Stream group display names ───
    private static readonly Dictionary<byte, string> StreamNames = new()
    {
        [1] = "S1 - 设备状态", [2] = "S2 - 设备控制", [3] = "S3 - 设备常量",
        [5] = "S5 - 报警管理", [6] = "S6 - 采集事件", [7] = "S7 - Process Program",
        [9] = "S9 - 错误消息", [10] = "S10 - 终端消息",
    };

    public static string[] CommonMessageTypes { get; } =
    [
        "S1F1 - Are You There", "S1F3 - Selected Equipment Status",
        "S1F13 - Establish Communication", "S2F41 - Host Command",
        "S5F1 - Alarm Report", "S6F11 - Collection Event Report",
        "S7F3 - Process Program Send", "S10F1 - Terminal Request",
    ];

    // ─── Load / Save ───

    public void LoadFromFile(string path)
    {
        try
        {
            var tf = SecsMessageTemplateFile.LoadFromFile(path);
            AllMessages.Clear();
            foreach (var t in tf.Messages)
                AllMessages.Add(SecsMessageViewModel.FromTemplate(t));
            FilePath = path;
            RebuildGroups();
            StatusMessage = $"已加载 {AllMessages.Count} 条消息模板";
        }
        catch (Exception ex) { StatusMessage = $"加载失败: {ex.Message}"; }
    }

    public void SaveToFile()
    {
        if (string.IsNullOrEmpty(FilePath)) return;
        try
        {
            var tf = new SecsMessageTemplateFile();
            foreach (var m in AllMessages) tf.Messages.Add(m.ToTemplate());
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(tf, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(FilePath, json);
            StatusMessage = $"已保存 {AllMessages.Count} 条消息";
        }
        catch (Exception ex) { StatusMessage = $"保存失败: {ex.Message}"; }
    }

    // ─── Group Rebuilding ───

    public void RebuildGroups()
    {
        var expandedGroups = new HashSet<byte>();
        var expandedPairs = new HashSet<string>();
        foreach (var g in Groups)
        {
            if (g.IsExpanded) expandedGroups.Add(g.Stream);
            foreach (var p in g.Pairs)
                if (p.IsExpanded) expandedPairs.Add($"{g.Stream}:{p.Title}");
        }

        Groups.Clear();
        foreach (var grp in AllMessages.GroupBy(m => m.Stream).OrderBy(g => g.Key))
        {
            var gvm = new MessageGroupViewModel
            {
                Stream = grp.Key,
                Title = StreamNames.GetValueOrDefault(grp.Key, $"S{grp.Key}"),
                IsExpanded = expandedGroups.Contains(grp.Key),
            };

            // Pair messages: F(2k-1) & F(2k)
            foreach (var pairGrp in grp.GroupBy(m => (m.Function + 1) / 2).OrderBy(p => p.Key))
            {
                var msgs = pairGrp.OrderBy(m => m.Function).ToList();
                var odd = msgs.Where(m => m.Function % 2 == 1).ToList();
                var even = msgs.Where(m => m.Function % 2 == 0).ToList();
                var all = msgs;
                var pairMsgs = all;
                var title = pairMsgs.Count == 1
                    ? $"S{msgs[0].Stream}F{msgs[0].Function}"
                    : $"S{msgs[0].Stream}F{msgs.Min(m => m.Function)} & S{msgs[0].Stream}F{msgs.Max(m => m.Function)}";
                var desc = CleanPairDescription(msgs[0].Name);
                if (odd.Count > 0 && even.Count > 0) desc = CleanPairDescription(odd[0].Name);

                var pvm = new MessagePairViewModel
                {
                    Title = title, Description = desc,
                    IsExpanded = expandedPairs.Contains($"{grp.Key}:{title}"),
                };
                foreach (var m in pairMsgs) pvm.Messages.Add(m);
                gvm.Pairs.Add(pvm);
            }
            Groups.Add(gvm);
        }
    }

    private static string CleanPairDescription(string name)
    {
        var n = name.Replace(" Request", "").Replace(" Reply", "").Replace(" Acknowledge", "").TrimEnd();
        var idx = n.IndexOf(" (");
        if (idx > 0) n = n[..idx];
        return n.Length < 3 ? name : n;
    }

    // ─── Commands ───

    [RelayCommand] private void SendSelected()
    {
        if (SelectedMessage != null) SendMessageRequested?.Invoke(SelectedMessage);
    }

    [RelayCommand] private void AddMessage(string? typeDesc)
    {
        byte s = 1, f = 1; string n = "New Message"; bool w = true;
        if (!string.IsNullOrEmpty(typeDesc))
        {
            var parts = typeDesc.Split(" - ", 2);
            var sf = parts[0].Trim(); n = parts.Length > 1 ? parts[1].Trim() : sf;
            if (sf.Length >= 3 && sf[0] == 'S')
            {
                var fi = sf.IndexOf('F');
                if (fi > 1 && fi < sf.Length - 1)
                { byte.TryParse(sf[1..fi], out s); byte.TryParse(sf[(fi + 1)..], out f); }
            }
        }
        var vm = new SecsMessageViewModel { Name = n, Stream = s, Function = f, WBit = w, RootItem = new SecsItemViewModel("L"), HasItems = true };
        AllMessages.Add(vm); RebuildGroups(); SelectedMessage = vm;
        StatusMessage = $"已添加 S{s}F{f} - {n}";
    }

    [RelayCommand] private void DeleteMessage(SecsMessageViewModel? target)
    {
        var msg = target ?? SelectedMessage;
        if (msg == null) return;
        var name = msg.DisplayText;
        AllMessages.Remove(msg);
        foreach (var g in Groups)
            foreach (var p in g.Pairs)
                if (p.Messages.Remove(msg)) { if (p.Messages.Count == 0) g.Pairs.Remove(p); goto done; }
        done:
        SelectedMessage = AllMessages.FirstOrDefault();
        StatusMessage = $"已删除 {name}";
    }

    [RelayCommand] private void DuplicateMessage(SecsMessageViewModel? target)
    {
        var msg = target ?? SelectedMessage;
        if (msg == null) return;
        var clone = msg.Clone();
        var idx = AllMessages.IndexOf(msg);
        if (idx >= 0) AllMessages.Insert(idx + 1, clone); else AllMessages.Add(clone);
        foreach (var g in Groups)
            foreach (var p in g.Pairs)
            {
                var mi = p.Messages.IndexOf(msg);
                if (mi >= 0) { p.Messages.Insert(mi + 1, clone); SelectedMessage = clone; return; }
            }
        RebuildGroups(); SelectedMessage = clone;
    }

    [RelayCommand] private void DuplicatePair(MessagePairViewModel? target)
    {
        if (target == null) return;
        var clones = target.Messages.Select(m => m.Clone()).ToList();
        var last = target.Messages.LastOrDefault();
        var idx = last != null ? AllMessages.IndexOf(last) : -1;
        for (int i = 0; i < clones.Count; i++)
            if (idx >= 0) AllMessages.Insert(idx + 1 + i, clones[i]); else AllMessages.Add(clones[i]);
        foreach (var g in Groups)
        {
            var pi = g.Pairs.IndexOf(target);
            if (pi >= 0)
            {
                var np = new MessagePairViewModel { Title = target.Title, Description = target.Description, IsExpanded = true };
                foreach (var c in clones) np.Messages.Add(c);
                g.Pairs.Insert(pi + 1, np);
                StatusMessage = $"已复制消息组 ({clones.Count} 条)";
                return;
            }
        }
    }

    [RelayCommand] private void DeletePair(MessagePairViewModel? target)
    {
        if (target == null) return;
        var name = target.Title; var count = target.Messages.Count;
        foreach (var m in target.Messages.ToList()) AllMessages.Remove(m);
        RebuildGroups(); SelectedMessage = AllMessages.FirstOrDefault();
        StatusMessage = $"已删除 {name} ({count} 条)";
    }

    [RelayCommand] private void ExpandAll()
    {
        foreach (var g in Groups)
        {
            g.IsExpanded = true;
            foreach (var p in g.Pairs) { p.IsExpanded = true; foreach (var m in p.Messages) m.IsExpanded = true; }
        }
    }

    [RelayCommand] private void CollapseAll()
    {
        foreach (var g in Groups)
        {
            g.IsExpanded = false;
            foreach (var p in g.Pairs) { p.IsExpanded = false; foreach (var m in p.Messages) m.IsExpanded = false; }
        }
    }

    // ─── Clipboard ───

    [RelayCommand] private void CopySelected()
    {
        if (SelectedTreeItem is MessagePairViewModel p) { _clipboard = p; _clipboardLevel = "pair"; StatusMessage = $"已复制: {p.Title}"; }
        else if (SelectedTreeItem is SecsMessageViewModel m) { _clipboard = m; _clipboardLevel = "message"; StatusMessage = $"已复制: {m.DisplayText}"; }
        else if (SelectedTreeItem is SecsItemViewModel i) { _clipboard = i; _clipboardLevel = "item"; StatusMessage = $"已复制: {i.TypeName}"; }
    }

    [RelayCommand] private void PasteClipboard()
    {
        if (_clipboard == null) return;
        if (_clipboardLevel == "pair" && _clipboard is MessagePairViewModel sp)
        {
            MessageGroupViewModel? tg = null;
            foreach (var g in Groups) { foreach (var p in g.Pairs) if (p == SelectedTreeItem || p.Messages.Any(x => x == SelectedTreeItem)) { tg = g; break; } if (tg != null) break; }
            if (tg == null) return;
            var clones = sp.Messages.Select(m => m.Clone()).ToList();
            foreach (var c in clones) AllMessages.Add(c);
            var np = new MessagePairViewModel { Title = sp.Title, Description = sp.Description, IsExpanded = true };
            foreach (var c in clones) np.Messages.Add(c);
            var rp = SelectedTreeItem as MessagePairViewModel;
            if (rp == null && SelectedTreeItem is SecsMessageViewModel sm) rp = tg.Pairs.FirstOrDefault(q => q.Messages.Contains(sm));
            if (rp != null) tg.Pairs.Insert(tg.Pairs.IndexOf(rp) + 1, np); else tg.Pairs.Add(np);
        }
        else if (_clipboardLevel == "message" && _clipboard is SecsMessageViewModel smsg)
        {
            MessagePairViewModel? tp = null;
            foreach (var g in Groups) { foreach (var p in g.Pairs) { if (p == SelectedTreeItem || p.Messages.Any(x => x == SelectedTreeItem)) { tp = p; break; } } if (tp != null) break; }
            if (tp == null) return;
            var clone = smsg.Clone(); AllMessages.Add(clone);
            if (SelectedTreeItem is SecsMessageViewModel sel) { tp.Messages.Insert(tp.Messages.IndexOf(sel) + 1, clone); } else tp.Messages.Add(clone);
        }
        else if (_clipboardLevel == "item" && _clipboard is SecsItemViewModel sit)
        {
            var clone = sit.Clone();
            if (SelectedTreeItem is SecsItemViewModel ti && ti.Parent != null) { clone.Parent = ti.Parent; ti.Parent.Children.Insert(ti.Parent.Children.IndexOf(ti) + 1, clone); }
        }
    }
}

// ─── Supporting ViewModels ───

public partial class MessageGroupViewModel : ObservableObject
{
    [ObservableProperty] private byte _stream;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private bool _isExpanded;
    public ObservableCollection<MessagePairViewModel> Pairs { get; } = new();
    public string DisplayText => $"{Title}  ({Pairs.Count} 组)";
    public MessageGroupViewModel() { Pairs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(DisplayText)); }
}

public partial class MessagePairViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private bool _isExpanded;
    public ObservableCollection<SecsMessageViewModel> Messages { get; } = new();
    public string DisplayText => $"{Title}  —  {Description}";
}

public partial class SecsMessageViewModel : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private byte _stream;
    [ObservableProperty] private byte _function;
    [ObservableProperty] private bool _wBit = true;
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private SecsItemViewModel? _rootItem;
    [ObservableProperty] private bool _hasItems;
    [ObservableProperty] private bool _isExpanded;

    public string DisplayText => $"S{Stream}F{Function}{(WBit ? "W" : "")} - {Name}";

    partial void OnStreamChanged(byte value) => OnPropertyChanged(nameof(DisplayText));
    partial void OnFunctionChanged(byte value) => OnPropertyChanged(nameof(DisplayText));
    partial void OnWBitChanged(bool value) => OnPropertyChanged(nameof(DisplayText));
    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(DisplayText));

    public static SecsMessageViewModel FromTemplate(SecsMessageTemplate template)
    {
        var vm = new SecsMessageViewModel
        {
            Name = template.Name, Stream = template.Stream, Function = template.Function,
            WBit = template.WBit, Description = template.Description,
        };
        if (!string.IsNullOrWhiteSpace(template.ItemXml))
        {
            try { var msg = template.BuildMessage(); if (msg.RootItem != null) { vm.RootItem = SecsItemViewModel.FromSecsItem(msg.RootItem); vm.HasItems = true; } }
            catch { vm.RootItem = new SecsItemViewModel("L"); vm.HasItems = true; }
        }
        else { vm.RootItem = new SecsItemViewModel("L"); vm.HasItems = true; }
        return vm;
    }

    public SecsMessageTemplate ToTemplate()
    {
        var t = new SecsMessageTemplate { Name = Name, Stream = Stream, Function = Function, WBit = WBit, Description = Description };
        if (RootItem != null) t.ItemXml = ToItemXml(RootItem);
        return t;
    }

    private static string ToItemXml(SecsItemViewModel vm)
    {
        if (vm.IsList) return $"<L>{string.Join("", vm.Children.Select(ToItemXml))}</L>";
        return $"<{vm.TypeName}>{vm.ValueText}</{vm.TypeName}>";
    }

    public SecsMessageViewModel Clone() => new()
    {
        Name = Name, Stream = Stream, Function = Function, WBit = WBit,
        Description = Description, RootItem = RootItem?.Clone(), HasItems = HasItems,
    };
}
