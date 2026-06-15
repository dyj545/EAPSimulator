using CommunityToolkit.Mvvm.ComponentModel;

namespace EAPSimulator.Wpf.ViewModels.FlowCanvas;

public interface IUndoableCommand
{
    string Description { get; }
    void Execute();
    void Undo();
}

public partial class UndoRedoManager : ObservableObject
{
    private readonly Stack<IUndoableCommand> _undoStack = new();
    private readonly Stack<IUndoableCommand> _redoStack = new();
    private const int MaxHistory = 100;

    [ObservableProperty] private bool _canUndo;
    [ObservableProperty] private bool _canRedo;

    public void Execute(IUndoableCommand command)
    {
        command.Execute();
        _undoStack.Push(command);
        if (_undoStack.Count > MaxHistory)
        {
            var arr = _undoStack.ToArray();
            _undoStack.Clear();
            for (int i = arr.Length - 1; i >= arr.Length - MaxHistory; i--)
                _undoStack.Push(arr[i]);
        }
        _redoStack.Clear();
        UpdateCanFlags();
    }

    public void Undo()
    {
        if (_undoStack.Count == 0) return;
        var cmd = _undoStack.Pop();
        cmd.Undo();
        _redoStack.Push(cmd);
        UpdateCanFlags();
    }

    public void Redo()
    {
        if (_redoStack.Count == 0) return;
        var cmd = _redoStack.Pop();
        cmd.Execute();
        _undoStack.Push(cmd);
        UpdateCanFlags();
    }

    private void UpdateCanFlags()
    {
        CanUndo = _undoStack.Count > 0;
        CanRedo = _redoStack.Count > 0;
    }
}
