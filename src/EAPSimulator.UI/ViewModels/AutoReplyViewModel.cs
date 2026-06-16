using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EAPSimulator.Core.Protocols.SecsGem;
using EAPSimulator.Core.Protocols.SecsGem.AutoReply;
using EAPSimulator.Core.Protocols.SecsGem.SecsII;
using Newtonsoft.Json;

namespace EAPSimulator.UI.ViewModels;

public partial class AutoReplyViewModel : ObservableObject
{
    /// <summary>Static reference for child VMs to access template data.</summary>
    internal static AutoReplyViewModel? Instance { get; private set; }

    private static string ResolveDefaultConfigPath()
    {
        var basePath = AppDomain.CurrentDomain.BaseDirectory;
        var projectRoot = Path.GetFullPath(Path.Combine(basePath, "..", "..", "..", "..", "..", "auto_reply_rules.json"));
        return File.Exists(projectRoot) ? projectRoot : AutoReplyConfig.GetDefaultPath();
    }

    private string _configPath = ResolveDefaultConfigPath();

    public ObservableCollection<QuickReplyRuleViewModel> QuickReplies { get; } = [];
    public ObservableCollection<ScenarioViewModel> Scenarios { get; } = [];

    /// <summary>
    /// Available template names from the message template file.
    /// Set by MainViewModel after loading templates.
    /// </summary>
    public ObservableCollection<string> TemplateNames { get; } = [];

    /// <summary>
    /// Full template list for resolving names to templates.
    /// </summary>
    private List<SecsMessageTemplate> _allTemplates = [];

    /// <summary>
    /// All message ViewModels from MessageEditor — shared reference for inline editing.
    /// </summary>
    private ObservableCollection<SecsMessageViewModel>? _allMessages;

    /// <summary>
    /// Currently displayed message body for the selected template.
    /// Shared reference with MessageEditorViewModel.AllMessages — edits sync automatically.
    /// </summary>
    [ObservableProperty]
    private SecsMessageViewModel? _displayedMessage;

    [ObservableProperty]
    private QuickReplyRuleViewModel? _selectedQuickReply;

    [ObservableProperty]
    private ScenarioViewModel? _selectedScenario;

    [ObservableProperty]
    private ScenarioStepViewModel? _selectedStep;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _showQuickReplies = true;

    [ObservableProperty]
    private bool _showScenarios;

    [ObservableProperty]
    private string _runStatus = "";

    /// <summary>
    /// Tracks which side of the link this simulator runs as. Set by MainViewModel from
    /// <see cref="ConfigViewModel.ConnectionMode"/>; used to default new scenarios' Role
    /// and to filter AutoStart scenarios so an EAP-side script doesn't fire on the
    /// equipment side and vice-versa.
    /// </summary>
    [ObservableProperty]
    private EAPSimulator.Core.Protocols.ProtocolRole _currentRole = EAPSimulator.Core.Protocols.ProtocolRole.Equipment;

    /// <summary>The currently active scenario engine, set by ApplyToRouter when protocol starts.</summary>
    private ScenarioEngine? _activeEngine;

    [RelayCommand]
    private void RunScenario()
    {
        if (SelectedScenario == null) { RunStatus = "未选择场景"; return; }
        if (_activeEngine == null) { RunStatus = "请先连接协议(Run 需要发送通道)"; return; }
        var def = SelectedScenario.ToModel();
        _activeEngine.Start(def);
        RunStatus = $"运行中: {def.Name}";
    }

    [RelayCommand]
    private void StopScenario()
    {
        _activeEngine?.Stop();
        RunStatus = "已请求停止";
    }

    // ─── Quick Reply Commands ───

    [RelayCommand]
    private void AddQuickReply()
    {
        var rule = new QuickReplyRuleViewModel
        {
            TriggerStream = 1,
            TriggerFunction = 1,
            ReplyTemplateName = TemplateNames.FirstOrDefault() ?? "",
            Description = "New Rule",
            Enabled = true,
        };
        rule.UpdateDisplayText();
        QuickReplies.Add(rule);
        SelectedQuickReply = rule;
    }

    [RelayCommand]
    private void DeleteQuickReply()
    {
        if (SelectedQuickReply == null) return;
        var idx = QuickReplies.IndexOf(SelectedQuickReply);
        QuickReplies.Remove(SelectedQuickReply);
        SelectedQuickReply = QuickReplies.Count > 0 ? QuickReplies[Math.Min(idx, QuickReplies.Count - 1)] : null;
    }

    // ─── Scenario Commands ───

    [RelayCommand]
    private void AddScenario()
    {
        // Default Role to whatever side this simulator currently runs as — saves the user
        // from picking it manually each time and prevents the "wrong-side" mistake by default.
        var defaultRole = CurrentRole == EAPSimulator.Core.Protocols.ProtocolRole.Host
            ? ScenarioRole.Host
            : ScenarioRole.Equipment;
        var scenario = new ScenarioViewModel
        {
            Name = "New Scenario",
            Description = "",
            Enabled = true,
            Role = defaultRole,
        };
        Scenarios.Add(scenario);
        SelectedScenario = scenario;
    }

    [RelayCommand]
    private void DeleteScenario()
    {
        if (SelectedScenario == null) return;
        var idx = Scenarios.IndexOf(SelectedScenario);
        Scenarios.Remove(SelectedScenario);
        SelectedScenario = Scenarios.Count > 0 ? Scenarios[Math.Min(idx, Scenarios.Count - 1)] : null;
    }

    [RelayCommand]
    private void AddStep(string? kindName)
    {
        if (SelectedScenario == null) return;
        var kind = ScenarioStepKind.Receive;
        if (!string.IsNullOrEmpty(kindName) && Enum.TryParse<ScenarioStepKind>(kindName, true, out var k))
            kind = k;
        var step = new ScenarioStepViewModel { Kind = kind };
        // Sensible per-kind defaults
        switch (kind)
        {
            case ScenarioStepKind.Send:
                step.TemplateName = TemplateNames.FirstOrDefault() ?? "";
                break;
            case ScenarioStepKind.Receive:
                step.TimeoutMs = 30_000;
                break;
            case ScenarioStepKind.Reply:
                step.TemplateName = TemplateNames.FirstOrDefault() ?? "";
                break;
            case ScenarioStepKind.Delay:
                step.DelayMs = 1_000;
                break;
            case ScenarioStepKind.Log:
                step.Message = "";
                break;
            case ScenarioStepKind.Branch:
                // Seed with one empty case so the user can start filling immediately.
                step.Cases.Add(new BranchCaseViewModel());
                break;
        }
        step.UpdateDisplayText();
        SelectedScenario.Steps.Add(step);
        SelectedStep = step;
    }

    [RelayCommand]
    private void AddBranchCase()
    {
        if (SelectedStep == null || SelectedStep.Kind != ScenarioStepKind.Branch) return;
        SelectedStep.Cases.Add(new BranchCaseViewModel());
        SelectedStep.UpdateDisplayText();
    }

    [RelayCommand]
    private void DeleteBranchCase(BranchCaseViewModel? c)
    {
        if (SelectedStep == null || c == null) return;
        SelectedStep.Cases.Remove(c);
        SelectedStep.UpdateDisplayText();
    }

    [RelayCommand]
    private void AddBranchCaseCondition(BranchCaseViewModel? c)
    {
        if (c == null) return;
        var cond = new FieldConditionViewModel { Path = "1/0", Value = "0" };
        // Try to seed template fields from the most recent Receive step before this Branch.
        if (SelectedScenario != null && SelectedStep != null)
        {
            var idx = SelectedScenario.Steps.IndexOf(SelectedStep);
            for (int i = idx - 1; i >= 0; i--)
            {
                var s = SelectedScenario.Steps[i];
                if (s.Kind == ScenarioStepKind.Receive && !string.IsNullOrEmpty(s.TemplateName))
                {
                    var fields = ExtractTemplateFields(s.TemplateName);
                    foreach (var f in fields) cond.TemplateFields.Add(f);
                    break;
                }
            }
        }
        c.Conditions.Add(cond);
        SelectedStep?.UpdateDisplayText();
    }

    [RelayCommand]
    private void DeleteBranchCaseCondition(FieldConditionViewModel? cond)
    {
        if (cond == null || SelectedStep == null) return;
        foreach (var bc in SelectedStep.Cases)
            if (bc.Conditions.Remove(cond)) break;
        SelectedStep.UpdateDisplayText();
    }

    [RelayCommand]
    private void DeleteStep()
    {
        if (SelectedScenario == null || SelectedStep == null) return;
        var idx = SelectedScenario.Steps.IndexOf(SelectedStep);
        SelectedScenario.Steps.Remove(SelectedStep);
        SelectedStep = SelectedScenario.Steps.Count > 0
            ? SelectedScenario.Steps[Math.Min(idx, SelectedScenario.Steps.Count - 1)]
            : null;
    }

    [RelayCommand]
    private void MoveStepUp()
    {
        if (SelectedScenario == null || SelectedStep == null) return;
        var idx = SelectedScenario.Steps.IndexOf(SelectedStep);
        if (idx > 0) SelectedScenario.Steps.Move(idx, idx - 1);
    }

    [RelayCommand]
    private void MoveStepDown()
    {
        if (SelectedScenario == null || SelectedStep == null) return;
        var idx = SelectedScenario.Steps.IndexOf(SelectedStep);
        if (idx < SelectedScenario.Steps.Count - 1) SelectedScenario.Steps.Move(idx, idx + 1);
    }

    [RelayCommand]
    private void AddCondition()
    {
        var cond = new FieldConditionViewModel { Path = "1/0", Value = "0" };

        // If a scenario step is selected, add to it (template comes from the step's TemplateName,
        // for Receive steps that's the template hint; for others conditions don't really apply but
        // we still allow them).
        if (SelectedStep != null)
        {
            if (!string.IsNullOrEmpty(SelectedStep.TemplateName))
            {
                var fields = ExtractTemplateFields(SelectedStep.TemplateName);
                foreach (var f in fields) cond.TemplateFields.Add(f);
            }
            SelectedStep.Conditions.Add(cond);
            SelectedStep.UpdateDisplayText();
            return;
        }
        // Otherwise, if a quick reply rule is selected, add to it
        if (SelectedQuickReply != null)
        {
            if (!string.IsNullOrEmpty(SelectedQuickReply.TriggerTemplateName))
            {
                var fields = ExtractTemplateFields(SelectedQuickReply.TriggerTemplateName);
                foreach (var f in fields) cond.TemplateFields.Add(f);
            }
            SelectedQuickReply.Conditions.Add(cond);
            SelectedQuickReply.UpdateDisplayText();
        }
    }

    [RelayCommand]
    private void DeleteCondition(FieldConditionViewModel? cond)
    {
        if (cond == null) return;
        // Try removing from scenario step first
        if (SelectedStep != null)
        {
            SelectedStep.Conditions.Remove(cond);
            SelectedStep.UpdateDisplayText();
            return;
        }
        // Otherwise try removing from quick reply rule
        if (SelectedQuickReply != null)
        {
            SelectedQuickReply.Conditions.Remove(cond);
            SelectedQuickReply.UpdateDisplayText();
        }
    }

    // ─── Save/Load ───

    [RelayCommand]
    private void SaveRules()
    {
        try
        {
            var config = ToConfig();
            config.SaveToFile(_configPath);
            StatusMessage = $"已保存 {QuickReplies.Count} 条快速回复 + {Scenarios.Count} 个场景";
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private void LoadRules()
    {
        try
        {
            LoadFromPath(_configPath);
            StatusMessage = $"已加载 {QuickReplies.Count} 条快速回复 + {Scenarios.Count} 个场景";
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败: {ex.Message}";
        }
    }


    // ─── Template Management ───

    /// <summary>
    /// Filtered template names for fuzzy search in ComboBox dropdown.
    /// </summary>
    public ObservableCollection<string> FilteredActionTemplates { get; } = [];

    /// <summary>
    /// Update the filtered template list based on search text.
    /// Called from code-behind on ComboBox TextChanged.
    /// </summary>
    public void UpdateActionTemplateFilter(string? searchText)
    {
        var filtered = string.IsNullOrWhiteSpace(searchText)
            ? TemplateNames.ToList()
            : TemplateNames.Where(t => t.Contains(searchText, StringComparison.OrdinalIgnoreCase)).ToList();

        // Avoid flickering: only update if content actually changed
        if (FilteredActionTemplates.Count == filtered.Count &&
            FilteredActionTemplates.SequenceEqual(filtered))
            return;

        // Use batch update to avoid UI glitches
        FilteredActionTemplates.Clear();
        foreach (var name in filtered)
            FilteredActionTemplates.Add(name);
    }

    /// <summary>
    /// Reset the filtered template list to show all templates.
    /// Called when dropdown closes to restore full list.
    /// </summary>
    public void ResetActionTemplateFilter()
    {
        if (FilteredActionTemplates.Count == TemplateNames.Count &&
            FilteredActionTemplates.SequenceEqual(TemplateNames))
            return;

        FilteredActionTemplates.Clear();
        foreach (var name in TemplateNames)
            FilteredActionTemplates.Add(name);
    }

    public void SetTemplates(IEnumerable<SecsMessageTemplate> templates)
    {
        Instance = this;
        _allTemplates = templates.ToList();
        TemplateNames.Clear();
        foreach (var name in _allTemplates.Select(t => t.Name).Distinct())
            TemplateNames.Add(name);

        // Initialize filtered list with all templates
        FilteredActionTemplates.Clear();
        foreach (var name in TemplateNames)
            FilteredActionTemplates.Add(name);
    }

    public void SetAllMessages(ObservableCollection<SecsMessageViewModel> messages)
    {
        _allMessages = messages;
    }

    public void UpdateDisplayedMessage(string? templateName)
    {
        if (_allMessages == null || string.IsNullOrEmpty(templateName))
        {
            DisplayedMessage = null;
            return;
        }
        DisplayedMessage = _allMessages.FirstOrDefault(m => m.Name == templateName);
    }

    public SecsMessageTemplate? FindTemplateByName(string name)
    {
        return _allTemplates.FirstOrDefault(t => t.Name == name);
    }

    public SecsMessageTemplate? FindTemplateByStreamFunction(byte stream, byte function)
    {
        return _allTemplates.FirstOrDefault(t => t.Stream == stream && t.Function == function);
    }

    /// <summary>
    /// Extract all fields from a template's item tree for dropdown selection.
    /// </summary>
    public List<FieldOption> ExtractTemplateFields(string templateName)
    {
        var tpl = FindTemplateByName(templateName);
        if (tpl == null) return [];

        var result = new List<FieldOption>();
        try
        {
            var msg = tpl.BuildMessage();
            if (msg.RootItem == null) return result;

            // Root is always a LIST in SECS-II. Enumerate its children as selectable fields.
            if (msg.RootItem.Format == SecsFormat.List)
            {
                var rootChildren = GetListItems(msg.RootItem);
                for (int i = 0; i < rootChildren.Length; i++)
                {
                    CollectFieldOptions(rootChildren[i], i.ToString(), tpl.FieldMetadata, result);
                }
            }
        }
        catch { }
        return result;
    }

    /// <summary>
    /// Get child items from a SecsList via reflection, avoiding direct cast issues.
    /// </summary>
    private static SecsItem[] GetListItems(SecsItem item)
    {
        if (item is SecsList list) return list.Items;
        // Fallback: use reflection
        var prop = item.GetType().GetProperty("Items");
        if (prop?.GetValue(item) is SecsItem[] items) return items;
        return [];
    }

    private static void CollectFieldOptions(SecsItem item, string path,
        Dictionary<string, FieldMetadata>? metadata, List<FieldOption> result)
    {
        var meta = metadata != null && metadata.TryGetValue(path, out var m) ? m : null;
        var alias = meta?.Alias;
        var description = meta?.Description;
        var typeName = item.Format switch
        {
            SecsFormat.List => "L",
            SecsFormat.ASCII => "A",
            SecsFormat.Binary => "B",
            SecsFormat.Boolean => "Boolean",
            SecsFormat.U1 => "U1", SecsFormat.U2 => "U2", SecsFormat.U4 => "U4", SecsFormat.U8 => "U8",
            SecsFormat.I1 => "I1", SecsFormat.I2 => "I2", SecsFormat.I4 => "I4", SecsFormat.I8 => "I8",
            SecsFormat.F4 => "F4", SecsFormat.F8 => "F8",
            _ => "?"
        };

        var display = string.IsNullOrEmpty(alias)
            ? $"{path} ({typeName})"
            : $"{path} ({typeName}) {alias}";
        if (!string.IsNullOrEmpty(description))
            display += $" - {description}";

        result.Add(new FieldOption
        {
            Path = path,
            DisplayName = display,
            Alias = alias,
            Description = description
        });

        // Recurse into LIST children
        if (item.Format == SecsFormat.List)
        {
            var children = GetListItems(item);
            for (int i = 0; i < children.Length; i++)
            {
                var childPath = $"{path}/{i}";
                CollectFieldOptions(children[i], childPath, metadata, result);
            }
        }
    }

    /// <summary>
    /// Populate TemplateFields on each condition from the selected trigger template.
    /// </summary>
    public void PopulateTemplateFields(ObservableCollection<FieldConditionViewModel> conditions, string templateName)
    {
        var fields = ExtractTemplateFields(templateName);
        foreach (var cond in conditions)
        {
            cond.TemplateFields.Clear();
            foreach (var f in fields)
                cond.TemplateFields.Add(f);
        }
    }

    // ─── Load/Save ───

    public void LoadFromPath(string path)
    {
        _configPath = path;
        var config = AutoReplyConfig.LoadFromFile(path);

        QuickReplies.Clear();
        foreach (var rule in config.QuickReplies)
        {
            QuickReplies.Add(QuickReplyRuleViewModel.FromModel(rule));
        }

        Scenarios.Clear();
        foreach (var scenario in config.Scenarios)
        {
            Scenarios.Add(ScenarioViewModel.FromModel(scenario));
        }

        if (QuickReplies.Count > 0) SelectedQuickReply = QuickReplies[0];
        if (Scenarios.Count > 0) SelectedScenario = Scenarios[0];
    }

    public void LoadDefault()
    {
        LoadFromPath(_configPath);
    }

    public AutoReplyConfig ToConfig()
    {
        return new AutoReplyConfig
        {
            QuickReplies = QuickReplies.Select(r => r.ToModel()).ToList(),
            Scenarios = Scenarios.Select(s => s.ToModel()).ToList(),
        };
    }

    /// <summary>
    /// Apply quick-reply rules and scenarios to the router.
    /// Call this after protocol starts. <paramref name="send"/> is invoked by Send/Reply
    /// scenario steps; pass the protocol's SendSecsMessageAsync.
    /// </summary>
    public void ApplyToRouter(MessageRouter router, Microsoft.Extensions.Logging.ILogger logger,
        Func<SecsMessage, CancellationToken, Task>? send = null)
    {
        router.ClearQuickReplyRules();

        // Register quick-reply rules
        foreach (var ruleVm in QuickReplies.Where(r => r.Enabled))
        {
            var rule = ruleVm.ToModel();
            var template = FindTemplateByName(ruleVm.ReplyTemplateName);
            if (template != null)
                router.RegisterQuickReplyRule(rule, template);
        }

        // Build a single engine for all scenarios; each Run picks one.
        // Pass CurrentRole so the engine refuses to run cross-role scripts (e.g. an EAP-side
        // scenario loaded on the equipment-side simulator).
        var engine = new ScenarioEngine(logger, FindTemplateByName, send, CurrentRole);
        engine.Log += text =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => StatusMessage = text);
        engine.ScenarioFinished += (sc, status) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => RunStatus = $"{sc.Name}: {status}");
        router.SetScenarioEngine(engine);
        _activeEngine = engine;

        // Auto-start scenarios — but only those whose authored Role matches this side.
        foreach (var scVm in Scenarios.Where(s => s.Enabled && s.AutoStart
                                                  && ScenarioEngine.RoleAllows(s.Role, CurrentRole)))
            engine.Start(scVm.ToModel());
    }

    /// <summary>
    /// Hot-reload: re-register all rules on a running router.
    /// </summary>
    public void HotReloadToRouter(MessageRouter router, Microsoft.Extensions.Logging.ILogger logger,
        Func<SecsMessage, CancellationToken, Task>? send = null)
    {
        ApplyToRouter(router, logger, send);
    }

    partial void OnSelectedQuickReplyChanged(QuickReplyRuleViewModel? value)
    {
        if (value == null) return;
        value.UpdateDisplayText();

        // Auto-populate trigger/reply template from existing S/F
        if (string.IsNullOrEmpty(value.TriggerTemplateName) && value.TriggerStream > 0)
        {
            var trigger = FindTemplateByStreamFunction(value.TriggerStream, value.TriggerFunction);
            if (trigger != null)
            {
                value.TriggerTemplateName = trigger.Name;
                // Reply template is auto-set by OnTriggerTemplateNameChanged
            }
        }
        else if (!string.IsNullOrEmpty(value.TriggerTemplateName))
        {
            // Trigger template already set — ensure reply template is also populated
            if (string.IsNullOrEmpty(value.ReplyTemplateName))
            {
                var reply = FindTemplateByStreamFunction(value.TriggerStream, (byte)(value.TriggerFunction + 1));
                if (reply != null)
                    value.ReplyTemplateName = reply.Name;
            }
            // Populate template fields for conditions
            PopulateTemplateFields(value.Conditions, value.TriggerTemplateName);
        }
    }
}

// ─── Quick Reply Rule ViewModel ───

public partial class QuickReplyRuleViewModel : ObservableObject
{
    [ObservableProperty]
    private byte _triggerStream;

    [ObservableProperty]
    private byte _triggerFunction;

    [ObservableProperty]
    private string _triggerTemplateName = "";

    [ObservableProperty]
    private string _replyTemplateName = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private bool _enabled = true;

    [ObservableProperty]
    private string _displayText = "";

    [ObservableProperty]
    private ObservableCollection<FieldConditionViewModel> _conditions = [];

    partial void OnTriggerStreamChanged(byte value) => UpdateDisplayText();
    partial void OnReplyTemplateNameChanged(string value) => UpdateDisplayText();

    partial void OnTriggerTemplateNameChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            var parent = AutoReplyViewModel.Instance;
            var tpl = parent?.FindTemplateByName(value);
            if (tpl != null)
            {
                TriggerStream = tpl.Stream;
                TriggerFunction = tpl.Function;

                // Auto-select reply template: reply function = trigger function + 1
                var replyTpl = parent?.FindTemplateByStreamFunction(tpl.Stream, (byte)(tpl.Function + 1));
                if (replyTpl != null)
                    ReplyTemplateName = replyTpl.Name;
            }
            parent?.PopulateTemplateFields(Conditions, value);
        }
    }

    public void UpdateDisplayText()
    {
        var condStr = Conditions.Count > 0 ? " [条件]" : "";
        DisplayText = $"{(Enabled ? "✓" : "✗")} S{TriggerStream}F{TriggerFunction} → {ReplyTemplateName}{condStr}";
    }

    public AutoReplyRule ToModel()
    {
        return new AutoReplyRule
        {
            TriggerStream = TriggerStream,
            TriggerFunction = TriggerFunction,
            Conditions = Conditions.Select(c => c.ToModel()).ToList(),
            Description = Description,
            Enabled = Enabled,
            ReplyTemplateName = ReplyTemplateName,
        };
    }

    public static QuickReplyRuleViewModel FromModel(AutoReplyRule rule)
    {
        var vm = new QuickReplyRuleViewModel
        {
            TriggerStream = rule.TriggerStream,
            TriggerFunction = rule.TriggerFunction,
            Description = rule.Description,
            Enabled = rule.Enabled,
            ReplyTemplateName = rule.ReplyTemplateName,
            Conditions = new ObservableCollection<FieldConditionViewModel>(
                rule.Conditions.Select(FieldConditionViewModel.FromModel)),
        };
        vm.UpdateDisplayText();
        return vm;
    }
}

// ─── Field Condition ViewModel ───

public partial class FieldConditionViewModel : ObservableObject
{
    [ObservableProperty]
    private string _path = "";

    [ObservableProperty]
    private string _operator = "==";

    [ObservableProperty]
    private string _value = "";

    [ObservableProperty]
    private FieldOption? _selectedFieldOption;

    /// <summary>
    /// Available fields from the selected trigger template, for dropdown selection.
    /// </summary>
    public ObservableCollection<FieldOption> TemplateFields { get; } = [];

    public string[] Operators => FieldCondition.SupportedOperators;

    partial void OnSelectedFieldOptionChanged(FieldOption? value)
    {
        if (value != null)
            Path = value.Path;
    }

    partial void OnPathChanged(string value)
    {
        // Sync SelectedFieldOption when Path changes (e.g., loaded from model)
        if (SelectedFieldOption?.Path != value)
            SelectedFieldOption = TemplateFields.FirstOrDefault(f => f.Path == value);
    }

    public FieldCondition ToModel() => new() { Path = Path, Operator = Operator, Value = Value };

    public static FieldConditionViewModel FromModel(FieldCondition cond) => new()
    {
        Path = cond.Path,
        Operator = cond.Operator ?? "==",
        Value = cond.Value,
    };
}

/// <summary>
/// A selectable field from a template's item tree.
/// </summary>
public class FieldOption
{
    public required string Path { get; init; }
    public required string DisplayName { get; init; }
    public string? Alias { get; init; }
    public string? Description { get; init; }

    /// <summary>
    /// Display format: "2/0/1 (A) 别名"
    /// </summary>
    public string DisplayWithAlias => !string.IsNullOrEmpty(Alias)
        ? $"{DisplayName} {Alias}"
        : DisplayName;
}

// ─── Scenario ViewModel ───

public partial class ScenarioViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private bool _enabled = true;

    [ObservableProperty]
    private bool _loop;

    [ObservableProperty]
    private bool _autoStart;

    /// <summary>
    /// Which side of the link this scenario is authored for. Drives the role badge in the
    /// list and is checked when ScenarioEngine.Start is called.
    /// </summary>
    [ObservableProperty]
    private ScenarioRole _role = ScenarioRole.Any;

    /// <summary>String adapter for binding <see cref="Role"/> to a ComboBox of strings.</summary>
    public string RoleName
    {
        get => Role.ToString();
        set { if (Enum.TryParse<ScenarioRole>(value, true, out var r)) Role = r; }
    }
    public string[] RoleNames => Enum.GetNames<ScenarioRole>();

    /// <summary>Short tag shown in the scenario list (e.g. "EAP", "EQP", "ANY").</summary>
    public string RoleBadge => Role switch
    {
        ScenarioRole.Host => "EAP",
        ScenarioRole.Equipment => "EQP",
        _ => "ANY",
    };

    /// <summary>Foreground colour for the role badge (kept in VM so axaml can bind directly).</summary>
    public string RoleBadgeColor => Role switch
    {
        ScenarioRole.Host => "#89B4FA",        // blue
        ScenarioRole.Equipment => "#A6E3A1",   // green
        _ => "#888888",                        // gray
    };

    partial void OnRoleChanged(ScenarioRole value)
    {
        OnPropertyChanged(nameof(RoleName));
        OnPropertyChanged(nameof(RoleBadge));
        OnPropertyChanged(nameof(RoleBadgeColor));
    }

    public ObservableCollection<ScenarioStepViewModel> Steps { get; } = [];

    public ScenarioViewModel()
    {
        Steps.CollectionChanged += (_, args) =>
        {
            if (args.NewItems != null)
                foreach (ScenarioStepViewModel s in args.NewItems)
                    s.PropertyChanged += OnStepPropertyChanged;
            if (args.OldItems != null)
                foreach (ScenarioStepViewModel s in args.OldItems)
                    s.PropertyChanged -= OnStepPropertyChanged;
            RefreshAvailableLabels();
        };
    }

    private void OnStepPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScenarioStepViewModel.Label))
            RefreshAvailableLabels();
    }

    /// <summary>
    /// Push the current set of non-empty labels to every step's AvailableLabels list.
    /// Branch case rows bind their target-label ComboBox to that list.
    /// </summary>
    public void RefreshAvailableLabels()
    {
        var labels = Steps
            .Select(s => s.Label)
            .Where(l => !string.IsNullOrEmpty(l))
            .Distinct()
            .ToList();
        foreach (var s in Steps)
        {
            s.AvailableLabels.Clear();
            foreach (var l in labels) s.AvailableLabels.Add(l);
        }
    }

    public ScenarioDefinition ToModel()
    {
        return new ScenarioDefinition
        {
            Name = Name,
            Description = Description,
            Role = Role,
            Enabled = Enabled,
            Loop = Loop,
            AutoStart = AutoStart,
            Steps = Steps.Select(s => s.ToModel()).ToList(),
        };
    }

    public static ScenarioViewModel FromModel(ScenarioDefinition def)
    {
        var vm = new ScenarioViewModel
        {
            Name = def.Name,
            Description = def.Description,
            Role = def.Role,
            Enabled = def.Enabled,
            Loop = def.Loop,
            AutoStart = def.AutoStart,
        };
        foreach (var step in def.Steps)
            vm.Steps.Add(ScenarioStepViewModel.FromModel(step));
        vm.RefreshAvailableLabels();
        return vm;
    }
}

// ─── Scenario Step ViewModel ───

/// <summary>
/// Single step VM. Holds every kind's fields; the UI shows only those relevant to <see cref="Kind"/>.
/// </summary>
public partial class ScenarioStepViewModel : ObservableObject
{
    [ObservableProperty]
    private ScenarioStepKind _kind = ScenarioStepKind.Receive;

    [ObservableProperty]
    private string _label = "";

    // Send / Reply
    [ObservableProperty]
    private string _templateName = "";

    [ObservableProperty]
    private bool _waitReply;

    // Receive
    [ObservableProperty]
    private byte _stream;

    [ObservableProperty]
    private byte _function;

    [ObservableProperty]
    private int _timeoutMs = 30_000;

    [ObservableProperty]
    private ReceiveTimeoutAction _onTimeout = ReceiveTimeoutAction.Fail;

    public ObservableCollection<FieldConditionViewModel> Conditions { get; } = [];

    // Delay
    [ObservableProperty]
    private int _delayMs = 1_000;

    // Log
    [ObservableProperty]
    private string _message = "";

    // Display
    [ObservableProperty]
    private string _displayText = "";

    // Kind-driven visibility flags for the editor (so axaml can use a single ContentControl
    // with simple {Binding IsXxxKind} → IsVisible bindings rather than a DataTemplateSelector).
    public bool IsSendKind => Kind == ScenarioStepKind.Send;
    public bool IsReceiveKind => Kind == ScenarioStepKind.Receive;
    public bool IsReplyKind => Kind == ScenarioStepKind.Reply;
    public bool IsDelayKind => Kind == ScenarioStepKind.Delay;
    public bool IsLogKind => Kind == ScenarioStepKind.Log;
    public bool IsBranchKind => Kind == ScenarioStepKind.Branch;
    public bool UsesTemplate => IsSendKind || IsReplyKind;

    /// <summary>Branch cases (used only when Kind == Branch).</summary>
    public ObservableCollection<BranchCaseViewModel> Cases { get; } = [];

    [ObservableProperty]
    private string _defaultLabel = "";

    /// <summary>Set by the parent ScenarioViewModel so each case row can pick a target label from the same scenario.</summary>
    public ObservableCollection<string> AvailableLabels { get; } = [];

    public string[] KindNames => Enum.GetNames<ScenarioStepKind>();
    public string[] OnTimeoutNames => Enum.GetNames<ReceiveTimeoutAction>();

    /// <summary>String adapter for binding <see cref="Kind"/> to a ComboBox of strings.</summary>
    public string KindName
    {
        get => Kind.ToString();
        set
        {
            if (Enum.TryParse<ScenarioStepKind>(value, true, out var k))
                Kind = k;
        }
    }

    /// <summary>String adapter for binding <see cref="OnTimeout"/> to a ComboBox of strings.</summary>
    public string OnTimeoutName
    {
        get => OnTimeout.ToString();
        set
        {
            if (Enum.TryParse<ReceiveTimeoutAction>(value, true, out var ot))
                OnTimeout = ot;
        }
    }

    partial void OnKindChanged(ScenarioStepKind value)
    {
        OnPropertyChanged(nameof(IsSendKind));
        OnPropertyChanged(nameof(IsReceiveKind));
        OnPropertyChanged(nameof(IsReplyKind));
        OnPropertyChanged(nameof(IsDelayKind));
        OnPropertyChanged(nameof(IsLogKind));
        OnPropertyChanged(nameof(IsBranchKind));
        OnPropertyChanged(nameof(UsesTemplate));
        OnPropertyChanged(nameof(KindName));
        UpdateDisplayText();
    }

    partial void OnOnTimeoutChanged(ReceiveTimeoutAction value)
        => OnPropertyChanged(nameof(OnTimeoutName));

    partial void OnStreamChanged(byte value) => UpdateDisplayText();
    partial void OnFunctionChanged(byte value) => UpdateDisplayText();
    partial void OnDelayMsChanged(int value) => UpdateDisplayText();
    partial void OnMessageChanged(string value) => UpdateDisplayText();
    partial void OnLabelChanged(string value) => UpdateDisplayText();

    partial void OnTemplateNameChanged(string value)
    {
        UpdateDisplayText();
        if (string.IsNullOrEmpty(value)) return;

        var parent = AutoReplyViewModel.Instance;
        var tpl = parent?.FindTemplateByName(value);
        if (tpl != null && Kind == ScenarioStepKind.Receive)
        {
            // For a Receive step using a template as a shape hint, copy S/F.
            Stream = tpl.Stream;
            Function = tpl.Function;
        }
        // Populate condition field options from the template body so the user
        // can pick paths via the dropdown.
        if (Kind == ScenarioStepKind.Receive && parent != null)
            parent.PopulateTemplateFields(Conditions, value);
    }

    public void UpdateDisplayText()
    {
        var label = string.IsNullOrEmpty(Label) ? "" : $" — {Label}";
        DisplayText = Kind switch
        {
            ScenarioStepKind.Send => $"▶ Send {(string.IsNullOrEmpty(TemplateName) ? "(未设置)" : TemplateName)}{label}",
            ScenarioStepKind.Receive => BuildReceiveDisplay() + label,
            ScenarioStepKind.Reply => $"↩ Reply {(string.IsNullOrEmpty(TemplateName) ? "(未设置)" : TemplateName)}{label}",
            ScenarioStepKind.Delay => $"⏱ Delay {DelayMs} ms{label}",
            ScenarioStepKind.Log => $"📝 Log {Message}{label}",
            ScenarioStepKind.Branch => BuildBranchDisplay() + label,
            _ => $"? {Kind}{label}",
        };
    }

    private string BuildReceiveDisplay()
    {
        var sf = Stream == 0 && Function == 0 ? "any" : $"S{Stream}F{Function}";
        if (Conditions.Count == 0) return $"◀ Recv {sf}";
        var conds = string.Join(" & ", Conditions.Select(c => $"[{c.Path}] {c.Operator} {c.Value}"));
        return $"◀ Recv {sf} where {conds}";
    }

    private string BuildBranchDisplay()
    {
        if (Cases.Count == 0)
            return string.IsNullOrEmpty(DefaultLabel) ? "⑂ Branch (无规则)" : $"⑂ Goto → {DefaultLabel}";
        var parts = Cases.Select(c => $"{c.Summary}→{c.TargetLabel}").ToList();
        if (!string.IsNullOrEmpty(DefaultLabel)) parts.Add($"else→{DefaultLabel}");
        return $"⑂ Branch {{ {string.Join(", ", parts)} }}";
    }

    public ScenarioStep ToModel()
    {
        return new ScenarioStep
        {
            Kind = Kind,
            Label = Label,
            TemplateName = TemplateName,
            WaitReply = WaitReply,
            Stream = Stream,
            Function = Function,
            Conditions = Conditions.Select(c => c.ToModel()).ToList(),
            TimeoutMs = TimeoutMs,
            OnTimeout = OnTimeout,
            DelayMs = DelayMs,
            Message = Message,
            Cases = Cases.Select(c => c.ToModel()).ToList(),
            DefaultLabel = DefaultLabel,
        };
    }

    public static ScenarioStepViewModel FromModel(ScenarioStep step)
    {
        var vm = new ScenarioStepViewModel
        {
            Kind = step.Kind,
            Label = step.Label,
            TemplateName = step.TemplateName,
            WaitReply = step.WaitReply,
            Stream = step.Stream,
            Function = step.Function,
            TimeoutMs = step.TimeoutMs,
            OnTimeout = step.OnTimeout,
            DelayMs = step.DelayMs,
            Message = step.Message,
            DefaultLabel = step.DefaultLabel,
        };
        foreach (var cond in step.Conditions.Select(FieldConditionViewModel.FromModel))
            vm.Conditions.Add(cond);
        foreach (var c in step.Cases)
            vm.Cases.Add(BranchCaseViewModel.FromModel(c));
        vm.UpdateDisplayText();
        return vm;
    }
}

/// <summary>
/// One case row in a Branch step. Holds conditions + target label.
/// </summary>
public partial class BranchCaseViewModel : ObservableObject
{
    public ObservableCollection<FieldConditionViewModel> Conditions { get; } = [];

    [ObservableProperty]
    private string _targetLabel = "";

    public string Summary => Conditions.Count == 0
        ? "always"
        : string.Join(" & ", Conditions.Select(c => $"[{c.Path}] {c.Operator} {c.Value}"));

    public BranchCase ToModel() => new()
    {
        Conditions = Conditions.Select(c => c.ToModel()).ToList(),
        TargetLabel = TargetLabel,
    };

    public static BranchCaseViewModel FromModel(BranchCase c)
    {
        var vm = new BranchCaseViewModel { TargetLabel = c.TargetLabel };
        foreach (var cond in c.Conditions.Select(FieldConditionViewModel.FromModel))
            vm.Conditions.Add(cond);
        return vm;
    }
}
