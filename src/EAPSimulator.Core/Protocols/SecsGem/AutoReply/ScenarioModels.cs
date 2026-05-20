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
    /// Expected value as string. Will be compared against the item's string representation.
    /// </summary>
    [JsonProperty("value")]
    public string Value { get; set; } = "";

    [JsonIgnore]
    public string DisplayText => string.IsNullOrEmpty(Path) ? "(无条件)" : $"[{Path}] = {Value}";
}

/// <summary>
/// A single step in a scenario with conditional matching.
/// </summary>
public class ScenarioStep
{
    /// <summary>
    /// Stream number to match (0 = any).
    /// </summary>
    [JsonProperty("stream")]
    public byte Stream { get; set; }

    /// <summary>
    /// Function number to match.
    /// </summary>
    [JsonProperty("function")]
    public byte Function { get; set; }

    /// <summary>
    /// Additional field conditions that must ALL match.
    /// </summary>
    [JsonProperty("conditions")]
    public List<FieldCondition> Conditions { get; set; } = [];

    /// <summary>
    /// Display name of the template to send when triggered. Empty = wait for next trigger.
    /// </summary>
    [JsonProperty("actionTemplateName")]
    public string ActionTemplateName { get; set; } = "";

    [JsonProperty("actionStream")]
    public byte ActionStream { get; set; }

    [JsonProperty("actionFunction")]
    public byte ActionFunction { get; set; }

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
