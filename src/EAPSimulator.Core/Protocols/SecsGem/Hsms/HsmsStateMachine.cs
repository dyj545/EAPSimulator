namespace EAPSimulator.Core.Protocols.SecsGem.Hsms;

public enum HsmsState
{
    NotConnected,
    ConnectedNotSelected,
    Selected,
}

public enum HsmsEvent
{
    Connect,
    Disconnect,
    Select,
    SelectTimeout,
    Deselect,
    LinkTestTimeout,
    ReceiveSelectReq,
    ReceiveSelectRsp,
    ReceiveDeselectReq,
    ReceiveDeselectRsp,
    ReceiveLinkTestReq,
    ReceiveLinkTestRsp,
    ReceiveRejectReq,
    ReceiveSeparateReq,
    T7Timeout,
}

public class HsmsStateMachine
{
    private HsmsState _state = HsmsState.NotConnected;

    public HsmsState State => _state;
    public event EventHandler<(HsmsState OldState, HsmsState NewState, HsmsEvent Event)>? StateChanged;

    public bool TryTrigger(HsmsEvent ev)
    {
        var oldState = _state;
        var newState = GetNextState(_state, ev);
        if (newState == null) return false;

        _state = newState.Value;
        StateChanged?.Invoke(this, (oldState, _state, ev));
        return true;
    }

    private static HsmsState? GetNextState(HsmsState current, HsmsEvent ev) => (current, ev) switch
    {
        (HsmsState.NotConnected, HsmsEvent.Connect) => HsmsState.ConnectedNotSelected,

        (HsmsState.ConnectedNotSelected, HsmsEvent.Select) => HsmsState.ConnectedNotSelected,
        (HsmsState.ConnectedNotSelected, HsmsEvent.ReceiveSelectReq) => HsmsState.Selected,
        (HsmsState.ConnectedNotSelected, HsmsEvent.ReceiveSelectRsp) => HsmsState.Selected,
        (HsmsState.ConnectedNotSelected, HsmsEvent.Disconnect) => HsmsState.NotConnected,
        (HsmsState.ConnectedNotSelected, HsmsEvent.SelectTimeout) => HsmsState.NotConnected,
        (HsmsState.ConnectedNotSelected, HsmsEvent.T7Timeout) => HsmsState.NotConnected,
        (HsmsState.ConnectedNotSelected, HsmsEvent.ReceiveRejectReq) => HsmsState.NotConnected,
        (HsmsState.ConnectedNotSelected, HsmsEvent.ReceiveSeparateReq) => HsmsState.NotConnected,

        (HsmsState.Selected, HsmsEvent.ReceiveSeparateReq) => HsmsState.ConnectedNotSelected,
        (HsmsState.Selected, HsmsEvent.Deselect) => HsmsState.ConnectedNotSelected,
        (HsmsState.Selected, HsmsEvent.ReceiveDeselectReq) => HsmsState.ConnectedNotSelected,
        (HsmsState.Selected, HsmsEvent.Disconnect) => HsmsState.NotConnected,
        (HsmsState.Selected, HsmsEvent.T7Timeout) => HsmsState.NotConnected,

        _ => null,
    };
}
