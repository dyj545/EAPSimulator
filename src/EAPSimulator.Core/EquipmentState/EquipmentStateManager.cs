using System.Text.Json;

namespace EAPSimulator.Core.EquipmentState;

/// <summary>
/// Manages the complete equipment state: status variables, equipment constants,
/// collection events, alarms, and state sets.
/// Replaces hardcoded data in EquipmentModel with a configurable state manager.
/// </summary>
public class EquipmentStateManager
{
    /// <summary>Equipment identifier.</summary>
    public string EquipmentId { get; set; } = "EQUIP001";

    /// <summary>Equipment model name.</summary>
    public string ModelName { get; set; } = "EAPSim";

    /// <summary>Software revision.</summary>
    public string SoftwareRevision { get; set; } = "1.00";

    /// <summary>Whether to accept communication requests.</summary>
    public bool AcceptCommunication { get; set; } = true;

    /// <summary>Status variables (readable values, SVID).</summary>
    public Dictionary<int, StateVariable> StatusVariables { get; } = new();

    /// <summary>Equipment constants (configurable values, ECID).</summary>
    public Dictionary<int, EquipmentConstant> EquipmentConstants { get; } = new();

    /// <summary>Collection events (CEID).</summary>
    public Dictionary<int, CollectionEvent> CollectionEvents { get; } = new();

    /// <summary>Data reports (RPTID).</summary>
    public Dictionary<int, DataReport> DataReports { get; } = new();

    /// <summary>Alarms (ALID).</summary>
    public Dictionary<int, Alarm> Alarms { get; } = new();

    /// <summary>Equipment state sets (e.g., ControlState, ProcessState).</summary>
    public Dictionary<string, EquipmentStateSet> StateSets { get; } = new();

    /// <summary>
    /// Create a default EquipmentStateManager with standard SECS/GEM variables.
    /// </summary>
    public static EquipmentStateManager CreateDefault()
    {
        var manager = new EquipmentStateManager();

        // Status variables
        manager.StatusVariables[1001] = new StateVariable
        {
            Id = 1001, Name = "Temperature", Value = "25.0", Unit = "C",
            Description = "Chamber temperature"
        };
        manager.StatusVariables[1002] = new StateVariable
        {
            Id = 1002, Name = "Pressure", Value = "101.3", Unit = "kPa",
            Description = "Chamber pressure"
        };
        manager.StatusVariables[1003] = new StateVariable
        {
            Id = 1003, Name = "RecipeName", Value = "DEFAULT", Unit = "",
            Description = "Current recipe name"
        };
        manager.StatusVariables[1004] = new StateVariable
        {
            Id = 1004, Name = "CNTRS", Value = "1", Unit = "",
            Description = "Control state indicator (1=Offline, 4=Online/Local, 5=Online/Remote)"
        };

        // Equipment constants
        manager.EquipmentConstants[2001] = new EquipmentConstant
        {
            Id = 2001, Name = "DefaultRecipe", Value = "RECIPE01", DefaultValue = "RECIPE01",
            Description = "Default recipe name"
        };
        manager.EquipmentConstants[2002] = new EquipmentConstant
        {
            Id = 2002, Name = "MaxWafers", Value = "25", DefaultValue = "25",
            Description = "Maximum number of wafers"
        };

        // Collection events
        manager.CollectionEvents[101] = new CollectionEvent
        {
            Id = 101, Name = "ProcessStart", Description = "Process started"
        };
        manager.CollectionEvents[102] = new CollectionEvent
        {
            Id = 102, Name = "ProcessEnd", Description = "Process completed"
        };
        manager.CollectionEvents[103] = new CollectionEvent
        {
            Id = 103, Name = "WaferArrived", Description = "Wafer arrived at station"
        };
        manager.CollectionEvents[104] = new CollectionEvent
        {
            Id = 104, Name = "WaferRemoved", Description = "Wafer removed from station"
        };
        manager.CollectionEvents[201] = new CollectionEvent
        {
            Id = 201, Name = "ControlStateChange", Description = "Control state changed",
            ReportLinks = { 2001 }
        };

        // Data reports
        manager.DataReports[2001] = new DataReport
        {
            Id = 2001, Name = "ControlStateReport",
            Description = "Control state change report",
            VariableIds = { 1004 }
        };

        // Alarms
        manager.Alarms[1] = new Alarm
        {
            Id = 1, Name = "OverTemperature", Description = "Temperature exceeds limit",
            Severity = AlarmSeverity.Critical
        };
        manager.Alarms[2] = new Alarm
        {
            Id = 2, Name = "LowPressure", Description = "Pressure below minimum",
            Severity = AlarmSeverity.Warning
        };

        // State sets
        manager.StateSets["ControlState"] = new EquipmentStateSet
        {
            Name = "ControlState",
            Description = "GEM control state",
            CurrentState = "Offline",
            States =
            {
                new StateDefinition { Name = "Offline", Value = "1", Description = "Equipment offline" },
                new StateDefinition { Name = "AttemptOnline", Value = "2", Description = "Attempting to go online" },
                new StateDefinition { Name = "HostOffline", Value = "3", Description = "Host offline" },
                new StateDefinition { Name = "OnlineLocal", Value = "4", Description = "Online local mode" },
                new StateDefinition { Name = "OnlineRemote", Value = "5", Description = "Online remote mode" },
            }
        };

        manager.StateSets["ProcessState"] = new EquipmentStateSet
        {
            Name = "ProcessState",
            Description = "Equipment process state",
            CurrentState = "Idle",
            States =
            {
                new StateDefinition { Name = "Idle", Value = "IDLE", Description = "Equipment idle" },
                new StateDefinition { Name = "Setup", Value = "SETUP", Description = "Equipment in setup" },
                new StateDefinition { Name = "Ready", Value = "READY", Description = "Equipment ready" },
                new StateDefinition { Name = "Executing", Value = "EXEC", Description = "Executing process" },
                new StateDefinition { Name = "Paused", Value = "PAUSED", Description = "Process paused" },
            }
        };

        return manager;
    }

    /// <summary>
    /// Save state to a JSON file.
    /// </summary>
    public void SaveToFile(string path)
    {
        var data = new EquipmentStateData
        {
            EquipmentId = EquipmentId,
            ModelName = ModelName,
            SoftwareRevision = SoftwareRevision,
            AcceptCommunication = AcceptCommunication,
            StatusVariables = StatusVariables.Values.ToList(),
            EquipmentConstants = EquipmentConstants.Values.ToList(),
            CollectionEvents = CollectionEvents.Values.ToList(),
            DataReports = DataReports.Values.ToList(),
            Alarms = Alarms.Values.ToList(),
            StateSets = StateSets.Values.ToList(),
        };

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Load state from a JSON file.
    /// </summary>
    public static EquipmentStateManager LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);
        var data = JsonSerializer.Deserialize<EquipmentStateData>(json)
            ?? throw new InvalidOperationException($"Failed to load equipment state from {path}");

        var manager = new EquipmentStateManager
        {
            EquipmentId = data.EquipmentId,
            ModelName = data.ModelName,
            SoftwareRevision = data.SoftwareRevision,
            AcceptCommunication = data.AcceptCommunication,
        };

        foreach (var sv in data.StatusVariables)
            manager.StatusVariables[sv.Id] = sv;
        foreach (var ec in data.EquipmentConstants)
            manager.EquipmentConstants[ec.Id] = ec;
        foreach (var ce in data.CollectionEvents)
            manager.CollectionEvents[ce.Id] = ce;
        foreach (var dr in data.DataReports)
            manager.DataReports[dr.Id] = dr;
        foreach (var alarm in data.Alarms)
            manager.Alarms[alarm.Id] = alarm;
        foreach (var ss in data.StateSets)
            manager.StateSets[ss.Name] = ss;

        return manager;
    }

    /// <summary>
    /// Get status variable by ID.
    /// </summary>
    public StateVariable? GetStatusVariable(int svid) =>
        StatusVariables.TryGetValue(svid, out var sv) ? sv : null;

    /// <summary>
    /// Get equipment constant by ID.
    /// </summary>
    public EquipmentConstant? GetEquipmentConstant(int cecid) =>
        EquipmentConstants.TryGetValue(cecid, out var ec) ? ec : null;

    /// <summary>
    /// Get alarm by ID.
    /// </summary>
    public Alarm? GetAlarm(int alid) =>
        Alarms.TryGetValue(alid, out var alarm) ? alarm : null;

    /// <summary>
    /// Get collection event by ID.
    /// </summary>
    public CollectionEvent? GetCollectionEvent(int ceid) =>
        CollectionEvents.TryGetValue(ceid, out var ce) ? ce : null;

    /// <summary>
    /// Get data report by ID.
    /// </summary>
    public DataReport? GetDataReport(int rptid) =>
        DataReports.TryGetValue(rptid, out var dr) ? dr : null;
}

/// <summary>
/// JSON serialization model for equipment state data.
/// </summary>
internal class EquipmentStateData
{
    public string EquipmentId { get; set; } = "EQUIP001";
    public string ModelName { get; set; } = "EAPSim";
    public string SoftwareRevision { get; set; } = "1.00";
    public bool AcceptCommunication { get; set; } = true;
    public List<StateVariable> StatusVariables { get; set; } = new();
    public List<EquipmentConstant> EquipmentConstants { get; set; } = new();
    public List<CollectionEvent> CollectionEvents { get; set; } = new();
    public List<DataReport> DataReports { get; set; } = new();
    public List<Alarm> Alarms { get; set; } = new();
    public List<EquipmentStateSet> StateSets { get; set; } = new();
}
