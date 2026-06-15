namespace EAPSimulator.Wpf.ViewModels.FlowCanvas.Commands;

public class DeleteConnectionCommand : FlowCanvas.IUndoableCommand
{
    private readonly FlowDocument _document;
    private readonly FlowConnectionViewModel _connection;
    private int _insertIndex;

    public string Description => "Delete connection";
    public DeleteConnectionCommand(FlowDocument document, FlowConnectionViewModel connection)
    {
        _document = document;
        _connection = connection;
    }
    public void Execute()
    {
        _insertIndex = _document.Connections.IndexOf(_connection);
        _document.Connections.Remove(_connection);
    }
    public void Undo()
    {
        if (_insertIndex >= 0 && _insertIndex <= _document.Connections.Count)
            _document.Connections.Insert(_insertIndex, _connection);
        else
            _document.Connections.Add(_connection);
    }
}
