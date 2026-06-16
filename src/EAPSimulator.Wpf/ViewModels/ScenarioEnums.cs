namespace EAPSimulator.Wpf.ViewModels;

public enum TriggerType
{
    SecsMessage = 0,
    HostMessage = 1,
    EquipmentState = 2,
    Mapper = 3,
    Judgement = 4,
}

public enum ActionType
{
    SecsReply = 0,
    HostMessage = 1,
    StateAlterer = 2,
    Mapper = 3,
}
