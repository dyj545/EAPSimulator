using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EAPSimulator.Wpf.ViewModels.FlowCanvas;

public enum FlowNodeType { Trigger, Action }
public enum SwimLane { Equipment, EAP, Host }

public partial class FlowNodeViewModel : ObservableObject
{
    [ObservableProperty] private string _nodeId = Guid.NewGuid().ToString("N");
    [ObservableProperty] private int _stepIndex;
    [ObservableProperty] private FlowNodeType _nodeType = FlowNodeType.Trigger;
    [ObservableProperty] private SwimLane _lane = SwimLane.EAP;
    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private double _width = 180;
    [ObservableProperty] private double _height = 44;
    [ObservableProperty] private string _label = "";
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isDragging;
    [ObservableProperty] private ScenarioStepViewModel? _stepViewModel;

    private ScenarioStepViewModel? _previousStepVm;

    partial void OnStepViewModelChanged(ScenarioStepViewModel? value)
    {
        if (_previousStepVm != null)
            _previousStepVm.PropertyChanged -= OnStepPropertyChanged;

        _previousStepVm = value;

        if (value != null)
        {
            value.PropertyChanged += OnStepPropertyChanged;
            if (string.IsNullOrEmpty(value.DisplayText))
                value.UpdateDisplayText();
            Label = value.DisplayText;
        }
        else
        {
            Label = "";
        }
    }

    private void OnStepPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScenarioStepViewModel.DisplayText) && StepViewModel != null)
            Label = StepViewModel.DisplayText;
    }
}
