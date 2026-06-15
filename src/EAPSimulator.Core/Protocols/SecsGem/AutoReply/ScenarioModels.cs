using Newtonsoft.Json;

namespace EAPSimulator.Core.Protocols.SecsGem.AutoReply;

/// <summary>
/// A condition that matches a specific value at a path within a SECS message item tree.
/// Path format: "0/1/2" means root list item 0 -> list item 1 -> list item 2.
/// </summary>
public class FieldCondition
{
    /// <summary>
    /// Path to the item in the SECS tree. "0/1/2" means root[0][1][2].
    /// </summary>
    [JsonProperty("path")]
    public string Path { get; set; } = "";

    /// <summary>
    /// Comparison operator: ==, !=, >, <, >=, <=, contains.
    /// </summary>
    [JsonProperty("operator")]
    public string Operator { get; set; } = "==";

    /// <summary>
    /// Expected value as string. Will be compared against the item's string representation.
    /// </summary>
    [JsonProperty("value")]
    public string Value { get; set; } = "";

    [JsonIgnore]
    public string DisplayText => string.IsNullOrEmpty(Path) ? "(无条件)" : $"[{Path}] {Operator} {Value}";

    [JsonIgnore]
    public static string[] SupportedOperators { get; } = ["==", "!=", ">", "<", ">=", "<=", "contains"];
}

/// <summary>
/// A single step in a scenario with conditional matching.
/// </summary>
public class ScenarioStep
{
    /// <summary>
    /// Unique node identifier for flow design.
    /// </summary>
    [JsonProperty("nodeId")]
    public string NodeId { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// Type of trigger for this step.
    /// </summary>
    [JsonProperty("triggerType")]
    public TriggerType TriggerType { get; set; } = TriggerType.SecsMessage;

    /// <summary>
    /// Stream number to match (0 = any). Used when TriggerType is SecsMessage.
    /// </summary>
    [JsonProperty("stream")]
    public byte Stream { get; set; }

    /// <summary>
    /// Function number to match.
    /// </summary>
    [JsonProperty("function")]
    public byte Function { get; set; }

    /// <summary>
    /// Host message name to match. Used when TriggerType is HostMessage.
    /// </summary>
    [JsonProperty("hostTriggerName")]
    public string HostTriggerName { get; set; } = "";

    /// <summary>
    /// Equipment state variable name to watch. Used when TriggerType is EquipmentState.
    /// </summary>
    [JsonProperty("stateVariableName")]
    public string StateVariableName { get; set; } = "";

    /// <summary>
    /// Source field for data mapping. Used when TriggerType is Mapper.
    /// </summary>
    [JsonProperty("mapperSourceField")]
    public string MapperSourceField { get; set; } = "";

    /// <summary>
    /// Target variable for data mapping.
    /// </summary>
    [JsonProperty("mapperVariable")]
    public string MapperVariable { get; set; } = "";

    /// <summary>
    /// Variable name for judgement evaluation. Used when TriggerType is Judgement.
    /// </summary>
    [JsonProperty("judgementVariable")]
    public string JudgementVariable { get; set; } = "";

    /// <summary>
    /// Operator for judgement: ==, !=, >, <, >=, <=, contains.
    /// </summary>
    [JsonProperty("judgementOperator")]
    public string JudgementOperator { get; set; } = "==";

    /// <summary>
    /// Expected value for judgement.
    /// </summary>
    [JsonProperty("judgementValue")]
    public string JudgementValue { get; set; } = "";

    /// <summary>
    /// Target step index to jump to when judgement is true. -1 = continue to next step.
    /// </summary>
    [JsonProperty("judgementTargetStep")]
    public int JudgementTargetStep { get; set; } = -1;

    /// <summary>
    /// Additional field conditions that must ALL match.
    /// </summary>
    [JsonProperty("conditions")]
    public List<FieldCondition> Conditions { get; set; } = [];

    /// <summary>
    /// Type of action to perform.
    /// </summary>
    [JsonProperty("actionType")]
    public ActionType ActionType { get; set; } = ActionType.SecsReply;

    /// <summary>
    /// Display name of the template to send when triggered. Empty = wait for next trigger.
    /// </summary>
    [JsonProperty("actionTemplateName")]
    public string ActionTemplateName { get; set; } = "";

    [JsonProperty("actionStream")]
    public byte ActionStream { get; set; }

    [JsonProperty("actionFunction")]
    public byte ActionFunction { get; set; }

    /// <summary>
    /// Host message name to send. Used when ActionType is HostMessage.
    /// </summary>
    [JsonProperty("hostActionName")]
    public string HostActionName { get; set; } = "";

    /// <summary>
    /// Target variable name for state alteration. Used when ActionType is StateAlterer.
    /// </summary>
    [JsonProperty("stateAlterTarget")]
    public string StateAlterTarget { get; set; } = "";

    /// <summary>
    /// Value to set for state alteration.
    /// </summary>
    [JsonProperty("stateAlterValue")]
    public string StateAlterValue { get; set; } = "";

    /// <summary>
    /// Whether this step was initiated by a Host message.
    /// </summary>
    [JsonProperty("hostInitiated")]
    public bool HostInitiated { get; set; }

    [JsonIgnore]
    public string TriggerDisplay
    {
        get
        {
            var base_ = $"S{Stream}F{Function}";
            if (Conditions.Count == 0) return base_;
            var conds = string.Join(" & ", Conditions.Select(c => c.DisplayText));
            return $"{base_} where {conds}";
        }
    }

    [JsonIgnore]
    public string ActionDisplay => !string.IsNullOrEmpty(ActionTemplateName)
        ? $"S{ActionStream}F{ActionFunction} ({ActionTemplateName})"
        : "(等待下一步)";
}

/// <summary>
/// A named scenario: a multi-step conversation flow with conditional triggers.
/// </summary>
public class ScenarioDefinition
{
    [JsonProperty("name")]
    public string Name { get; set; } = "New Scenario";

    [JsonProperty("description")]
    public string Description { get; set; } = "";

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// If true, scenario restarts from step 0 after completing all steps.
    /// </summary>
    [JsonProperty("loop")]
    public bool Loop { get; set; } = false;

    [JsonProperty("steps")]
    public List<ScenarioStep> Steps { get; set; } = [];
}

/// <summary>
/// Types of triggers that can activate a scenario step.
/// </summary>
public enum TriggerType
{
    SecsMessage = 0,
    HostMessage = 1,
    EquipmentState = 2,
    Mapper = 3,
    Judgement = 4,
}

/// <summary>
/// Types of actions that a scenario step can perform.
/// </summary>
public enum ActionType
{
    SecsReply = 0,
    HostMessage = 1,
    StateAlterer = 2,
    Mapper = 3,
}

/// <summary>
/// Event args for host action triggered by a scenario.
/// </summary>
public class HostActionEventArgs : EventArgs
{
    public string HostMessageName { get; }

    public HostActionEventArgs(string hostMessageName)
    {
        HostMessageName = hostMessageName;
    }
}

/// <summary>
/// Event args for state alter action triggered by a scenario.
/// </summary>
public class StateAlterEventArgs : EventArgs
{
    public string VariableName { get; }
    public string NewValue { get; }

    public StateAlterEventArgs(string variableName, string newValue)
    {
        VariableName = variableName;
        NewValue = newValue;
    }
}
