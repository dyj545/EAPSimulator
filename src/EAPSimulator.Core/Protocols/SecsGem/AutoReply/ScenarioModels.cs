using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
/// Step kinds for a Famate-style scenario script.
/// </summary>
public enum ScenarioStepKind
{
    /// <summary>Actively send a message built from a template.</summary>
    Send,

    /// <summary>Wait for an inbound message matching (S,F) and conditions, with timeout.</summary>
    Receive,

    /// <summary>Reply to the most recently received Receive-step message with a template.</summary>
    Reply,

    /// <summary>Sleep for a fixed duration before continuing.</summary>
    Delay,

    /// <summary>Emit a message into the status/log stream.</summary>
    Log,

    /// <summary>
    /// Conditional jump. Evaluates each <see cref="ScenarioStep.Cases"/> in order against
    /// the most recently received message; first matching case jumps to its TargetLabel.
    /// If none match and <see cref="ScenarioStep.DefaultLabel"/> is set, jumps there;
    /// otherwise falls through to the next step. An empty cases list with only DefaultLabel
    /// is an unconditional goto.
    /// </summary>
    Branch,

    /// <summary>
    /// Send a message to the Host/MES side via HostProtocol, built from a Host template.
    /// Use when EAP needs to actively initiate Host traffic (e.g. EAPDA_MAP_COUNT_REQ).
    /// </summary>
    HostSend,

    /// <summary>
    /// Wait for an inbound Host/MES message matching by name (and optional path conditions),
    /// with timeout. Use to consume RMS/MES replies (e.g. DAEAP_MAP_COUNT_REP/ERROR).
    /// </summary>
    HostReceive,
}

/// <summary>
/// What to do when a Receive step times out.
/// </summary>
public enum ReceiveTimeoutAction
{
    /// <summary>Abort the scenario.</summary>
    Fail,

    /// <summary>Skip this step and continue.</summary>
    Skip,

    /// <summary>Treat as if matched and continue (no message captured).</summary>
    Continue,
}

/// <summary>
/// Which side of the SECS link this scenario is authored for. Open the same .json on the
/// wrong side and Send/Receive get reversed — the role tag prevents that.
/// </summary>
public enum ScenarioRole
{
    /// <summary>Role-agnostic — runs on either side. Use for pure Delay/Log smoke tests.</summary>
    Any,

    /// <summary>EAP / Host side (Active mode by default).</summary>
    Host,

    /// <summary>Equipment side (Passive mode by default).</summary>
    Equipment,
}

/// <summary>
/// One step in a scenario script. Kind selects which fields are meaningful.
/// </summary>
[JsonConverter(typeof(ScenarioStepConverter))]
public class ScenarioStep
{
    [JsonProperty("kind")]
    public ScenarioStepKind Kind { get; set; } = ScenarioStepKind.Receive;

    /// <summary>Optional human-readable name shown in the step list.</summary>
    [JsonProperty("label")]
    public string Label { get; set; } = "";

    // ─── Send / Reply ───

    /// <summary>Template name used to build the message (Send / Reply).</summary>
    [JsonProperty("templateName")]
    public string TemplateName { get; set; } = "";

    /// <summary>If true, Send will block until the peer's reply arrives (W-bit handling).</summary>
    [JsonProperty("waitReply")]
    public bool WaitReply { get; set; }

    // ─── Receive ───

    [JsonProperty("stream")]
    public byte Stream { get; set; }

    [JsonProperty("function")]
    public byte Function { get; set; }

    /// <summary>Conditions applied to the inbound message body (all must match).</summary>
    [JsonProperty("conditions")]
    public List<FieldCondition> Conditions { get; set; } = [];

    /// <summary>Receive timeout in milliseconds. 0 = wait forever.</summary>
    [JsonProperty("timeoutMs")]
    public int TimeoutMs { get; set; } = 30_000;

    [JsonProperty("onTimeout")]
    public ReceiveTimeoutAction OnTimeout { get; set; } = ReceiveTimeoutAction.Fail;

    // ─── HostSend / HostReceive ───

    /// <summary>
    /// Host message name. For HostSend it is the template name; for HostReceive it is
    /// the inbound message name to match (empty = match any).
    /// </summary>
    [JsonProperty("hostMessageName")]
    public string HostMessageName { get; set; } = "";

    /// <summary>
    /// Host channel to route this step through ("MES", "RMS", ...). Empty = first
    /// configured channel. Lets a single scenario talk to multiple downstream systems.
    /// </summary>
    [JsonProperty("hostChannelName")]
    public string HostChannelName { get; set; } = "";

    // ─── Delay ───

    [JsonProperty("delayMs")]
    public int DelayMs { get; set; } = 1_000;

    // ─── Log ───

    [JsonProperty("message")]
    public string Message { get; set; } = "";

    // ─── Branch ───

    /// <summary>
    /// Branch cases evaluated in order against the last received message.
    /// First match jumps to its <see cref="BranchCase.TargetLabel"/>.
    /// </summary>
    [JsonProperty("cases")]
    public List<BranchCase> Cases { get; set; } = [];

    /// <summary>
    /// Label to jump to if no <see cref="Cases"/> matched. Empty = continue to next step.
    /// (An unconditional goto is a Branch with empty Cases and a non-empty DefaultLabel.)
    /// </summary>
    [JsonProperty("defaultLabel")]
    public string DefaultLabel { get; set; } = "";

    [JsonIgnore]
    public string DisplayText
    {
        get
        {
            var label = string.IsNullOrEmpty(Label) ? "" : $" — {Label}";
            return Kind switch
            {
                ScenarioStepKind.Send => $"▶ Send {(string.IsNullOrEmpty(TemplateName) ? "(未设置)" : TemplateName)}{label}",
                ScenarioStepKind.Receive => BuildReceiveDisplay() + label,
                ScenarioStepKind.Reply => $"↩ Reply {(string.IsNullOrEmpty(TemplateName) ? "(未设置)" : TemplateName)}{label}",
                ScenarioStepKind.Delay => $"⏱ Delay {DelayMs} ms{label}",
                ScenarioStepKind.Log => $"📝 Log {Message}{label}",
                ScenarioStepKind.Branch => BuildBranchDisplay() + label,
                ScenarioStepKind.HostSend => $"▶ HostSend [{(string.IsNullOrEmpty(HostChannelName) ? "*" : HostChannelName)}] {(string.IsNullOrEmpty(HostMessageName) ? "(未设置)" : HostMessageName)}{label}",
                ScenarioStepKind.HostReceive => BuildHostReceiveDisplay() + label,
                _ => $"? {Kind}{label}",
            };
        }
    }

    private string BuildReceiveDisplay()
    {
        var sf = Stream == 0 && Function == 0 ? "any" : $"S{Stream}F{Function}";
        if (Conditions.Count == 0) return $"◀ Recv {sf}";
        var conds = string.Join(" & ", Conditions.Select(c => c.DisplayText));
        return $"◀ Recv {sf} where {conds}";
    }

    private string BuildBranchDisplay()
    {
        if (Cases.Count == 0)
            return string.IsNullOrEmpty(DefaultLabel) ? "⑂ Branch (无规则)" : $"⑂ Goto → {DefaultLabel}";
        var parts = Cases.Select(c => $"{c.Summary}→{c.TargetLabel}").ToList();
        if (!string.IsNullOrEmpty(DefaultLabel)) parts.Add($"else→{DefaultLabel}");
        return $"⑂ Branch {{ {string.Join(", ", parts)} }}";
    }

    private string BuildHostReceiveDisplay()
    {
        var name = string.IsNullOrEmpty(HostMessageName) ? "any" : HostMessageName;
        var ch = string.IsNullOrEmpty(HostChannelName) ? "*" : HostChannelName;
        if (Conditions.Count == 0) return $"◀ HostRecv [{ch}] {name}";
        var conds = string.Join(" & ", Conditions.Select(c => c.DisplayText));
        return $"◀ HostRecv [{ch}] {name} where {conds}";
    }
}

/// <summary>
/// One case in a <see cref="ScenarioStepKind.Branch"/> step.
/// All <see cref="Conditions"/> must match (AND); the first matching case jumps to <see cref="TargetLabel"/>.
/// </summary>
public class BranchCase
{
    [JsonProperty("conditions")]
    public List<FieldCondition> Conditions { get; set; } = [];

    [JsonProperty("targetLabel")]
    public string TargetLabel { get; set; } = "";

    [JsonIgnore]
    public string Summary => Conditions.Count == 0
        ? "always"
        : string.Join(" & ", Conditions.Select(c => c.DisplayText));
}

/// <summary>
/// A named scenario: an ordered sequence of script steps.
/// </summary>
public class ScenarioDefinition
{
    [JsonProperty("name")]
    public string Name { get; set; } = "New Scenario";

    [JsonProperty("description")]
    public string Description { get; set; } = "";

    /// <summary>
    /// Side of the link this scenario is authored for. Old files without the field
    /// deserialize to <see cref="ScenarioRole.Any"/> (no validation).
    /// </summary>
    [JsonProperty("role")]
    public ScenarioRole Role { get; set; } = ScenarioRole.Any;

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// If true, scenario restarts from step 0 after completing all steps.
    /// </summary>
    [JsonProperty("loop")]
    public bool Loop { get; set; } = false;

    /// <summary>
    /// If true, scenario starts automatically when the protocol enters Connected/Online.
    /// Otherwise the user must press Run.
    /// </summary>
    [JsonProperty("autoStart")]
    public bool AutoStart { get; set; } = false;

    [JsonProperty("steps")]
    public List<ScenarioStep> Steps { get; set; } = [];
}

/// <summary>
/// JsonConverter that auto-migrates legacy ScenarioStep schema (no "kind" field,
/// trigger and action embedded in one record) to the new Receive+Reply pair.
/// </summary>
internal class ScenarioStepConverter : JsonConverter<ScenarioStep>
{
    public override bool CanWrite => false;

    public override ScenarioStep? ReadJson(JsonReader reader, Type objectType, ScenarioStep? existingValue,
        bool hasExistingValue, JsonSerializer serializer)
    {
        var obj = JObject.Load(reader);

        // New schema: has "kind"
        if (obj.TryGetValue("kind", StringComparison.OrdinalIgnoreCase, out var _))
        {
            var step = new ScenarioStep();
            // Read fields manually to avoid recursing into ourselves
            PopulateFromJson(step, obj, serializer);
            return step;
        }

        // Legacy schema: synthesize a Receive step. The companion Reply (if any) will be
        // emitted by ScenarioDefinitionMigrator after the list deserializes.
        var legacy = new ScenarioStep
        {
            Kind = ScenarioStepKind.Receive,
            Stream = obj.Value<byte?>("stream") ?? 0,
            Function = obj.Value<byte?>("function") ?? 0,
            Conditions = obj["conditions"]?.ToObject<List<FieldCondition>>(serializer) ?? [],
            // Stash the legacy action template name in the Message field so the migrator
            // can pick it up and produce a Reply step. Cleared after migration.
            Message = obj.Value<string>("actionTemplateName") ?? "",
        };
        return legacy;
    }

    private static void PopulateFromJson(ScenarioStep step, JObject obj, JsonSerializer serializer)
    {
        step.Kind = obj.Value<string>("kind") is { } kindStr && Enum.TryParse<ScenarioStepKind>(kindStr, true, out var k)
            ? k
            : ScenarioStepKind.Receive;
        step.Label = obj.Value<string>("label") ?? "";
        step.TemplateName = obj.Value<string>("templateName") ?? "";
        step.WaitReply = obj.Value<bool?>("waitReply") ?? false;
        step.Stream = obj.Value<byte?>("stream") ?? 0;
        step.Function = obj.Value<byte?>("function") ?? 0;
        step.Conditions = obj["conditions"]?.ToObject<List<FieldCondition>>(serializer) ?? [];
        step.TimeoutMs = obj.Value<int?>("timeoutMs") ?? 30_000;
        step.OnTimeout = obj.Value<string>("onTimeout") is { } otStr && Enum.TryParse<ReceiveTimeoutAction>(otStr, true, out var ot)
            ? ot
            : ReceiveTimeoutAction.Fail;
        step.DelayMs = obj.Value<int?>("delayMs") ?? 1_000;
        step.Message = obj.Value<string>("message") ?? "";
        step.Cases = obj["cases"]?.ToObject<List<BranchCase>>(serializer) ?? [];
        step.DefaultLabel = obj.Value<string>("defaultLabel") ?? "";
    }

    public override void WriteJson(JsonWriter writer, ScenarioStep? value, JsonSerializer serializer)
    {
        // Not used — CanWrite=false uses default contract serialization, which writes every
        // [JsonProperty] field. That's fine; readers ignore irrelevant fields per Kind.
        throw new NotSupportedException();
    }
}

/// <summary>
/// Helper that finalizes legacy migration after a ScenarioDefinition list has been deserialized.
/// Splits any (Receive with Message=actionTemplateName) into Receive + Reply.
/// </summary>
public static class ScenarioMigration
{
    public static void MigrateLegacy(IEnumerable<ScenarioDefinition> scenarios)
    {
        foreach (var sc in scenarios)
        {
            for (int i = 0; i < sc.Steps.Count; i++)
            {
                var s = sc.Steps[i];
                // The converter parks the legacy "actionTemplateName" in Message on a Receive step.
                if (s.Kind == ScenarioStepKind.Receive && !string.IsNullOrEmpty(s.Message))
                {
                    var actionName = s.Message;
                    s.Message = "";
                    sc.Steps.Insert(i + 1, new ScenarioStep
                    {
                        Kind = ScenarioStepKind.Reply,
                        TemplateName = actionName,
                    });
                    i++; // skip the inserted Reply
                }
            }
        }
    }
}
