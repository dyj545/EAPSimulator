namespace EAPSimulator.Wpf.ViewModels.FlowCanvas.Commands;

public class DeleteNodeCommand : FlowCanvas.IUndoableCommand
{
    private readonly FlowDocument _document;
    private readonly FlowNodeViewModel _node;
    private List<FlowConnectionViewModel> _removedConnections = [];
    private int _insertIndex;

    public string Description => $"Delete node {_node.Label}";

    public DeleteNodeCommand(FlowDocument document, FlowNodeViewModel node)
    {
        _document = document;
        _node = node;
    }

    public void Execute()
    {
        _insertIndex = _document.Nodes.IndexOf(_node);
        _removedConnections = _document.Connections
            .Where(c => c.FromNodeId == _node.NodeId || c.ToNodeId == _node.NodeId)
            .ToList();

        foreach (var conn in _removedConnections)
            _document.Connections.Remove(conn);
        _document.Nodes.Remove(_node);
    }

    public void Undo()
    {
        if (_insertIndex >= 0 && _insertIndex <= _document.Nodes.Count)
            _document.Nodes.Insert(_insertIndex, _node);
        else
            _document.Nodes.Add(_node);

        foreach (var conn in _removedConnections)
            _document.Connections.Add(conn);
    }
}
