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
    /// 场景名列表 — 给 CallScenario 步骤的子场景下拉做模糊搜索用。
    /// 与 <see cref="Scenarios"/> 同步：Scenarios 变更 / 单个场景改名时这里也会刷新。
    /// </summary>
    public ObservableCollection<string> ScenarioNames { get; } = [];

    /// <summary>
    /// Available template names from the message template file.
    /// Set by MainViewModel after loading templates.
    /// </summary>
    public ObservableCollection<string> TemplateNames { get; } = [];

    /// <summary>
    /// Available Host/MES message template names. Set by MainViewModel when host templates load.
    /// Empty when Host protocol is not in use.
    /// </summary>
    public ObservableCollection<string> HostMessageNames { get; } = [];

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

    /// <summary>
    /// True = show the flow-canvas; false = show the legacy step ListBox.
    /// </summary>
    [ObservableProperty]
    private bool _isFlowView = true;

    /// <summary>Inverse of <see cref="IsFlowView"/> for binding to the ListBox's IsVisible.</summary>
    public bool IsListView => !IsFlowView;

    partial void OnIsFlowViewChanged(bool value) => OnPropertyChanged(nameof(IsListView));

    // ─── Debugger UI state ───

    /// <summary>True when the running engine is parked on a breakpoint or after a step.</summary>
    [ObservableProperty]
    private bool _isPaused;

    /// <summary>Step index the engine is currently paused on (-1 = not paused).</summary>
    [ObservableProperty]
    private int _pausedStepIndex = -1;

    /// <summary>Variable snapshot exposed in the watch panel; refreshed on every pause.</summary>
    public ObservableCollection<DebuggerVariableRow> WatchVariables { get; } = new();

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

    public AutoReplyViewModel()
    {
        Scenarios.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
                foreach (ScenarioViewModel s in e.NewItems)
                    s.PropertyChanged += OnScenarioPropertyChanged;
            if (e.OldItems != null)
                foreach (ScenarioViewModel s in e.OldItems)
                    s.PropertyChanged -= OnScenarioPropertyChanged;
            RefreshScenarioNames();
        };
    }

    private void OnScenarioPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScenarioViewModel.Name))
            RefreshScenarioNames();
    }

    private void RefreshScenarioNames()
    {
        ScenarioNames.Clear();
        foreach (var s in Scenarios)
            ScenarioNames.Add(s.Name);
    }

    [RelayCommand]
    private void RunScenario()
    {
        if (SelectedScenario == null) { RunStatus = "未选择场景"; return; }
        if (_activeEngine == null) { RunStatus = "请先连接协议(Run 需要发送通道)"; return; }
        var def = SelectedScenario.ToModel();
        // Sync breakpoints from the VM (per-step IsBreakpoint flag) into the engine before
        // starting — the engine reads them at every step boundary; we push the full set on
        // each Run so toggles between runs take effect.
        SyncBreakpointsToEngine(def);
        WireDebuggerEvents(_activeEngine);
        _activeEngine.Start(def);
        RunStatus = $"运行中: {def.Name}";
    }

    [RelayCommand]
    private void StopScenario()
    {
        _activeEngine?.Stop();
        RunStatus = "已请求停止";
    }

    /// <summary>Pause at the next step boundary.</summary>
    [RelayCommand]
    private void PauseScenario() => _activeEngine?.Pause();

    /// <summary>Resume from the current pause and run to next breakpoint / end.</summary>
    [RelayCommand]
    private void ContinueScenario() => _activeEngine?.Continue();

    /// <summary>Run exactly one step then pause again.</summary>
    [RelayCommand]
    private void StepOverScenario() => _activeEngine?.StepOver();

    /// <summary>Toggle the breakpoint on the currently-selected step.</summary>
    [RelayCommand]
    private void ToggleBreakpoint()
    {
        if (SelectedStep == null) return;
        SelectedStep.IsBreakpoint = !SelectedStep.IsBreakpoint;
        // If the engine is running, push the change live so it takes effect next step.
        if (SelectedScenario != null && _activeEngine != null)
        {
            var idx = SelectedScenario.Steps.IndexOf(SelectedStep);
            if (idx >= 0)
            {
                if (SelectedStep.IsBreakpoint) _activeEngine.Breakpoints.Add(idx);
                else _activeEngine.Breakpoints.Remove(idx);
            }
        }
    }

    private void SyncBreakpointsToEngine(ScenarioDefinition def)
    {
        if (_activeEngine == null || SelectedScenario == null) return;
        _activeEngine.Breakpoints.Clear();
        for (int i = 0; i < SelectedScenario.Steps.Count; i++)
            if (SelectedScenario.Steps[i].IsBreakpoint)
                _activeEngine.Breakpoints.Add(i);
    }

    private bool _debuggerWired;
    private void WireDebuggerEvents(ScenarioEngine engine)
    {
        if (_debuggerWired) return;
        _debuggerWired = true;
        engine.Paused += (_, pc, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsPaused = true;
                PausedStepIndex = pc;
                RefreshWatchVariables(engine);
            });
        };
        engine.Resumed += () =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsPaused = false;
                PausedStepIndex = -1;
            });
        };
    }

    private void RefreshWatchVariables(ScenarioEngine engine)
    {
        WatchVariables.Clear();
        foreach (var (k, v) in engine.PausedVariables.OrderBy(p => p.Key, StringComparer.Ordinal))
            WatchVariables.Add(new DebuggerVariableRow(k, v));
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
        // Insert immediately AFTER the currently selected step (most natural "add next step"
        // gesture). With no selection — e.g. brand-new scenario — append to the end.
        var sel = SelectedStep;
        var insertAt = SelectedScenario.Steps.Count;
        if (sel != null)
        {
            var idx = SelectedScenario.Steps.IndexOf(sel);
            if (idx >= 0)
                insertAt = idx + 1;
        }
        // Sensible per-kind defaults
        switch (kind)
        {
            case ScenarioStepKind.Send:
                // Keep new Send steps blank. Defaulting to the first template (usually S1F1)
                // makes users accidentally send the wrong message; the template picker already
                // supports fuzzy search so choosing explicitly is cheap.
                break;
            case ScenarioStepKind.Receive:
                step.TimeoutMs = 30_000;
                break;
            case ScenarioStepKind.Reply:
                step.TemplateName = DefaultReplyTemplateName(insertAt) ?? TemplateNames.FirstOrDefault() ?? "";
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
            case ScenarioStepKind.SetVariable:
                step.VariableName = "var1";
                step.VariableSource = VariableSource.Literal;
                step.LiteralValue = "";
                break;
            case ScenarioStepKind.Loop:
                // Auto-generate a fresh LoopId so two Loop steps in the same scenario don't clash.
                step.LoopId = NextLoopId();
                step.LoopTimes = 3;
                break;
            case ScenarioStepKind.EndLoop:
                // Default to closing the most recent unclosed Loop, if any.
                step.LoopId = OpenLoopId();
                break;
            case ScenarioStepKind.CallScenario:
                step.SubScenarioName = Scenarios.FirstOrDefault(s => s != SelectedScenario)?.Name ?? "";
                break;
            case ScenarioStepKind.ForEach:
                step.ForEachId = NextForEachId();
                step.ForEachSource = ForEachSource.Variable;
                step.ForEachItemVariable = "item";
                step.ForEachSeparator = ",";
                break;
            case ScenarioStepKind.EndForEach:
                step.ForEachId = OpenForEachId();
                break;
        }
        step.UpdateDisplayText();
        SelectedScenario.ShiftLayoutOverrides(insertAt, +1);
        SelectedScenario.Steps.Insert(insertAt, step);
        SelectedStep = step;
    }

    /// <summary>
    /// Insert a step of the given kind at <paramref name="insertAfterIndex"/> + 1 (i.e. immediately
    /// after that step). Used by the flow-canvas right-click menu so the user can drop a node
    /// into the middle of the flow without first selecting and then clicking a toolbar button.
    /// </summary>
    public void InsertStepAfter(int insertAfterIndex, ScenarioStepKind kind)
    {
        if (SelectedScenario == null) return;
        var insertAt = Math.Clamp(insertAfterIndex + 1, 0, SelectedScenario.Steps.Count);
        var step = BuildStepWithDefaults(kind, insertAt);
        SelectedScenario.ShiftLayoutOverrides(insertAt, +1);
        SelectedScenario.Steps.Insert(insertAt, step);
        SelectedStep = step;
    }

    public void InsertStepBefore(int insertBeforeIndex, ScenarioStepKind kind)
    {
        if (SelectedScenario == null) return;
        var insertAt = Math.Clamp(insertBeforeIndex, 0, SelectedScenario.Steps.Count);
        var step = BuildStepWithDefaults(kind, insertAt);
        SelectedScenario.ShiftLayoutOverrides(insertAt, +1);
        SelectedScenario.Steps.Insert(insertAt, step);
        SelectedStep = step;
    }

    /// <summary>Centralised per-kind defaults so both append and insert paths agree.</summary>
    private ScenarioStepViewModel BuildStepWithDefaults(ScenarioStepKind kind, int? insertAt = null)
    {
        var step = new ScenarioStepViewModel { Kind = kind };
        switch (kind)
        {
            case ScenarioStepKind.Send:
                // Keep new Send steps blank. Defaulting to the first template (usually S1F1)
                // makes users accidentally send the wrong message; the template picker already
                // supports fuzzy search so choosing explicitly is cheap.
                break;
            case ScenarioStepKind.Receive:
                step.TimeoutMs = 30_000;
                break;
            case ScenarioStepKind.Reply:
                step.TemplateName = DefaultReplyTemplateName(insertAt) ?? TemplateNames.FirstOrDefault() ?? "";
                break;
            case ScenarioStepKind.Delay:
                step.DelayMs = 1_000;
                break;
            case ScenarioStepKind.Branch:
                step.Cases.Add(new BranchCaseViewModel());
                break;
            case ScenarioStepKind.SetVariable:
                step.VariableName = "var1";
                step.VariableSource = VariableSource.Literal;
                break;
            case ScenarioStepKind.Loop:
                step.LoopId = NextLoopId();
                step.LoopTimes = 3;
                break;
            case ScenarioStepKind.EndLoop:
                step.LoopId = OpenLoopId();
                break;
            case ScenarioStepKind.CallScenario:
                step.SubScenarioName = Scenarios.FirstOrDefault(s => s != SelectedScenario)?.Name ?? "";
                break;
            case ScenarioStepKind.ForEach:
                step.ForEachId = NextForEachId();
                step.ForEachSource = ForEachSource.Variable;
                step.ForEachItemVariable = "item";
                step.ForEachSeparator = ",";
                break;
            case ScenarioStepKind.EndForEach:
                step.ForEachId = OpenForEachId();
                break;
        }
        step.UpdateDisplayText();
        return step;
    }

    /// <summary>
    /// Pick a reply template for a newly inserted Reply step from the nearest previous Receive.
    /// SECS/GEM replies conventionally use the same Stream and Function+1, so a Receive S6F11
    /// suggests S6F12 automatically. This is only a default; the user can still override it in UI.
    /// </summary>
    private string? DefaultReplyTemplateName(int? insertAt)
    {
        if (SelectedScenario == null) return null;
        var start = Math.Min(insertAt ?? SelectedScenario.Steps.Count, SelectedScenario.Steps.Count) - 1;
        for (int i = start; i >= 0; i--)
        {
            var prev = SelectedScenario.Steps[i];
            if (prev.Kind != ScenarioStepKind.Receive) continue;

            byte stream = prev.Stream;
            byte function = prev.Function;
            if ((stream == 0 || function == 0) && !string.IsNullOrEmpty(prev.TemplateName))
            {
                var receiveTpl = FindTemplateByName(prev.TemplateName);
                if (receiveTpl != null)
                {
                    stream = receiveTpl.Stream;
                    function = receiveTpl.Function;
                }
            }
            if (stream == 0 || function == 0 || function == byte.MaxValue) return null;
            var replyTpl = FindTemplateByStreamFunction(stream, (byte)(function + 1));
            return replyTpl?.Name;
        }
        return null;
    }

    /// <summary>Pick the next free LoopId in the current scenario (L1, L2, ...).</summary>
    private string NextLoopId()
    {
        if (SelectedScenario == null) return "L1";
        var taken = new HashSet<string>(
            SelectedScenario.Steps.Where(s => s.Kind == ScenarioStepKind.Loop)
                .Select(s => s.LoopId), StringComparer.Ordinal);
        for (int i = 1; ; i++)
        {
            var id = "L" + i;
            if (!taken.Contains(id)) return id;
        }
    }

    /// <summary>
    /// Find the LoopId of the deepest still-open Loop (no matching EndLoop yet) in the
    /// current scenario — handy default when the user adds an EndLoop right after a Loop.
    /// </summary>
    private string OpenLoopId()
    {
        if (SelectedScenario == null) return "";
        var stack = new Stack<string>();
        foreach (var s in SelectedScenario.Steps)
        {
            if (s.Kind == ScenarioStepKind.Loop && !string.IsNullOrEmpty(s.LoopId))
                stack.Push(s.LoopId);
            else if (s.Kind == ScenarioStepKind.EndLoop && stack.Count > 0)
                stack.Pop();
        }
        return stack.Count > 0 ? stack.Peek() : "";
    }

    /// <summary>Pick the next free ForEachId in the current scenario (F1, F2, ...).</summary>
    private string NextForEachId()
    {
        if (SelectedScenario == null) return "F1";
        var taken = new HashSet<string>(
            SelectedScenario.Steps.Where(s => s.Kind == ScenarioStepKind.ForEach)
                .Select(s => s.ForEachId), StringComparer.Ordinal);
        for (int i = 1; ; i++)
        {
            var id = "F" + i;
            if (!taken.Contains(id)) return id;
        }
    }

    /// <summary>ForEach counterpart to <see cref="OpenLoopId"/> — innermost unclosed ForEach.</summary>
    private string OpenForEachId()
    {
        if (SelectedScenario == null) return "";
        var stack = new Stack<string>();
        foreach (var s in SelectedScenario.Steps)
        {
            if (s.Kind == ScenarioStepKind.ForEach && !string.IsNullOrEmpty(s.ForEachId))
                stack.Push(s.ForEachId);
            else if (s.Kind == ScenarioStepKind.EndForEach && stack.Count > 0)
                stack.Pop();
        }
        return stack.Count > 0 ? stack.Peek() : "";
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
        SelectedScenario.RemoveLayoutOverrideAndShift(idx);
        SelectedStep = SelectedScenario.Steps.Count > 0
            ? SelectedScenario.Steps[Math.Min(idx, SelectedScenario.Steps.Count - 1)]
            : null;
    }

    [RelayCommand]
    private void MoveStepUp()
    {
        if (SelectedScenario == null || SelectedStep == null) return;
        var idx = SelectedScenario.Steps.IndexOf(SelectedStep);
        if (idx > 0)
        {
            var moved = SelectedStep;
            SelectedScenario.Steps.Move(idx, idx - 1);
            SelectedScenario.SwapLayoutOverrides(idx, idx - 1);
            // ListBox 双向绑定在 Move 时会把 SelectedItem 短暂写回 null，
            // 这里把选中项恢复回那个被移动的步骤，连续按 ↑ 才不需要再点一次。
            SelectedStep = moved;
        }
    }

    [RelayCommand]
    private void MoveStepDown()
    {
        if (SelectedScenario == null || SelectedStep == null) return;
        var idx = SelectedScenario.Steps.IndexOf(SelectedStep);
        if (idx < SelectedScenario.Steps.Count - 1)
        {
            var moved = SelectedStep;
            SelectedScenario.Steps.Move(idx, idx + 1);
            SelectedScenario.SwapLayoutOverrides(idx, idx + 1);
            SelectedStep = moved;
        }
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

    [RelayCommand]
    private void AddTriggerCondition()
    {
        if (SelectedScenario == null) return;
        var cond = new FieldConditionViewModel { Path = "1/0", Value = "" };
        // 把当前触发模板的字段树灌进新条件，让"字段"下拉立即可用——否则要等用户重新选一次模板才会有内容。
        var tplName = SelectedScenario.TriggerTemplateName;
        if (string.IsNullOrEmpty(tplName) && SelectedScenario.TriggerStream != 0)
        {
            // 没显式选模板但 S/F 已填 → 反查模板名兜底
            var tpl = FindTemplateByStreamFunction(SelectedScenario.TriggerStream, SelectedScenario.TriggerFunction);
            if (tpl != null) tplName = tpl.Name;
        }
        if (!string.IsNullOrEmpty(tplName))
            foreach (var f in ExtractTemplateFields(tplName))
                cond.TemplateFields.Add(f);
        SelectedScenario.TriggerConditions.Add(cond);
    }

    [RelayCommand]
    private void DeleteTriggerCondition(FieldConditionViewModel? cond)
    {
        if (cond == null || SelectedScenario == null) return;
        SelectedScenario.TriggerConditions.Remove(cond);
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
    /// Resolve a user's partial Host message input to a single known template name. AutoCompleteBox
    /// filters visually, but Text binding still writes the raw typed text; this lets typing a unique
    /// tail such as "LotEnd" commit to "MESLOTEND_MES_LotEnd" so HostSend can find the template.
    /// </summary>
    public string? ResolveUniqueHostMessageName(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var exact = HostMessageNames.FirstOrDefault(n => string.Equals(n, text, StringComparison.Ordinal));
        if (exact != null) return exact;

        var matches = HostMessageNames
            .Where(n => n.Contains(text, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
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
    /// scenario steps; pass the protocol's SendSecsMessageAsync. Optional host parameters
    /// enable HostSend/HostReceive scenario steps when the Host protocol is wired up.
    /// </summary>
    public void ApplyToRouter(MessageRouter router, Microsoft.Extensions.Logging.ILogger logger,
        Func<SecsMessage, CancellationToken, Task>? send = null,
        Func<string, EAPSimulator.Core.Protocols.HostProtocol.HostMessageTemplate?>? hostTemplateLookup = null,
        Func<EAPSimulator.Core.Protocols.HostProtocol.HostMessage, CancellationToken, Task>? hostSend = null)
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
        // Sub-scenario lookup: by name, materialized from the current Scenarios list at call time.
        var engine = new ScenarioEngine(logger, FindTemplateByName, send, CurrentRole,
            hostTemplateLookup, hostSend,
            name => Scenarios.FirstOrDefault(s => s.Name == name)?.ToModel());
        engine.Log += text =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => StatusMessage = text);
        engine.ScenarioFinished += (sc, status) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => RunStatus = $"{sc.Name}: {status}");
        router.SetScenarioEngine(engine);
        _activeEngine = engine;

        // Register message-triggered scenarios. 引擎空闲时收到匹配消息会自动 Start。
        engine.SetTriggerScenarios(
            Scenarios.Where(s => s.Enabled && s.TriggerOnMessage
                                 && ScenarioEngine.RoleAllows(s.Role, CurrentRole))
                     .Select(s => s.ToModel()));

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

    /// <summary>
    /// Attach Host capability to the currently active scenario engine after it's been built —
    /// used when the SECS protocol starts before the Host transport. The <paramref name="subscribe"/>
    /// callback is invoked so the caller can register the engine's <c>OnHostMessageReceived</c>
    /// against <c>HostProtocol.HostMessageReceived</c>.
    /// </summary>
    public void AttachHostToScenarioEngine(
        Func<string, EAPSimulator.Core.Protocols.HostProtocol.HostMessageTemplate?> hostTemplateLookup,
        Func<EAPSimulator.Core.Protocols.HostProtocol.HostMessage, CancellationToken, Task> hostSend,
        Action<EventHandler<EAPSimulator.Core.Protocols.HostProtocol.HostMessage>> subscribe)
    {
        if (_activeEngine == null) return;
        _activeEngine.AttachHost(hostTemplateLookup, hostSend);
        subscribe((_, msg) => _activeEngine.OnHostMessageReceived(msg));
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

    /// <summary>
    /// Expression-mode body. When non-empty, the engine ignores Path/Operator/Value and
    /// runs this string through <see cref="ScenarioExpression"/>. UI shows a separate
    /// "expression" row that swaps in for the legacy three-field row when in expression mode.
    /// </summary>
    [ObservableProperty]
    private string _expression = "";

    [ObservableProperty]
    private FieldOption? _selectedFieldOption;

    /// <summary>
    /// Available fields from the selected trigger template, for dropdown selection.
    /// </summary>
    public ObservableCollection<FieldOption> TemplateFields { get; } = [];

    public string[] Operators => FieldCondition.SupportedOperators;

    /// <summary>True when <see cref="Expression"/> is non-empty — UI uses this to swap rows.</summary>
    public bool IsExpressionMode => !string.IsNullOrWhiteSpace(Expression);
    public bool IsLegacyMode => !IsExpressionMode;

    partial void OnExpressionChanged(string value)
    {
        OnPropertyChanged(nameof(IsExpressionMode));
        OnPropertyChanged(nameof(IsLegacyMode));
    }

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

    public FieldCondition ToModel() => new()
    {
        Path = Path,
        Operator = Operator,
        Value = Value,
        Expression = Expression,
    };

    public static FieldConditionViewModel FromModel(FieldCondition cond) => new()
    {
        Path = cond.Path,
        Operator = cond.Operator ?? "==",
        Value = cond.Value,
        Expression = cond.Expression ?? "",
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
    /// 当为 true 时，引擎空闲且收到 (TriggerStream,TriggerFunction) 命中且所有 TriggerConditions 匹配
    /// 的入站 SECS 消息时，自动 Start 本场景。典型用法：S6F11 + CEID==某值 → 跑对应处理流程。
    /// </summary>
    [ObservableProperty]
    private bool _triggerOnMessage;

    [ObservableProperty]
    private byte _triggerStream;

    [ObservableProperty]
    private byte _triggerFunction;

    /// <summary>
    /// 触发模板选择 UI 用 — 选中一个模板名后自动把 <see cref="TriggerStream"/>/<see cref="TriggerFunction"/>
    /// 同步过来；同时把模板字段树灌进 <see cref="TriggerConditions"/> 的 TemplateFields，使条件可以下拉选路径。
    /// 不参与序列化（仅 UI 辅助）。
    /// </summary>
    [ObservableProperty]
    private string _triggerTemplateName = "";

    partial void OnTriggerTemplateNameChanged(string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        var parent = AutoReplyViewModel.Instance;
        var tpl = parent?.FindTemplateByName(value);
        if (tpl == null)
        {
            // 用户还在输入中（AutoCompleteBox 的 Text 在每次按键时都会回写），
            // 名字尚未对上某个模板就别动 S/F 和已经填好的 TemplateFields——否则会被空列表覆盖。
            return;
        }
        _triggerTemplateAuthoritative = true;
        TriggerStream = tpl.Stream;
        TriggerFunction = tpl.Function;
        _triggerTemplateAuthoritative = false;
        parent?.PopulateTemplateFields(TriggerConditions, value);
    }

    /// <summary>
    /// True 表示 S/F 当前由模板选择驱动，OnTriggerStream/FunctionChanged 不应反过来清空模板名。
    /// 用户手动改 S/F 时这个标志为 false，模板名会被清掉以避免显示和实际不一致。
    /// </summary>
    private bool _triggerTemplateAuthoritative;

    partial void OnTriggerStreamChanged(byte value)
    {
        if (_triggerTemplateAuthoritative) return;
        // 手动改 S/F → 取消"已选模板"的视觉绑定，避免模板名和实际 S/F 对不上。
        TriggerTemplateName = "";
    }

    partial void OnTriggerFunctionChanged(byte value)
    {
        if (_triggerTemplateAuthoritative) return;
        TriggerTemplateName = "";
    }

    public ObservableCollection<FieldConditionViewModel> TriggerConditions { get; } = [];

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

    /// <summary>
    /// Per-step (x, y) overrides for the flow canvas, keyed by step index. Mutated when the user
    /// drags a node; persisted to/from <see cref="ScenarioFlowPersistedLayout"/> via
    /// <see cref="ToModel"/> / <see cref="FromModel"/>. Empty = canvas falls back to its column
    /// default and the JSON gets no <c>layout</c> key — old files round-trip unchanged.
    /// </summary>
    public Dictionary<int, (double X, double Y)> LayoutOverrides { get; } = new();

    /// <summary>
    /// Build a lightweight <see cref="ScenarioDefinition"/> snapshot for the flow canvas — only
    /// the fields the layout engine reads. Avoids the full <see cref="ToModel"/> cost on every
    /// step-property change while still letting <see cref="ScenarioFlowLayout"/> see fresh data.
    /// </summary>
    public ScenarioDefinition ToModelLayoutPreview() => ToModel();

    /// <summary>
    /// Shift every layout override at or after <paramref name="fromIndex"/> by <paramref name="delta"/>.
    /// Call BEFORE inserting (with +1) so the new index slot is empty, or AFTER deleting
    /// (with -1) so the gap closes. Without this the dragged positions stay attached to
    /// step indices that no longer exist (or wrong ones).
    /// </summary>
    public void ShiftLayoutOverrides(int fromIndex, int delta)
    {
        if (LayoutOverrides.Count == 0 || delta == 0) return;
        var keys = LayoutOverrides.Keys.Where(k => k >= fromIndex).OrderBy(k => -delta).ToList();
        // Sort direction matters: when delta=+1 process highest first so we don't overwrite
        // a pending key. When delta=-1 process lowest first for the same reason.
        if (delta > 0) keys.Sort((a, b) => b.CompareTo(a));
        else           keys.Sort();
        foreach (var k in keys)
        {
            var v = LayoutOverrides[k];
            LayoutOverrides.Remove(k);
            LayoutOverrides[k + delta] = v;
        }
    }

    /// <summary>Remove the override for <paramref name="removedIndex"/> and shift the tail down.</summary>
    public void RemoveLayoutOverrideAndShift(int removedIndex)
    {
        LayoutOverrides.Remove(removedIndex);
        ShiftLayoutOverrides(removedIndex + 1, -1);
    }

    /// <summary>Swap the overrides of two adjacent steps (used by Move Up / Down).</summary>
    public void SwapLayoutOverrides(int a, int b)
    {
        var hasA = LayoutOverrides.TryGetValue(a, out var va);
        var hasB = LayoutOverrides.TryGetValue(b, out var vb);
        LayoutOverrides.Remove(a);
        LayoutOverrides.Remove(b);
        if (hasA) LayoutOverrides[b] = va;
        if (hasB) LayoutOverrides[a] = vb;
    }

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
            RefreshTriggerWarning();
        };
    }

    private void OnStepPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScenarioStepViewModel.Label))
            RefreshAvailableLabels();
        // 首步 Kind 变了可能让"触发但首步不是 Receive"的警告生效/消失。
        if (e.PropertyName == nameof(ScenarioStepViewModel.Kind))
            RefreshTriggerWarning();
    }

    partial void OnTriggerOnMessageChanged(bool value) => RefreshTriggerWarning();

    /// <summary>
    /// 触发配置的静态校验提示。返回空串表示没问题；否则一段人话警告。
    /// 当前只检一种常见坑：勾了触发但首步不是 Receive —— 触发消息会进 inbox，
    /// 但若首步直接是 Reply / Send / Branch 等，<c>_lastReceived</c> 仍是 null，
    /// 后续 Reply 步骤会抛错走 OnError。
    /// </summary>
    public string TriggerWarning
    {
        get
        {
            if (!TriggerOnMessage) return "";
            if (Steps.Count == 0)
                return "⚠ 已勾选触发但场景没有步骤";
            var first = Steps[0];
            if (first.Kind != ScenarioStepKind.Receive)
                return $"⚠ 首步是 {first.Kind}，建议改为 Receive。否则触发消息无法绑定到 _lastReceived，" +
                       "后续 Reply 会失败。";
            return "";
        }
    }

    public bool HasTriggerWarning => !string.IsNullOrEmpty(TriggerWarning);

    private void RefreshTriggerWarning()
    {
        OnPropertyChanged(nameof(TriggerWarning));
        OnPropertyChanged(nameof(HasTriggerWarning));
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
        var def = new ScenarioDefinition
        {
            Name = Name,
            Description = Description,
            Role = Role,
            Enabled = Enabled,
            Loop = Loop,
            AutoStart = AutoStart,
            TriggerOnMessage = TriggerOnMessage,
            TriggerStream = TriggerStream,
            TriggerFunction = TriggerFunction,
            TriggerConditions = TriggerConditions.Select(c => c.ToModel()).ToList(),
            Steps = Steps.Select(s => s.ToModel()).ToList(),
        };
        if (LayoutOverrides.Count > 0)
        {
            def.Layout = new ScenarioFlowPersistedLayout
            {
                Nodes = LayoutOverrides
                    .Select(kv => new ScenarioFlowPersistedNode { StepIndex = kv.Key, X = kv.Value.X, Y = kv.Value.Y })
                    .OrderBy(n => n.StepIndex)
                    .ToList(),
            };
        }
        return def;
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
            TriggerOnMessage = def.TriggerOnMessage,
            TriggerStream = def.TriggerStream,
            TriggerFunction = def.TriggerFunction,
        };
        foreach (var c in def.TriggerConditions)
            vm.TriggerConditions.Add(FieldConditionViewModel.FromModel(c));
        // 反查触发模板名，让 AutoCompleteBox 加载场景后能显示已选模板；同时把字段树灌进
        // 已有的 TriggerConditions（来自 JSON 反序列化的条件 TemplateFields 是空的）。
        if (def.TriggerOnMessage && (def.TriggerStream != 0 || def.TriggerFunction != 0))
        {
            var parent = AutoReplyViewModel.Instance;
            var tpl = parent?.FindTemplateByStreamFunction(def.TriggerStream, def.TriggerFunction);
            if (tpl != null)
            {
                vm._triggerTemplateAuthoritative = true;
                vm.TriggerTemplateName = tpl.Name;
                vm._triggerTemplateAuthoritative = false;
            }
        }
        foreach (var step in def.Steps)
            vm.Steps.Add(ScenarioStepViewModel.FromModel(step));
        if (def.Layout?.Nodes != null)
        {
            foreach (var n in def.Layout.Nodes)
                vm.LayoutOverrides[n.StepIndex] = (n.X, n.Y);
        }
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

    /// <summary>
    /// Debugger flag — true marks this step as a breakpoint. NOT serialized (transient debug
    /// state, not part of the scenario contract). Pushed into the engine's Breakpoints set
    /// on Run and on each toggle while paused.
    /// </summary>
    [ObservableProperty]
    private bool _isBreakpoint;

    // Receive
    [ObservableProperty]
    private byte _stream;

    [ObservableProperty]
    private byte _function;

    [ObservableProperty]
    private int _timeoutMs = 30_000;

    [ObservableProperty]
    private ReceiveTimeoutAction _onTimeout = ReceiveTimeoutAction.Fail;

    /// <summary>
    /// Optional label to jump to when the step throws (timeout, IO failure, missing template,
    /// unknown sub-scenario, expression error, …). Empty = fail the scenario the legacy way.
    /// </summary>
    [ObservableProperty]
    private string _onErrorLabel = "";

    public ObservableCollection<FieldConditionViewModel> Conditions { get; } = [];

    // Delay
    [ObservableProperty]
    private int _delayMs = 1_000;

    // Log
    [ObservableProperty]
    private string _message = "";

    // HostSend / HostReceive
    [ObservableProperty]
    private string _hostMessageName = "";

    [ObservableProperty]
    private string _hostChannelName = "";

    // SetVariable
    [ObservableProperty]
    private string _variableName = "";

    [ObservableProperty]
    private VariableSource _variableSource = VariableSource.Literal;

    [ObservableProperty]
    private string _variablePath = "";

    [ObservableProperty]
    private string _literalValue = "";

    // Loop / EndLoop
    [ObservableProperty]
    private string _loopId = "";

    [ObservableProperty]
    private int _loopTimes = 0;

    [ObservableProperty]
    private string _loopWhile = "";

    // CallScenario
    [ObservableProperty]
    private string _subScenarioName = "";

    // ForEach / EndForEach
    [ObservableProperty]
    private string _forEachId = "";

    [ObservableProperty]
    private ForEachSource _forEachSource = ForEachSource.SecsList;

    [ObservableProperty]
    private string _forEachPath = "";

    [ObservableProperty]
    private string _forEachItemVariable = "";

    [ObservableProperty]
    private string _forEachSeparator = ",";

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
    public bool IsHostSendKind => Kind == ScenarioStepKind.HostSend;
    public bool IsHostReceiveKind => Kind == ScenarioStepKind.HostReceive;
    public bool IsSetVariableKind => Kind == ScenarioStepKind.SetVariable;
    public bool IsLoopKind => Kind == ScenarioStepKind.Loop;
    public bool IsEndLoopKind => Kind == ScenarioStepKind.EndLoop;
    public bool IsCallScenarioKind => Kind == ScenarioStepKind.CallScenario;
    public bool IsForEachKind => Kind == ScenarioStepKind.ForEach;
    public bool IsEndForEachKind => Kind == ScenarioStepKind.EndForEach;
    public bool UsesTemplate => IsSendKind || IsReplyKind;
    public bool UsesHostMessage => IsHostSendKind || IsHostReceiveKind;

    /// <summary>Branch cases (used only when Kind == Branch).</summary>
    public ObservableCollection<BranchCaseViewModel> Cases { get; } = [];

    [ObservableProperty]
    private string _defaultLabel = "";

    /// <summary>Set by the parent ScenarioViewModel so each case row can pick a target label from the same scenario.</summary>
    public ObservableCollection<string> AvailableLabels { get; } = [];

    public string[] KindNames => Enum.GetNames<ScenarioStepKind>();
    public string[] OnTimeoutNames => Enum.GetNames<ReceiveTimeoutAction>();
    public string[] VariableSourceNames => Enum.GetNames<VariableSource>();
    public string[] ForEachSourceNames => Enum.GetNames<ForEachSource>();

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

    /// <summary>String adapter for binding <see cref="VariableSource"/> to a ComboBox of strings.</summary>
    public string VariableSourceName
    {
        get => VariableSource.ToString();
        set
        {
            if (Enum.TryParse<VariableSource>(value, true, out var vs))
                VariableSource = vs;
        }
    }

    /// <summary>String adapter for binding <see cref="ForEachSource"/> to a ComboBox of strings.</summary>
    public string ForEachSourceName
    {
        get => ForEachSource.ToString();
        set
        {
            if (Enum.TryParse<ForEachSource>(value, true, out var fs))
                ForEachSource = fs;
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
        OnPropertyChanged(nameof(IsHostSendKind));
        OnPropertyChanged(nameof(IsHostReceiveKind));
        OnPropertyChanged(nameof(IsSetVariableKind));
        OnPropertyChanged(nameof(IsLoopKind));
        OnPropertyChanged(nameof(IsEndLoopKind));
        OnPropertyChanged(nameof(IsCallScenarioKind));
        OnPropertyChanged(nameof(IsForEachKind));
        OnPropertyChanged(nameof(IsEndForEachKind));
        OnPropertyChanged(nameof(UsesTemplate));
        OnPropertyChanged(nameof(UsesHostMessage));
        OnPropertyChanged(nameof(KindName));
        UpdateDisplayText();
    }

    partial void OnOnTimeoutChanged(ReceiveTimeoutAction value)
        => OnPropertyChanged(nameof(OnTimeoutName));

    partial void OnVariableSourceChanged(VariableSource value)
    {
        OnPropertyChanged(nameof(VariableSourceName));
        UpdateDisplayText();
    }

    partial void OnStreamChanged(byte value) => UpdateDisplayText();
    partial void OnFunctionChanged(byte value) => UpdateDisplayText();
    partial void OnDelayMsChanged(int value) => UpdateDisplayText();
    partial void OnMessageChanged(string value) => UpdateDisplayText();
    partial void OnLabelChanged(string value) => UpdateDisplayText();
    partial void OnHostMessageNameChanged(string value)
    {
        UpdateDisplayText();
        if (_hostMessageNameAuthoritative) return;
        if (string.IsNullOrWhiteSpace(value)) return;

        var parent = AutoReplyViewModel.Instance;
        var match = parent?.ResolveUniqueHostMessageName(value);
        if (match == null || string.Equals(match, value, StringComparison.Ordinal)) return;

        _hostMessageNameAuthoritative = true;
        HostMessageName = match;
        _hostMessageNameAuthoritative = false;
    }

    private bool _hostMessageNameAuthoritative;
    partial void OnHostChannelNameChanged(string value) => UpdateDisplayText();
    partial void OnVariableNameChanged(string value) => UpdateDisplayText();
    partial void OnVariablePathChanged(string value) => UpdateDisplayText();
    partial void OnLiteralValueChanged(string value) => UpdateDisplayText();
    partial void OnLoopIdChanged(string value) => UpdateDisplayText();
    partial void OnLoopTimesChanged(int value) => UpdateDisplayText();
    partial void OnLoopWhileChanged(string value) => UpdateDisplayText();
    partial void OnSubScenarioNameChanged(string value) => UpdateDisplayText();
    partial void OnForEachIdChanged(string value) => UpdateDisplayText();
    partial void OnForEachSourceChanged(ForEachSource value)
    {
        OnPropertyChanged(nameof(ForEachSourceName));
        UpdateDisplayText();
    }
    partial void OnForEachPathChanged(string value) => UpdateDisplayText();
    partial void OnForEachItemVariableChanged(string value) => UpdateDisplayText();
    partial void OnForEachSeparatorChanged(string value) => UpdateDisplayText();
    partial void OnOnErrorLabelChanged(string value) => UpdateDisplayText();

    partial void OnTemplateNameChanged(string value)
    {
        UpdateDisplayText();
        if (string.IsNullOrEmpty(value)) return;

        var parent = AutoReplyViewModel.Instance;
        var tpl = parent?.FindTemplateByName(value);
        // tpl == null 说明用户还在 AutoCompleteBox 里逐字输入，没对上有效模板；
        // 这种中间态别动 S/F 和 TemplateFields，否则会被空列表覆盖（参考触发模板的同类保护）。
        if (tpl == null) return;
        if (Kind == ScenarioStepKind.Receive)
        {
            // For a Receive step using a template as a shape hint, copy S/F.
            Stream = tpl.Stream;
            Function = tpl.Function;
            // Populate condition field options from the template body so the user
            // can pick paths via the dropdown.
            parent?.PopulateTemplateFields(Conditions, value);
        }
    }

    public void UpdateDisplayText()
    {
        var label = string.IsNullOrEmpty(Label) ? "" : $" — {Label}";
        var onErr = string.IsNullOrEmpty(OnErrorLabel) ? "" : $"  ⚠→{OnErrorLabel}";
        DisplayText = (Kind switch
        {
            ScenarioStepKind.Send => $"▶ Send {(string.IsNullOrEmpty(TemplateName) ? "(未设置)" : TemplateName)}{label}",
            ScenarioStepKind.Receive => BuildReceiveDisplay() + label,
            ScenarioStepKind.Reply => $"↩ Reply {(string.IsNullOrEmpty(TemplateName) ? "(未设置)" : TemplateName)}{label}",
            ScenarioStepKind.Delay => $"⏱ Delay {DelayMs} ms{label}",
            ScenarioStepKind.Log => $"📝 Log {Message}{label}",
            ScenarioStepKind.Branch => BuildBranchDisplay() + label,
            ScenarioStepKind.HostSend => $"▶ HostSend {(string.IsNullOrEmpty(HostMessageName) ? "(未设置)" : HostMessageName)}{label}",
            ScenarioStepKind.HostReceive => BuildHostReceiveDisplay() + label,
            ScenarioStepKind.SetVariable => BuildSetVariableDisplay() + label,
            ScenarioStepKind.Loop => BuildLoopDisplay() + label,
            ScenarioStepKind.EndLoop => $"⤴ EndLoop {(string.IsNullOrEmpty(LoopId) ? "(无 LoopId)" : LoopId)}{label}",
            ScenarioStepKind.CallScenario => $"⏎ Call {(string.IsNullOrEmpty(SubScenarioName) ? "(未设置)" : SubScenarioName)}{label}",
            ScenarioStepKind.ForEach => BuildForEachDisplay() + label,
            ScenarioStepKind.EndForEach => $"⤴ EndForEach {(string.IsNullOrEmpty(ForEachId) ? "(无 ForEachId)" : ForEachId)}{label}",
            _ => $"? {Kind}{label}",
        }) + onErr;
    }

    private string BuildHostReceiveDisplay()
    {
        var name = string.IsNullOrEmpty(HostMessageName) ? "any" : HostMessageName;
        if (Conditions.Count == 0) return $"◀ HostRecv {name}";
        var conds = string.Join(" & ", Conditions.Select(c => $"[{c.Path}] {c.Operator} {c.Value}"));
        return $"◀ HostRecv {name} where {conds}";
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

    private string BuildSetVariableDisplay()
    {
        var name = string.IsNullOrEmpty(VariableName) ? "?" : VariableName;
        var rhs = VariableSource switch
        {
            VariableSource.Literal => $"\"{LiteralValue}\"",
            VariableSource.LastSecsField => $"secs[{VariablePath}]",
            VariableSource.LastHostField => $"host.{VariablePath}",
            _ => "?",
        };
        return $"𝑥 Set {name} = {rhs}";
    }

    private string BuildLoopDisplay()
    {
        var id = string.IsNullOrEmpty(LoopId) ? "?" : LoopId;
        if (LoopTimes > 0) return $"⟳ Loop {id} × {LoopTimes}";
        if (!string.IsNullOrEmpty(LoopWhile)) return $"⟳ Loop {id} while {LoopWhile}";
        return $"⟳ Loop {id} ∞";
    }

    private string BuildForEachDisplay()
    {
        var id = string.IsNullOrEmpty(ForEachId) ? "?" : ForEachId;
        var src = ForEachSource switch
        {
            ForEachSource.SecsList => $"secs[{(string.IsNullOrEmpty(ForEachPath) ? "*" : ForEachPath)}]",
            ForEachSource.HostArrayList => $"host[{(string.IsNullOrEmpty(ForEachPath) ? "*" : ForEachPath)}]",
            ForEachSource.Variable => $"split(${{{ForEachPath}}}, \"{ForEachSeparator}\")",
            _ => "?",
        };
        var alias = string.IsNullOrEmpty(ForEachItemVariable) ? "" : $" as ${ForEachItemVariable}";
        return $"⟳⃗ ForEach {id} ← {src}{alias}";
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
            HostMessageName = HostMessageName,
            HostChannelName = HostChannelName,
            Cases = Cases.Select(c => c.ToModel()).ToList(),
            DefaultLabel = DefaultLabel,
            VariableName = VariableName,
            VariableSource = VariableSource,
            VariablePath = VariablePath,
            LiteralValue = LiteralValue,
            LoopId = LoopId,
            LoopTimes = LoopTimes,
            LoopWhile = LoopWhile,
            SubScenarioName = SubScenarioName,
            ForEachId = ForEachId,
            ForEachSource = ForEachSource,
            ForEachPath = ForEachPath,
            ForEachItemVariable = ForEachItemVariable,
            ForEachSeparator = ForEachSeparator,
            OnErrorLabel = OnErrorLabel,
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
            HostMessageName = step.HostMessageName,
            HostChannelName = step.HostChannelName,
            DefaultLabel = step.DefaultLabel,
            VariableName = step.VariableName,
            VariableSource = step.VariableSource,
            VariablePath = step.VariablePath,
            LiteralValue = step.LiteralValue,
            LoopId = step.LoopId,
            LoopTimes = step.LoopTimes,
            LoopWhile = step.LoopWhile,
            SubScenarioName = step.SubScenarioName,
            ForEachId = step.ForEachId,
            ForEachSource = step.ForEachSource,
            ForEachPath = step.ForEachPath,
            ForEachItemVariable = step.ForEachItemVariable,
            ForEachSeparator = string.IsNullOrEmpty(step.ForEachSeparator) ? "," : step.ForEachSeparator,
            OnErrorLabel = step.OnErrorLabel,
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


/// <summary>
/// One row in the debugger's variable-watch panel: variable name + its string value at the
/// moment of the latest pause. Plain data — the panel binds an ObservableCollection of these
/// directly, no per-row notifications needed since the whole list is rebuilt on each pause.
/// </summary>
public record DebuggerVariableRow(string Name, string Value);

