namespace EAPSimulator.Core.Protocols.SecsGem.Gem;

public class StatusVariable
{
    public ushort Svid { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
}

public class EquipmentConstant
{
    public ushort Cecid { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string DefaultValue { get; set; } = string.Empty;
}

public class CollectionEvent
{
    public ushort Ceid { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public List<ushort> ReportLinks { get; set; } = new();
}

public class DataReport
{
    public ushort Rptid { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<ushort> Variables { get; set; } = new();
}

public class Alarm
{
    public ushort Alid { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsSet { get; set; }
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Holds the complete GEM equipment state: status variables, alarms, collection events, etc.
/// </summary>
public class EquipmentModel
{
    public string EquipmentId { get; set; } = "EQUIP001";
    public string ModelName { get; set; } = "EAPSim";
    public string SoftwareRevision { get; set; } = "1.00";

    /// <summary>
    /// Controls whether S1F13 handler replies ACK (true) or NACK (false).
    /// Toggle this to simulate communication rejection scenarios.
    /// </summary>
    public bool AcceptCommunication { get; set; } = true;

    public GemStateMachine GemStateMachine { get; } = new();

    /// <summary>
    /// CNTRS (Control State) VID - GEM control state indicator.
    /// 1=Offline/machine offline, 2=Offline/online attempt,
    /// 3=Offline/host offline, 4=Online/local, 5=Online/remote
    /// </summary>
    public ushort Cntrs { get; set; } = 1;

    public List<StatusVariable> StatusVariables { get; set; } = new()
    {
        new StatusVariable { Svid = 1001, Name = "Temperature", Value = "25.0", Unit = "C" },
        new StatusVariable { Svid = 1002, Name = "Pressure", Value = "101.3", Unit = "kPa" },
        new StatusVariable { Svid = 1003, Name = "RecipeName", Value = "DEFAULT", Unit = "" },
        new StatusVariable { Svid = 1004, Name = "CNTRS", Value = "1", Unit = "" },
    };

    public List<EquipmentConstant> EquipmentConstants { get; set; } = new()
    {
        new EquipmentConstant { Cecid = 2001, Name = "DefaultRecipe", Value = "RECIPE01", DefaultValue = "RECIPE01" },
        new EquipmentConstant { Cecid = 2002, Name = "MaxWafers", Value = "25", DefaultValue = "25" },
    };

    public List<CollectionEvent> CollectionEvents { get; set; } = new()
    {
        new CollectionEvent { Ceid = 101, Name = "ProcessStart" },
        new CollectionEvent { Ceid = 102, Name = "ProcessEnd" },
        new CollectionEvent { Ceid = 103, Name = "WaferArrived" },
        new CollectionEvent { Ceid = 104, Name = "WaferRemoved" },
        new CollectionEvent { Ceid = 201, Name = "ControlStateChange", ReportLinks = { 2001 } },
    };

    public List<Alarm> Alarms { get; set; } = new()
    {
        new Alarm { Alid = 1, Name = "OverTemperature", Description = "Temperature exceeds limit" },
        new Alarm { Alid = 2, Name = "LowPressure", Description = "Pressure below minimum" },
    };

    public StatusVariable? GetStatusVariable(ushort svid) =>
        StatusVariables.FirstOrDefault(sv => sv.Svid == svid);

    public EquipmentConstant? GetEquipmentConstant(ushort cecid) =>
        EquipmentConstants.FirstOrDefault(ec => ec.Cecid == cecid);

    public Alarm? GetAlarm(ushort alid) =>
        Alarms.FirstOrDefault(a => a.Alid == alid);

    /// <summary>
    /// Update CNTRS based on current GEM state.
    /// </summary>
    public void UpdateCntrs(GemState state)
    {
        Cntrs = state switch
        {
            GemState.Offline => 1,
            GemState.AttemptOnline => 2,
            GemState.OnlineLocal => 4,
            GemState.OnlineRemote => 5,
            _ => 1
        };
        // Update the status variable value
        var sv = GetStatusVariable(1004);
        if (sv != null) sv.Value = Cntrs.ToString();
    }

    /// <summary>
    /// Get CNTRS description for display.
    /// </summary>
    public static string GetCntrsDescription(ushort cntrs) => cntrs switch
    {
        1 => "Offline/machine offline",
        2 => "Offline/online establish attempt",
        3 => "Offline/host offline",
        4 => "Online/local",
        5 => "Online/remote",
        _ => "Unknown"
    };
}

