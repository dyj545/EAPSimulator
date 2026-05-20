using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EAPSimulator.Core.Protocols.SecsGem;
using EAPSimulator.Core.Protocols.SecsGem.SecsII;

namespace EAPSimulator.UI.ViewModels;

/// <summary>
/// ViewModel for the SECS message editor panel.
/// Manages template file loading, tree view with stream grouping, and send operations.
/// </summary>
public partial class MessageEditorViewModel : ObservableObject
{
    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private SecsMessageViewModel? _selectedMessage;

    [ObservableProperty]
    private object? _selectedTreeItem;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _autoSaveEnabled = true;

    // Clipboard for Ctrl+C / Ctrl+V
    // null = nothing copied, SecsMessageViewModel = single message copied, MessagePairViewModel = pair copied
    private object? _clipboard;
    private string _clipboardLevel = string.Empty; // "message" or "pair"

    /// <summary>Flat list of all messages (source of truth for add/delete/save).</summary>
    public ObservableCollection<SecsMessageViewModel> AllMessages { get; } = new();

    /// <summary>Grouped view by Stream number for tree display.</summary>
    public ObservableCollection<MessageGroupViewModel> Groups { get; } = new();

    public event Action<SecsMessageViewModel>? SendMessageRequested;
    public event Action? ExpandSelectedRequested;

    // Auto-save timer
    private System.Threading.Timer? _autoSaveTimer;
    private readonly object _autoSaveLock = new();
    private bool _pendingSave;

    public MessageEditorViewModel()
    {
        // Watch for changes in AllMessages to trigger auto-save
        AllMessages.CollectionChanged += (_, args) =>
        {
            ScheduleAutoSave();

            // Subscribe to property changes on new items
            if (args.NewItems != null)
            {
                foreach (SecsMessageViewModel msg in args.NewItems)
                {
                    SubscribeToMessageChanges(msg);
                }
            }
        };
    }

    private void SubscribeToMessageChanges(SecsMessageViewModel msg)
    {
        msg.PropertyChanged += (_, _) => ScheduleAutoSave();
        if (msg.RootItem != null)
            SubscribeToItemChanges(msg.RootItem);
    }

    private void SubscribeToItemChanges(SecsItemViewModel item)
    {
        item.PropertyChanged += (_, _) => ScheduleAutoSave();
        item.Children.CollectionChanged += (_, args) =>
        {
            ScheduleAutoSave();
            if (args.NewItems != null)
            {
                foreach (SecsItemViewModel child in args.NewItems)
                    SubscribeToItemChanges(child);
            }
        };
        foreach (var child in item.Children)
            SubscribeToItemChanges(child);
    }

    private void ScheduleAutoSave()
    {
        if (!AutoSaveEnabled || string.IsNullOrEmpty(FilePath)) return;

        lock (_autoSaveLock)
        {
            _pendingSave = true;
            _autoSaveTimer?.Dispose();
            _autoSaveTimer = new System.Threading.Timer(_ =>
            {
                lock (_autoSaveLock)
                {
                    if (_pendingSave)
                    {
                        _pendingSave = false;
                        SaveToFile(FilePath);
                    }
                }
            }, null, 500, System.Threading.Timeout.Infinite);
        }
    }

    private void SaveToFile(string path)
    {
        try
        {
            var templateFile = new SecsMessageTemplateFile();
            foreach (var msgVm in AllMessages)
            {
                templateFile.Messages.Add(msgVm.ToTemplate());
            }
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(templateFile, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(path, json);
        }
        catch
        {
            // Silently fail for auto-save
        }
    }

    [RelayCommand]
    private void ExpandSelected() => ExpandSelectedRequested?.Invoke();

    [RelayCommand]
    private void SendSelected()
    {
        if (SelectedMessage != null)
            SendMessageRequested?.Invoke(SelectedMessage);
    }

    /// <summary>Stream → group title mapping for display.</summary>
    private static readonly Dictionary<byte, string> StreamNames = new()
    {
        [1] = "S1 - 设备状态",
        [2] = "S2 - 设备控制",
        [3] = "S3 - 设备常量",
        [5] = "S5 - 报警管理",
        [6] = "S6 - 采集事件",
        [7] = "S7 - Process Program",
        [9] = "S9 - 错误消息",
        [10] = "S10 - 终端消息",
    };

    /// <summary>Common SECS message types for quick creation.</summary>
    public static string[] CommonMessageTypes { get; } =
    [
        "S1F1 - Are You There",
        "S1F3 - Selected Equipment Status",
        "S1F5 - List Alarmed Variables",
        "S1F11 - SV Namelist Request",
        "S1F13 - Establish Communication",
        "S2F13 - Process Program Send",
        "S2F17 - Date and Time Request",
        "S2F25 - Loopback Diagnostic",
        "S2F33 - Define Report",
        "S2F35 - Link Event Report",
        "S2F37 - Enable/Disable Event Report",
        "S2F41 - Host Command",
        "S2F49 - Enhanced Host Command",
        "S5F1 - Alarm Report",
        "S5F5 - List Alarms",
        "S6F11 - Collection Event Report",
        "S7F1 - Process Program Load Inquire",
        "S7F3 - Process Program Send",
        "S10F1 - Terminal Request",
    ];


    public void LoadFromFile(string path)
    {
        try
        {
            var templateFile = SecsMessageTemplateFile.LoadFromFile(path);
            AllMessages.Clear();
            foreach (var template in templateFile.Messages)
            {
                AllMessages.Add(SecsMessageViewModel.FromTemplate(template));
            }
            FilePath = path;
            RebuildGroups();
            StatusMessage = $"已加载 {AllMessages.Count} 条消息模板";
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败: {ex.Message}";
        }
    }

    /// <summary>Rebuild the grouped tree from AllMessages with pair sub-grouping.</summary>
    private void RebuildGroups()
    {
        // Save expanded states before rebuilding
        var expandedGroups = new HashSet<byte>();
        var expandedPairs = new HashSet<string>();
        var expandedMessages = new HashSet<string>();

        foreach (var g in Groups)
        {
            if (g.IsExpanded) expandedGroups.Add(g.Stream);
            foreach (var p in g.Pairs)
            {
                if (p.IsExpanded) expandedPairs.Add($"{g.Stream}:{p.Title}");
                foreach (var m in p.Messages)
                {
                    if (m.IsExpanded) expandedMessages.Add($"{m.Stream}F{m.Function}:{m.Name}");
                }
            }
        }

        Groups.Clear();
        var grouped = AllMessages
            .GroupBy(m => m.Stream)
            .OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            var groupTitle = StreamNames.TryGetValue(group.Key, out var name)
                ? name
                : $"S{group.Key}";
            var gvm = new MessageGroupViewModel
            {
                Stream = group.Key,
                Title = groupTitle,
                IsExpanded = expandedGroups.Contains(group.Key),
            };

            // Sub-group by paired functions: F(2k-1) & F(2k) → pair k
            var funcPairGroups = group
                .GroupBy(m => (m.Function + 1) / 2)
                .OrderBy(pg => pg.Key);

            foreach (var funcPairGroup in funcPairGroups)
            {
                var allInPair = funcPairGroup.ToList();

                // Split by variant from odd-function (request) messages only
                var oddMessages = allInPair.Where(m => m.Function % 2 == 1).ToList();
                var evenMessages = allInPair.Where(m => m.Function % 2 == 0).ToList();

                var variantGroups = oddMessages
                    .GroupBy(m => ExtractVariant(m.Name))
                    .ToList();

                // Also check reply-side variants (e.g. S1F14 ACK/NACK)
                var evenVariantGroups = evenMessages
                    .GroupBy(m => ExtractVariant(m.Name))
                    .ToList();

                // Determine if we should split by request variants or reply variants
                var hasOddVariants = variantGroups.Count > 1
                                     && variantGroups.Any(vg => !string.IsNullOrEmpty(vg.Key));
                var hasEvenVariants = evenVariantGroups.Count > 1
                                      && evenVariantGroups.Any(vg => !string.IsNullOrEmpty(vg.Key));

                if (hasOddVariants)
                {
                    // One pair group per request variant
                    foreach (var vg in variantGroups
                        .Where(vg => !string.IsNullOrEmpty(vg.Key))
                        .OrderBy(vg => vg.Key))
                    {
                        var pairMsgs = new List<SecsMessageViewModel>();
                        pairMsgs.AddRange(vg);

                        // Match even messages by the same variant, or include unmatched ones
                        var matchedEven = evenMessages
                            .Where(em => ExtractVariant(em.Name) == vg.Key)
                            .ToList();
                        if (matchedEven.Count > 0)
                            pairMsgs.AddRange(matchedEven);
                        else
                            pairMsgs.AddRange(evenMessages.Where(em => string.IsNullOrEmpty(ExtractVariant(em.Name))));

                        pairMsgs = pairMsgs.OrderBy(m => m.Function).ToList();

                        var pairTitle = DerivePairTitle(pairMsgs[0], pairMsgs);

                        var pvm = new MessagePairViewModel
                        {
                            Title = pairTitle,
                            Description = vg.Key,
                            IsExpanded = expandedPairs.Contains($"{group.Key}:{pairTitle}"),
                        };
                        foreach (var msg in pairMsgs)
                            pvm.Messages.Add(msg);
                        gvm.Pairs.Add(pvm);
                    }
                }
                else if (hasEvenVariants)
                {
                    // One pair group per reply variant
                    foreach (var vg in evenVariantGroups
                        .Where(vg => !string.IsNullOrEmpty(vg.Key))
                        .OrderBy(vg => vg.Key))
                    {
                        var pairMsgs = new List<SecsMessageViewModel>();

                        // Match odd messages by the same variant, or include unmatched ones
                        var matchedOdd = oddMessages
                            .Where(om => ExtractVariant(om.Name) == vg.Key)
                            .ToList();
                        if (matchedOdd.Count > 0)
                            pairMsgs.AddRange(matchedOdd);
                        else
                            pairMsgs.AddRange(oddMessages.Where(om => string.IsNullOrEmpty(ExtractVariant(om.Name))));

                        pairMsgs.AddRange(vg);
                        pairMsgs = pairMsgs.OrderBy(m => m.Function).ToList();

                        var pairTitle = DerivePairTitle(pairMsgs[0], pairMsgs);

                        var pvm = new MessagePairViewModel
                        {
                            Title = pairTitle,
                            Description = vg.Key,
                            IsExpanded = expandedPairs.Contains($"{group.Key}:{pairTitle}"),
                        };
                        foreach (var msg in pairMsgs)
                            pvm.Messages.Add(msg);
                        gvm.Pairs.Add(pvm);
                    }
                }
                else
                {
                    // No variant splitting — single pair group
                    var pairMsgs = allInPair.OrderBy(m => m.Function).ToList();
                    var pairTitle = DerivePairTitle(pairMsgs[0], pairMsgs);
                    var pairDesc = DerivePairDescription(pairMsgs);

                    var pvm = new MessagePairViewModel
                    {
                        Title = pairTitle,
                        Description = pairDesc,
                        IsExpanded = expandedPairs.Contains($"{group.Key}:{pairTitle}"),
                    };
                    foreach (var msg in pairMsgs)
                        pvm.Messages.Add(msg);
                    gvm.Pairs.Add(pvm);
                }
            }

            Groups.Add(gvm);
        }
    }

    /// <summary>Extract the variant text from a message name, e.g. "(Enable)" → "Enable".</summary>
    private static string ExtractVariant(string name)
    {
        var match = System.Text.RegularExpressions.Regex.Match(name, @"\(([^)]+)\)\s*$");
        return match.Success ? match.Groups[1].Value : "";
    }

    /// <summary>Derive a clean pair title like "S1F1 &amp; S1F2" from the messages.</summary>
    private static string DerivePairTitle(SecsMessageViewModel first, List<SecsMessageViewModel> msgs)
    {
        if (msgs.Count == 1)
            return $"S{first.Stream}F{first.Function}";

        var minF = msgs.Min(m => m.Function);
        var maxF = msgs.Max(m => m.Function);
        return $"S{first.Stream}F{minF} & S{first.Stream}F{maxF}";
    }

    /// <summary>Derive a clean description for the pair by stripping Request/Reply/Acknowledge suffixes.</summary>
    private static string DerivePairDescription(List<SecsMessageViewModel> msgs)
    {
        var name = msgs[0].Name;
        // Strip common suffixes to get the base description
        var clean = name
            .Replace(" Acknowledge", "")
            .Replace(" Ack", "")
            .Replace(" Request", "")
            .Replace(" Reply", "")
            .Replace(" Report", " 报告")
            .TrimEnd();

        // Remove trailing parenthetical variants like (ACK), (NACK), (SET), (CLEAR)
        var idx = clean.IndexOf(" (");
        if (idx > 0)
            clean = clean[..idx];

        // If we stripped too much, fall back to the original name
        if (clean.Length < 3)
            clean = name;

        return clean;
    }

    [RelayCommand]
    private void ExpandAll()
    {
        foreach (var g in Groups)
        {
            g.IsExpanded = true;
            foreach (var p in g.Pairs)
            {
                p.IsExpanded = true;
                foreach (var m in p.Messages)
                    m.IsExpanded = true;
            }
        }
    }

    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var g in Groups)
        {
            g.IsExpanded = false;
            foreach (var p in g.Pairs)
            {
                p.IsExpanded = false;
                foreach (var m in p.Messages)
                    m.IsExpanded = false;
            }
        }
    }
    [RelayCommand]
    private void AddMessage(string? typeDesc)
    {
        byte stream = 1, function = 1;
        string name = "New Message";
        bool wBit = true;

        if (!string.IsNullOrEmpty(typeDesc))
        {
            var parts = typeDesc.Split(" - ", 2);
            var sf = parts[0].Trim();
            name = parts.Length > 1 ? parts[1].Trim() : sf;

            if (sf.Length >= 3 && sf[0] == 'S')
            {
                var fIdx = sf.IndexOf('F');
                if (fIdx > 1 && fIdx < sf.Length - 1)
                {
                    byte.TryParse(sf[1..fIdx], out stream);
                    byte.TryParse(sf[(fIdx + 1)..], out function);
                }
            }
        }

        var vm = new SecsMessageViewModel
        {
            Name = name,
            Stream = stream,
            Function = function,
            WBit = wBit,
            RootItem = new SecsItemViewModel("L"),
            HasItems = true,
        };
        AllMessages.Add(vm);
        RebuildGroups();
        SelectedMessage = vm;
        StatusMessage = $"已添加 S{stream}F{function} - {name}";
    }

    [RelayCommand]
    private void DeleteMessage(SecsMessageViewModel? target)
    {
        var msg = target ?? SelectedMessage;
        if (msg == null) return;
        var name = msg.DisplayText;

        // Remove from AllMessages
        AllMessages.Remove(msg);

        // Remove from tree directly
        foreach (var g in Groups)
        {
            foreach (var p in g.Pairs)
            {
                if (p.Messages.Remove(msg))
                {
                    // If pair is empty, remove it from the group
                    if (p.Messages.Count == 0)
                        g.Pairs.Remove(p);

                    SelectedMessage = AllMessages.FirstOrDefault();
                    StatusMessage = $"已删除 {name}";
                    return;
                }
            }
        }

        // Fallback
        RebuildGroups();
        SelectedMessage = AllMessages.FirstOrDefault();
        StatusMessage = $"已删除 {name}";
    }

    [RelayCommand]
    private void DuplicateMessage(SecsMessageViewModel? target)
    {
        var msg = target ?? SelectedMessage;
        if (msg == null) return;
        var clone = msg.Clone();

        // Add to flat list
        var idx = AllMessages.IndexOf(msg);
        if (idx >= 0)
            AllMessages.Insert(idx + 1, clone);
        else
            AllMessages.Add(clone);

        // Also insert into the same Pair in the tree (no RebuildGroups needed)
        foreach (var g in Groups)
        {
            foreach (var p in g.Pairs)
            {
                var msgIdx = p.Messages.IndexOf(msg);
                if (msgIdx >= 0)
                {
                    p.Messages.Insert(msgIdx + 1, clone);
                    SelectedMessage = msg;
                    StatusMessage = $"已复制 {clone.DisplayText}";
                    return;
                }
            }
        }

        // Fallback if not found in any pair
        RebuildGroups();
        SelectedMessage = msg;
        StatusMessage = $"已复制 {clone.DisplayText}";
    }

    [RelayCommand]
    private void DuplicatePair(MessagePairViewModel? target)
    {
        if (target == null) return;

        // Clone all messages (no name changes, no RebuildGroups)
        var clones = target.Messages.Select(m => m.Clone()).ToList();

        // Add to flat list after the original messages
        var lastOriginal = target.Messages.LastOrDefault();
        var insertIdx = lastOriginal != null ? AllMessages.IndexOf(lastOriginal) : -1;
        for (int i = 0; i < clones.Count; i++)
        {
            if (insertIdx >= 0)
                AllMessages.Insert(insertIdx + 1 + i, clones[i]);
            else
                AllMessages.Add(clones[i]);
        }

        // Find the parent group and insert a new pair after the original
        foreach (var g in Groups)
        {
            var pairIdx = g.Pairs.IndexOf(target);
            if (pairIdx >= 0)
            {
                var newPair = new MessagePairViewModel
                {
                    Title = target.Title,
                    Description = target.Description,
                    IsExpanded = true,
                };
                foreach (var clone in clones)
                    newPair.Messages.Add(clone);

                g.Pairs.Insert(pairIdx + 1, newPair);
                StatusMessage = $"已复制消息组 ({clones.Count} 条消息)";
                return;
            }
        }
    }

    [RelayCommand]
    private void DeletePair(MessagePairViewModel? target)
    {
        if (target == null) return;
        var name = target.Title;
        var count = target.Messages.Count;
        foreach (var msg in target.Messages.ToList())
        {
            AllMessages.Remove(msg);
        }
        RebuildGroups();
        SelectedMessage = AllMessages.FirstOrDefault();
        StatusMessage = $"已删除 {name} ({count} 条消息)";
    }

    [RelayCommand]
    private void CopySelected()
    {
        var item = SelectedTreeItem;
        if (item is MessagePairViewModel pair)
        {
            _clipboard = pair;
            _clipboardLevel = "pair";
            StatusMessage = $"已复制消息组: {pair.Title}";
        }
        else if (item is SecsMessageViewModel msg)
        {
            _clipboard = msg;
            _clipboardLevel = "message";
            StatusMessage = $"已复制消息: {msg.DisplayText}";
        }
        else if (item is SecsItemViewModel secsItem)
        {
            _clipboard = secsItem;
            _clipboardLevel = "item";
            StatusMessage = $"已复制节点: {secsItem.TypeName} {secsItem.ValueText}";
        }
    }

    [RelayCommand]
    private void PasteClipboard()
    {
        if (_clipboard == null) return;

        if (_clipboardLevel == "pair" && _clipboard is MessagePairViewModel sourcePair)
        {
            // Find the group of the current selected item
            MessageGroupViewModel? targetGroup = null;
            foreach (var g in Groups)
            {
                foreach (var p in g.Pairs)
                {
                    if (p == SelectedTreeItem || p.Messages.Any(m => m == SelectedTreeItem))
                    {
                        targetGroup = g;
                        break;
                    }
                }
                if (targetGroup != null) break;
            }

            if (targetGroup == null) return;

            // Clone the pair's messages
            var clones = sourcePair.Messages.Select(m => m.Clone()).ToList();
            foreach (var clone in clones)
                AllMessages.Add(clone);

            // Create new pair and insert into the same group
            var newPair = new MessagePairViewModel
            {
                Title = sourcePair.Title,
                Description = sourcePair.Description,
                IsExpanded = true,
            };
            foreach (var clone in clones)
                newPair.Messages.Add(clone);

            // Insert after the current selected pair, or at the end
            var refPair = SelectedTreeItem as MessagePairViewModel;
            if (refPair == null && SelectedTreeItem is SecsMessageViewModel selMsg)
            {
                // Find the pair containing the selected message
                refPair = targetGroup.Pairs.FirstOrDefault(p => p.Messages.Contains(selMsg));
            }

            if (refPair != null)
            {
                var idx = targetGroup.Pairs.IndexOf(refPair);
                targetGroup.Pairs.Insert(idx + 1, newPair);
            }
            else
            {
                targetGroup.Pairs.Add(newPair);
            }

            StatusMessage = $"已粘贴消息组: {newPair.Title}";
        }
        else if (_clipboardLevel == "message" && _clipboard is SecsMessageViewModel sourceMsg)
        {
            // Find the pair containing the current selected item
            MessagePairViewModel? targetPair = null;
            foreach (var g in Groups)
            {
                foreach (var p in g.Pairs)
                {
                    if (p == SelectedTreeItem)
                    {
                        targetPair = p;
                        break;
                    }
                    if (p.Messages.Any(m => m == SelectedTreeItem))
                    {
                        targetPair = p;
                        break;
                    }
                }
                if (targetPair != null) break;
            }

            if (targetPair == null) return;

            var clone = sourceMsg.Clone();
            AllMessages.Add(clone);

            // Insert after the selected message, or at the end of the pair
            if (SelectedTreeItem is SecsMessageViewModel selMsg)
            {
                var idx = targetPair.Messages.IndexOf(selMsg);
                targetPair.Messages.Insert(idx + 1, clone);
            }
            else
            {
                targetPair.Messages.Add(clone);
            }

            StatusMessage = $"已粘贴消息: {clone.DisplayText}";
        }
        else if (_clipboardLevel == "item" && _clipboard is SecsItemViewModel sourceItem)
        {
            var clone = sourceItem.Clone();
            if (SelectedTreeItem is SecsItemViewModel targetItem && targetItem.Parent != null)
            {
                clone.Parent = targetItem.Parent;
                var idx = targetItem.Parent.Children.IndexOf(targetItem);
                targetItem.Parent.Children.Insert(idx + 1, clone);
                StatusMessage = $"已粘贴节点: {clone.TypeName} {clone.ValueText}";
            }
        }
    }

    [RelayCommand]
    private void SaveFile()
    {
        if (string.IsNullOrEmpty(FilePath))
            return;

        try
        {
            var templateFile = new SecsMessageTemplateFile();
            foreach (var msgVm in AllMessages)
            {
                templateFile.Messages.Add(msgVm.ToTemplate());
            }
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(templateFile, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(FilePath, json);
            StatusMessage = $"已保存 {AllMessages.Count} 条消息到 {Path.GetFileName(FilePath)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存失败: {ex.Message}";
        }
    }
}

/// <summary>
/// A group of SECS messages under the same Stream number.
/// e.g. "S1 - 设备状态" contains S1F1, S1F2, S1F13, S1F14, etc.
/// </summary>
public partial class MessageGroupViewModel : ObservableObject
{
    [ObservableProperty]
    private byte _stream;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>Sub-groups: paired messages within this stream.</summary>
    public ObservableCollection<MessagePairViewModel> Pairs { get; } = new();

    public string DisplayText => $"{Title}  ({Pairs.Count} 组)";

    public MessageGroupViewModel()
    {
        Pairs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(DisplayText));
    }
}

/// <summary>
/// A pair of request/reply SECS messages (e.g. S1F1 & S1F2).
/// </summary>
public partial class MessagePairViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private bool _isExpanded;

    public ObservableCollection<SecsMessageViewModel> Messages { get; } = new();

    public string DisplayText => $"{Title}  —  {Description}";
}

/// <summary>
/// Represents a single SECS message in the tree editor.
/// </summary>
public partial class SecsMessageViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private byte _stream;

    [ObservableProperty]
    private byte _function;

    [ObservableProperty]
    private bool _wBit = true;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private SecsItemViewModel? _rootItem;

    [ObservableProperty]
    private bool _hasItems;

    [ObservableProperty]
    private bool _isExpanded = false;

    public string DisplayText => $"S{Stream}F{Function}{(WBit ? "W" : "")} - {Name}";

    partial void OnStreamChanged(byte value) => OnPropertyChanged(nameof(DisplayText));
    partial void OnFunctionChanged(byte value) => OnPropertyChanged(nameof(DisplayText));
    partial void OnWBitChanged(bool value) => OnPropertyChanged(nameof(DisplayText));
    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(DisplayText));

    public static SecsMessageViewModel FromTemplate(SecsMessageTemplate template)
    {
        var vm = new SecsMessageViewModel
        {
            Name = template.Name,
            Stream = template.Stream,
            Function = template.Function,
            WBit = template.WBit,
            Description = template.Description,
        };

        if (!string.IsNullOrWhiteSpace(template.ItemXml))
        {
            try
            {
                var msg = template.BuildMessage();
                if (msg.RootItem != null)
                {
                    vm.RootItem = SecsItemViewModel.FromSecsItem(msg.RootItem);
                    vm.HasItems = true;
                }
            }
            catch
            {
                vm.RootItem = new SecsItemViewModel("L");
                vm.HasItems = true;
            }
        }
        else
        {
            vm.RootItem = new SecsItemViewModel("L");
            vm.HasItems = true;
        }

        // Apply field metadata if available
        if (template.FieldMetadata != null && vm.RootItem != null)
        {
            ApplyFieldMetadata(vm.RootItem, "", template.FieldMetadata);
        }

        return vm;
    }

    private static void ApplyFieldMetadata(SecsItemViewModel item, string path, Dictionary<string, FieldMetadata> metadata)
    {
        if (metadata.TryGetValue(path, out var meta))
        {
            item.Alias = meta.Alias ?? string.Empty;
            item.Description = meta.Description ?? string.Empty;
            item.Format = meta.Format ?? string.Empty;
            item.Nlb = meta.Nlb ?? string.Empty;
            item.DefaultValue = meta.DefaultValue ?? string.Empty;

            if (meta.ValueMappings != null)
            {
                item.ValueMappings.Clear();
                foreach (var (key, value) in meta.ValueMappings)
                {
                    item.ValueMappings.Add(new ValueMappingEntry { Value = key, DisplayText = value });
                }
            }
        }

        // Recurse into children
        for (int i = 0; i < item.Children.Count; i++)
        {
            var childPath = string.IsNullOrEmpty(path) ? i.ToString() : $"{path}/{i}";
            ApplyFieldMetadata(item.Children[i], childPath, metadata);
        }
    }

    public SecsMessage ToSecsMessage()
    {
        SecsItem? rootItem = RootItem?.ToSecsItem();
        return new SecsMessage(Stream, Function, WBit, rootItem);
    }

    public SecsMessageTemplate ToTemplate()
    {
        var template = new SecsMessageTemplate
        {
            Name = Name,
            Stream = Stream,
            Function = Function,
            WBit = WBit,
            Description = Description,
        };

        if (RootItem != null)
        {
            template.ItemXml = ToItemXml(RootItem);

            // Collect field metadata
            var metadata = new Dictionary<string, FieldMetadata>();
            CollectFieldMetadata(RootItem, "", metadata);
            if (metadata.Count > 0)
                template.FieldMetadata = metadata;
        }

        return template;
    }

    private static void CollectFieldMetadata(SecsItemViewModel item, string path, Dictionary<string, FieldMetadata> metadata)
    {
        // Only save metadata if the item has at least one non-empty field
        if (!string.IsNullOrEmpty(item.Alias) || !string.IsNullOrEmpty(item.Description) ||
            !string.IsNullOrEmpty(item.Format) || !string.IsNullOrEmpty(item.Nlb) ||
            !string.IsNullOrEmpty(item.DefaultValue) || item.ValueMappings.Count > 0)
        {
            var meta = new FieldMetadata
            {
                Alias = item.Alias,
                Description = item.Description,
                Format = item.Format,
                Nlb = item.Nlb,
                DefaultValue = item.DefaultValue,
            };
            if (item.ValueMappings.Count > 0)
            {
                meta.ValueMappings = item.ValueMappings.ToDictionary(m => m.Value, m => m.DisplayText);
            }
            metadata[path] = meta;
        }

        // Recurse into children
        for (int i = 0; i < item.Children.Count; i++)
        {
            var childPath = string.IsNullOrEmpty(path) ? i.ToString() : $"{path}/{i}";
            CollectFieldMetadata(item.Children[i], childPath, metadata);
        }
    }

    private static string ToItemXml(SecsItemViewModel vm)
    {
        if (vm.IsList)
        {
            var children = string.Join("", vm.Children.Select(ToItemXml));
            return $"<L>{children}</L>";
        }
        return $"<{vm.TypeName}>{vm.ValueText}</{vm.TypeName}>";
    }

    public SecsMessageViewModel Clone()
    {
        return new SecsMessageViewModel
        {
            Name = Name,
            Stream = Stream,
            Function = Function,
            WBit = WBit,
            Description = Description,
            RootItem = RootItem?.Clone(),
            HasItems = HasItems,
        };
    }
}
