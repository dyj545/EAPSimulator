using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EAPSimulator.Wpf.ViewModels.FlowCanvas;

public enum FlowConnectionType { Sequential, JudgementTrue, JudgementFalse }

public partial class FlowConnectionViewModel : ObservableObject
{
    [ObservableProperty] private string _connectionId = Guid.NewGuid().ToString("N");
    [ObservableProperty] private string _fromNodeId = "";
    [ObservableProperty] private string _toNodeId = "";
    [ObservableProperty] private string _sourceAttachmentId = "";
    [ObservableProperty] private string _targetAttachmentId = "";
    [ObservableProperty] private string _label = "";
    [ObservableProperty] private double _labelX;
    [ObservableProperty] private double _labelY;
    [ObservableProperty] private FlowConnectionType _connectionType = FlowConnectionType.Sequential;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private List<Point> _waypoints = [];
}
