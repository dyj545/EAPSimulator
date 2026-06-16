using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EAPSimulator.Core.Protocols.SecsGem.AutoReply;
using EAPSimulator.Wpf.Models;
using Newtonsoft.Json;

namespace EAPSimulator.Wpf.ViewModels;

public partial class AutoReplyViewModel : ObservableObject
{
    public ObservableCollection<ScenarioViewModel> Scenarios { get; } = [];
    public ObservableCollection<MessageTemplateViewModel> AllMessages { get; } = [];

    [ObservableProperty]
    private ScenarioViewModel? _selectedScenario;

    [ObservableProperty]
    private ScenarioStepViewModel? _selectedStep;

    [ObservableProperty]
    private int _selectedStepIndex = -1;

    [ObservableProperty]
    private string _configPath = "auto_reply_rules.json";

    [ObservableProperty]
    private string _statusMessage = "";

    public FlowCanvas.FlowCanvasViewModel FlowCanvas { get; } = new();

    public void SetTemplates(List<MessageTemplate> templates)
    {
        AllMessages.Clear();
        foreach (var t in templates)
            AllMessages.Add(new MessageTemplateViewModel(t));
    }

    public void LoadConfig(string path)
    {
        ConfigPath = path;
        if (!File.Exists(path)) return;

        try
        {
            var config = AutoReplyConfig.LoadFromFile(path);
            Scenarios.Clear();
            foreach (var s in config.Scenarios)
                Scenarios.Add(ScenarioViewModel.FromModel(s));

            if (Scenarios.Count > 0)
                SelectedScenario = Scenarios[0];
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private void AddScenario()
    {
        var vm = new ScenarioViewModel { Name = $"场景 {Scenarios.Count + 1}" };
        Scenarios.Add(vm);
        SelectedScenario = vm;
    }

    [RelayCommand]
    private void DeleteScenario()
    {
        if (SelectedScenario != null)
        {
            Scenarios.Remove(SelectedScenario);
            SelectedScenario = Scenarios.FirstOrDefault();
        }
    }

    [RelayCommand]
    private void AddStep()
    {
        if (SelectedScenario == null) return;
        var step = new ScenarioStepViewModel();
        SelectedScenario.Steps.Add(step);
        SelectedStep = step;
    }

    [RelayCommand]
    private void DeleteStep()
    {
        if (SelectedScenario == null || SelectedStep == null) return;
        var idx = SelectedScenario.Steps.IndexOf(SelectedStep);
        SelectedScenario.Steps.Remove(SelectedStep);
        SelectedStep = SelectedScenario.Steps.FirstOrDefault(s =>
            SelectedScenario.Steps.IndexOf(s) == Math.Min(idx, SelectedScenario.Steps.Count - 1));
    }

    [RelayCommand]
    private void MoveStepUp()
    {
        if (SelectedScenario == null || SelectedStep == null) return;
        var idx = SelectedScenario.Steps.IndexOf(SelectedStep);
        if (idx > 0)
        {
            SelectedScenario.Steps.Move(idx, idx - 1);
            SelectedStepIndex = idx - 1;
        }
    }

    [RelayCommand]
    private void MoveStepDown()
    {
        if (SelectedScenario == null || SelectedStep == null) return;
        var idx = SelectedScenario.Steps.IndexOf(SelectedStep);
        if (idx < SelectedScenario.Steps.Count - 1)
        {
            SelectedScenario.Steps.Move(idx, idx + 1);
            SelectedStepIndex = idx + 1;
        }
    }

    [RelayCommand]
    private void SaveConfig()
    {
        try
        {
            var config = new AutoReplyConfig
            {
                Scenarios = Scenarios.Select(s => s.ToModel()).ToList()
            };
            config.SaveToFile(ConfigPath);
            StatusMessage = $"已保存 {Scenarios.Count} 个场景";
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存失败: {ex.Message}";
        }
    }
}

// ===== Scenario ViewModel =====

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

// ===== Scenario Step ViewModel =====

public partial class ScenarioStepViewModel : ObservableObject
{
    [ObservableProperty] private string _nodeId = "";
    [ObservableProperty] private TriggerType _triggerType = TriggerType.SecsMessage;
    [ObservableProperty] private int _triggerTypeIndex;
    [ObservableProperty] private byte _stream;
    [ObservableProperty] private byte _function;
    [ObservableProperty] private string _hostTriggerName = "";
    [ObservableProperty] private string _stateVariableName = "";
    [ObservableProperty] private string _mapperSourceField = "";
    [ObservableProperty] private string _mapperVariable = "";
    [ObservableProperty] private string _judgementVariable = "";
    [ObservableProperty] private string _judgementOperator = "==";
    [ObservableProperty] private string _judgementValue = "";
    [ObservableProperty] private int _judgementTargetStep = -1;
    [ObservableProperty] private ActionType _actionType = ActionType.SecsReply;
    [ObservableProperty] private int _actionTypeIndex;
    [ObservableProperty] private string _actionTemplateName = "";
    [ObservableProperty] private byte _actionStream;
    [ObservableProperty] private byte _actionFunction;
    [ObservableProperty] private string _hostActionName = "";
    [ObservableProperty] private string _stateAlterTarget = "";
    [ObservableProperty] private string _stateAlterValue = "";
    [ObservableProperty] private bool _hostInitiated;
    [ObservableProperty] private string _displayText = "";

    [ObservableProperty] private bool _isSecsTrigger = true;
    [ObservableProperty] private bool _isHostTrigger;
    [ObservableProperty] private bool _isStateTrigger;
    [ObservableProperty] private bool _isMapperTrigger;
    [ObservableProperty] private bool _isJudgementTrigger;
    [ObservableProperty] private bool _isSecsAction = true;
    [ObservableProperty] private bool _isHostAction;
    [ObservableProperty] private bool _isStateAction;
    [ObservableProperty] private bool _isMapperAction;

    public string[] TriggerTypes { get; } = ["SECS 消息", "Host 消息", "设备状态", "数据映射", "条件判断"];
    public string[] ActionTypes { get; } = ["SECS 回复", "Host 消息", "状态修改", "数据映射"];

    partial void OnTriggerTypeChanged(TriggerType value)
    {
        TriggerTypeIndex = (int)value;
        IsSecsTrigger = value == TriggerType.SecsMessage;
        IsHostTrigger = value == TriggerType.HostMessage;
        IsStateTrigger = value == TriggerType.EquipmentState;
        IsMapperTrigger = value == TriggerType.Mapper;
        IsJudgementTrigger = value == TriggerType.Judgement;
        UpdateDisplayText();
    }

    partial void OnTriggerTypeIndexChanged(int value)
    {
        if (value >= 0 && value <= (int)TriggerType.Judgement)
            TriggerType = (TriggerType)value;
    }

    partial void OnActionTypeChanged(ActionType value)
    {
        ActionTypeIndex = (int)value;
        IsSecsAction = value == ActionType.SecsReply;
        IsHostAction = value == ActionType.HostMessage;
        IsStateAction = value == ActionType.StateAlterer;
        IsMapperAction = value == ActionType.Mapper;
        UpdateDisplayText();
    }

    partial void OnActionTypeIndexChanged(int value)
    {
        if (value >= 0 && value <= (int)ActionType.Mapper)
            ActionType = (ActionType)value;
    }

    partial void OnStreamChanged(byte value) => UpdateDisplayText();
    partial void OnFunctionChanged(byte value) => UpdateDisplayText();
    partial void OnActionTemplateNameChanged(string value) => UpdateDisplayText();
    partial void OnHostTriggerNameChanged(string value) => UpdateDisplayText();
    partial void OnHostActionNameChanged(string value) => UpdateDisplayText();
    partial void OnStateVariableNameChanged(string value) => UpdateDisplayText();
    partial void OnMapperSourceFieldChanged(string value) => UpdateDisplayText();
    partial void OnMapperVariableChanged(string value) => UpdateDisplayText();
    partial void OnJudgementVariableChanged(string value) => UpdateDisplayText();

    public void UpdateDisplayText()
    {
        var trigger = TriggerType switch
        {
            TriggerType.SecsMessage => Stream == 0 && Function == 0 ? "(未设置)" : $"S{Stream}F{Function}",
            TriggerType.HostMessage => string.IsNullOrEmpty(HostTriggerName) ? "(未设置)" : $"Host:{HostTriggerName}",
            TriggerType.EquipmentState => string.IsNullOrEmpty(StateVariableName) ? "(未设置)" : $"State:{StateVariableName}",
            TriggerType.Mapper => $"Map:{MapperSourceField}→{MapperVariable}",
            TriggerType.Judgement => $"Judge:{JudgementVariable} {JudgementOperator} {JudgementValue}",
            _ => "(未知)"
        };

        var action = ActionType switch
        {
            ActionType.SecsReply => !string.IsNullOrEmpty(ActionTemplateName) ? ActionTemplateName : "(未设置)",
            ActionType.HostMessage => !string.IsNullOrEmpty(HostActionName) ? $"Host:{HostActionName}" : "(未设置)",
            ActionType.StateAlterer => !string.IsNullOrEmpty(StateAlterTarget) ? $"Set:{StateAlterTarget}={StateAlterValue}" : "(未设置)",
            ActionType.Mapper => $"Map:{MapperSourceField}→{MapperVariable}",
            _ => "(未知)"
        };

        DisplayText = $"{trigger} → {action}";
    }

    public ScenarioStep ToModel()
    {
        // WPF UI is legacy; Avalonia UI (EAPSimulator.UI) is canonical.
        // Persist authored fields as a Receive step so the Famate-style engine can at least
        // load it. Round-tripping the legacy trigger/action graph isn't supported here.
        return new ScenarioStep
        {
            Kind = ScenarioStepKind.Receive,
            TemplateName = ActionTemplateName,
            Stream = Stream,
            Function = Function,
        };
    }

    public static ScenarioStepViewModel FromModel(ScenarioStep step)
    {
        var vm = new ScenarioStepViewModel
        {
            Stream = step.Stream,
            Function = step.Function,
            ActionTemplateName = step.TemplateName,
        };
        vm.UpdateDisplayText();
        return vm;
    }
}
