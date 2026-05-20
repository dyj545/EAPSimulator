using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EAPSimulator.Core.Protocols.SecsGem;
using EAPSimulator.Core.Protocols.SecsGem.AutoReply;
using Newtonsoft.Json;

namespace EAPSimulator.UI.ViewModels;

public partial class AutoReplyViewModel : ObservableObject
{
    private string _configPath = AutoReplyConfig.GetDefaultPath();

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
        var scenario = new ScenarioViewModel { Name = "New Scenario", Description = "", Enabled = true };
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
    private void AddStep()
    {
        if (SelectedScenario == null) return;
        var step = new ScenarioStepViewModel
        {
            Stream = 0,
            Function = 0,
            ActionTemplateName = "",
        };
        step.UpdateDisplayText();
        SelectedScenario.Steps.Add(step);
        SelectedStep = step;
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
        // If a scenario step is selected, add to it
        if (SelectedStep != null)
        {
            SelectedStep.Conditions.Add(new FieldConditionViewModel { Path = "1/0", Value = "0" });
            return;
        }
        // Otherwise, if a quick reply rule is selected, add to it
        if (SelectedQuickReply != null)
        {
            SelectedQuickReply.Conditions.Add(new FieldConditionViewModel { Path = "1/0", Value = "0" });
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
        _allTemplates = templates.ToList();
        TemplateNames.Clear();
        foreach (var name in _allTemplates.Select(t => t.Name).Distinct())
            TemplateNames.Add(name);

        // Initialize filtered list with all templates
        FilteredActionTemplates.Clear();
        foreach (var name in TemplateNames)
            FilteredActionTemplates.Add(name);
    }

    public SecsMessageTemplate? FindTemplateByName(string name)
    {
        return _allTemplates.FirstOrDefault(t => t.Name == name);
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
    /// Call this after protocol starts.
    /// </summary>
    public void ApplyToRouter(MessageRouter router, Microsoft.Extensions.Logging.ILogger logger)
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

        // Register scenario engine
        var scenarioDefs = Scenarios.Where(s => s.Enabled).Select(s => s.ToModel()).ToList();
        if (scenarioDefs.Count > 0)
        {
            var engine = new ScenarioEngine(logger, scenarioDefs, FindTemplateByName);
            router.SetScenarioEngine(engine);
        }
        else
        {
            router.SetScenarioEngine(null);
        }
    }

    /// <summary>
    /// Hot-reload: re-register all rules on a running router.
    /// </summary>
    public void HotReloadToRouter(MessageRouter router, Microsoft.Extensions.Logging.ILogger logger)
    {
        ApplyToRouter(router, logger);
    }

    partial void OnSelectedQuickReplyChanged(QuickReplyRuleViewModel? value)
    {
        if (value != null) value.UpdateDisplayText();
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
    private string _value = "";

    public FieldCondition ToModel() => new() { Path = Path, Value = Value };

    public static FieldConditionViewModel FromModel(FieldCondition cond) => new()
    {
        Path = cond.Path,
        Value = cond.Value,
    };
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

    public ObservableCollection<ScenarioStepViewModel> Steps { get; } = [];

    public ScenarioDefinition ToModel()
    {
        return new ScenarioDefinition
        {
            Name = Name,
            Description = Description,
            Enabled = Enabled,
            Loop = Loop,
            Steps = Steps.Select(s => s.ToModel()).ToList(),
        };
    }

    public static ScenarioViewModel FromModel(ScenarioDefinition def)
    {
        var vm = new ScenarioViewModel
        {
            Name = def.Name,
            Description = def.Description,
            Enabled = def.Enabled,
            Loop = def.Loop,
        };
        foreach (var step in def.Steps)
            vm.Steps.Add(ScenarioStepViewModel.FromModel(step));
        return vm;
    }
}

// ─── Scenario Step ViewModel ───

public partial class ScenarioStepViewModel : ObservableObject
{
    [ObservableProperty]
    private byte _stream;

    [ObservableProperty]
    private byte _function;

    [ObservableProperty]
    private string _actionTemplateName = "";

    [ObservableProperty]
    private byte _actionStream;

    [ObservableProperty]
    private byte _actionFunction;

    [ObservableProperty]
    private string _displayText = "";

    public ObservableCollection<FieldConditionViewModel> Conditions { get; } = [];

    partial void OnStreamChanged(byte value) => UpdateDisplayText();
    partial void OnFunctionChanged(byte value) => UpdateDisplayText();
    partial void OnActionTemplateNameChanged(string value) => UpdateDisplayText();

    public void UpdateDisplayText()
    {
        var condStr = Conditions.Count > 0
            ? " where " + string.Join(" & ", Conditions.Select(c => $"[{c.Path}]={c.Value}"))
            : "";
        var action = !string.IsNullOrEmpty(ActionTemplateName) ? ActionTemplateName : "(未设置)";
        var trigger = Stream == 0 && Function == 0 ? "(未设置)" : $"S{Stream}F{Function}";
        DisplayText = $"{trigger}{condStr} → {action}";
    }

    public ScenarioStep ToModel()
    {
        return new ScenarioStep
        {
            Stream = Stream,
            Function = Function,
            Conditions = Conditions.Select(c => c.ToModel()).ToList(),
            ActionTemplateName = ActionTemplateName,
            ActionStream = ActionStream,
            ActionFunction = ActionFunction,
        };
    }

    public static ScenarioStepViewModel FromModel(ScenarioStep step)
    {
        var vm = new ScenarioStepViewModel
        {
            Stream = step.Stream,
            Function = step.Function,
            ActionTemplateName = step.ActionTemplateName,
            ActionStream = step.ActionStream,
            ActionFunction = step.ActionFunction,
        };
        foreach (var cond in step.Conditions.Select(FieldConditionViewModel.FromModel))
            vm.Conditions.Add(cond);
        vm.UpdateDisplayText();
        return vm;
    }
}
