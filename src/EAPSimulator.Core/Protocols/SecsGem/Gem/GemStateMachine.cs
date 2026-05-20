namespace EAPSimulator.Core.Protocols.SecsGem.Gem;

public enum GemState
{
    Offline,
    AttemptOnline,
    OnlineLocal,
    OnlineRemote,
}

public enum GemEvent
{
    StartCommunication,
    CommunicationEstablished,
    CommunicationFailed,
    SwitchToLocal,
    SwitchToRemote,
    GoOffline,
}

/// <summary>
/// GEM communication state machine per SEMI E30.
/// Offline → AttemptOnline → Online(Local) → Online(Remote)
/// </summary>
public class GemStateMachine
{
    private GemState _state = GemState.Offline;
    private readonly object _lock = new();

    public GemState State
    {
        get { lock (_lock) return _state; }
    }

    public event EventHandler<(GemState OldState, GemState NewState, GemEvent Event)>? StateChanged;

    public bool TryTrigger(GemEvent ev)
    {
        lock (_lock)
        {
            var newState = GetNextState(_state, ev);
            if (newState == null) return false;

            var oldState = _state;
            _state = newState.Value;
            StateChanged?.Invoke(this, (oldState, _state, ev));
            return true;
        }
    }

    private static GemState? GetNextState(GemState current, GemEvent ev) => (current, ev) switch
    {
        (GemState.Offline, GemEvent.StartCommunication) => GemState.AttemptOnline,

        (GemState.AttemptOnline, GemEvent.CommunicationEstablished) => GemState.OnlineLocal,
        (GemState.AttemptOnline, GemEvent.CommunicationFailed) => GemState.Offline,

        (GemState.OnlineLocal, GemEvent.SwitchToRemote) => GemState.OnlineRemote,
        (GemState.OnlineLocal, GemEvent.GoOffline) => GemState.Offline,

        (GemState.OnlineRemote, GemEvent.SwitchToLocal) => GemState.OnlineLocal,
        (GemState.OnlineRemote, GemEvent.GoOffline) => GemState.Offline,

        _ => null,
    };
}
