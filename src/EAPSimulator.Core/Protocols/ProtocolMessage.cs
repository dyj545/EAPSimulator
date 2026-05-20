namespace EAPSimulator.Core.Protocols;

public enum ProtocolRole
{
    Host,
    Equipment
}

public enum ProtocolState
{
    Disconnected,
    Connecting,
    Connected,
    Online,
    Offline
}

public enum MessageDirection
{
    Send,
    Receive
}

public class ProtocolMessageEventArgs : EventArgs
{
    public ProtocolMessage Message { get; }
    public DateTime Timestamp { get; }
    public MessageDirection Direction { get; }

    public ProtocolMessageEventArgs(ProtocolMessage message, MessageDirection direction)
    {
        Message = message;
        Direction = direction;
        Timestamp = DateTime.Now;
    }
}

public class ProtocolStateEventArgs : EventArgs
{
    public ProtocolState OldState { get; }
    public ProtocolState NewState { get; }
    public string? Reason { get; }

    public ProtocolStateEventArgs(ProtocolState oldState, ProtocolState newState, string? reason = null)
    {
        OldState = oldState;
        NewState = newState;
        Reason = reason;
    }
}

public class ProtocolMessage
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, object?> Fields { get; set; } = new();
    public byte[]? RawData { get; set; }
    public string? Description { get; set; }

    public T? GetField<T>(string name)
    {
        return Fields.TryGetValue(name, out var value) && value is T typed ? typed : default;
    }

    public void SetField(string name, object? value) => Fields[name] = value;
}
