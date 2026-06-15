namespace EAPSimulator.Core.EquipmentState;

/// <summary>
/// Equipment state variable (readable value, corresponds to SVID).
/// </summary>
public class StateVariable
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public StateVariableFormat Format { get; set; } = StateVariableFormat.ASCII;
}

/// <summary>
/// Equipment constant (configurable value, corresponds to ECID).
/// </summary>
public class EquipmentConstant
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string DefaultValue { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public StateVariableFormat Format { get; set; } = StateVariableFormat.ASCII;
}

/// <summary>
/// Collection event (corresponds to CEID).
/// </summary>
public class CollectionEvent
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public List<int> ReportLinks { get; set; } = new();
}

/// <summary>
/// Data report (corresponds to RPTID).
/// </summary>
public class DataReport
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<int> VariableIds { get; set; } = new();
}

/// <summary>
/// Alarm definition (corresponds to ALID).
/// </summary>
public class Alarm
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsSet { get; set; }
    public AlarmSeverity Severity { get; set; } = AlarmSeverity.Warning;
}

/// <summary>
/// Equipment state set (e.g., ControlState, ProcessState).
/// </summary>
public class EquipmentStateSet
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CurrentState { get; set; } = string.Empty;
    public List<StateDefinition> States { get; set; } = new();
}

/// <summary>
/// A single state within a state set.
/// </summary>
public class StateDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Data format for state variables and equipment constants.
/// </summary>
public enum StateVariableFormat
{
    ASCII,
    Binary,
    Boolean,
    U1, U2, U4, U8,
    I1, I2, I4, I8,
    F4, F8
}

/// <summary>
/// Alarm severity levels.
/// </summary>
public enum AlarmSeverity
{
    Info,
    Warning,
    Critical
}
