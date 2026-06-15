using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace EAPSimulator.Wpf.ViewModels.FlowCanvas;

public partial class FlowCanvasViewModel : ObservableObject
{
    [ObservableProperty] private FlowDocument _document = new();
    [ObservableProperty] private FlowNodeViewModel? _selectedNode;
    [ObservableProperty] private FlowConnectionViewModel? _selectedConnection;
    [ObservableProperty] private double _zoom = 1.0;
    [ObservableProperty] private double _panOffsetX;
    [ObservableProperty] private double _panOffsetY;
    [ObservableProperty] private string _statusMessage = "";

    private readonly List<FlowNodeViewModel> _selectedNodes = [];

    public IReadOnlyList<FlowNodeViewModel> SelectedNodes => _selectedNodes;
    public UndoRedoManager UndoRedo { get; } = new();

    public event Action? CanvasInvalidationRequested;
    public void InvalidateCanvas() => CanvasInvalidationRequested?.Invoke();

    public void SelectNode(FlowNodeViewModel? node)
    {
        foreach (var n in _selectedNodes) n.IsSelected = false;
        _selectedNodes.Clear();
        if (SelectedConnection != null) SelectedConnection.IsSelected = false;
        SelectedConnection = null;

        if (node != null)
        {
            node.IsSelected = true;
            _selectedNodes.Add(node);
        }
        SelectedNode = node;
        InvalidateCanvas();
    }

    public void SelectConnection(FlowConnectionViewModel? connection)
    {
        foreach (var n in _selectedNodes) n.IsSelected = false;
        _selectedNodes.Clear();
        if (SelectedNode != null) SelectedNode.IsSelected = false;
        SelectedNode = null;
        if (SelectedConnection != null) SelectedConnection.IsSelected = false;

        SelectedConnection = connection;
        if (connection != null) connection.IsSelected = true;
        InvalidateCanvas();
    }

    public void ClearSelection()
    {
        foreach (var n in _selectedNodes) n.IsSelected = false;
        _selectedNodes.Clear();
        if (SelectedNode != null) SelectedNode.IsSelected = false;
        if (SelectedConnection != null) SelectedConnection.IsSelected = false;
        SelectedNode = null;
        SelectedConnection = null;
        InvalidateCanvas();
    }

    public void DeleteSelected()
    {
        if (SelectedNode != null)
        {
            UndoRedo.Execute(new Commands.DeleteNodeCommand(Document, SelectedNode));
            ClearSelection();
        }
        else if (SelectedConnection != null)
        {
            UndoRedo.Execute(new Commands.DeleteConnectionCommand(Document, SelectedConnection));
            ClearSelection();
        }
    }

    [RelayCommand] private void Undo() { UndoRedo.Undo(); InvalidateCanvas(); }
    [RelayCommand] private void Redo() { UndoRedo.Redo(); InvalidateCanvas(); }
    [RelayCommand] private void ResetView() { Zoom = 1.0; PanOffsetX = 0; PanOffsetY = 0; InvalidateCanvas(); }
}
