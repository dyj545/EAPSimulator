using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EAPSimulator.Core.Protocols.SecsGem.AutoReply;

/// <summary>
/// A condition for matching SECS / Host messages. Carries two coexisting forms:
/// <list type="bullet">
///   <item><b>Legacy form</b> (Path + Operator + Value): one path, one operator, one expected
///         string. Still supported and still authored by the simple UI.</item>
///   <item><b>Expression form</b> (Expression): a DynamicExpresso boolean expression evaluated
///         against the engine context (<c>secs["0/1/2"] == "OK" &amp;&amp; num(vars["count"]) &gt; 0</c>).</item>
/// </list>
/// At evaluation time, a non-empty <see cref="Expression"/> takes precedence; otherwise the
/// engine synthesizes an equivalent expression from the legacy fields and runs it through the
/// same evaluator. Old JSON files keep working with no migration.
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

    /// <summary>
    /// Optional expression-mode body. When non-empty, this is evaluated directly against the
    /// <see cref="ScenarioExpression"/> context and the legacy Path/Operator/Value fields are
    /// ignored. Empty by default so old files round-trip unchanged.
    /// </summary>
    [JsonProperty("expression")]
    public string Expression { get; set; } = "";

    [JsonIgnore]
    public bool IsExpressionMode => !string.IsNullOrWhiteSpace(Expression);

    [JsonIgnore]
    public string DisplayText
    {
        get
        {
            if (IsExpressionMode) return $"{{ {Expression} }}";
            return string.IsNullOrEmpty(Path) ? "(无条件)" : $"[{Path}] {Operator} {Value}";
        }
    }

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

    /// <summary>
    /// Assign a value (literal or captured from the last received SECS/Host message)
    /// to a named variable. Variables can be interpolated into Send/Reply/HostSend
    /// templates with <c>${name}</c>.
    /// </summary>
    SetVariable,

    /// <summary>
    /// Mark the start of a loop body. A matching <see cref="EndLoop"/> with the same
    /// <see cref="ScenarioStep.LoopId"/> closes it. <see cref="ScenarioStep.LoopTimes"/>
    /// = N runs the body N times; 0 with no other guard means infinite (controlled by Stop).
    /// </summary>
    Loop,

    /// <summary>
    /// Mark the end of a loop body. Pairs with <see cref="Loop"/> by <see cref="ScenarioStep.LoopId"/>.
    /// </summary>
    EndLoop,

    /// <summary>
    /// Run another scenario inline as a sub-routine. Variables are shared with the parent.
    /// Returns to the next step on completion. Recursion depth is bounded.
    /// </summary>
    CallScenario,

    /// <summary>
    /// Iterate over a runtime collection (a SECS list item, a Host ArrayList field, or a
    /// delimited string variable). Pairs with <see cref="EndForEach"/> using <see cref="ScenarioStep.ForEachId"/>.
    /// On each iteration the current element is exposed as <c>${$foreach.&lt;Id&gt;.item}</c>
    /// (plus an alias if <see cref="ScenarioStep.ForEachItemVariable"/> is set) and the 0-based
    /// index as <c>${$foreach.&lt;Id&gt;.index}</c>; in expression context <c>foreach["Id"]</c>
    /// reads the current item and <c>foreachIndex["Id"]</c> reads the index.
    /// </summary>
    ForEach,

    /// <summary>Mark the end of a <see cref="ForEach"/> body; pairs by <see cref="ScenarioStep.ForEachId"/>.</summary>
    EndForEach,
}

/// <summary>
/// Where a <see cref="ScenarioStepKind.ForEach"/> step reads its collection from.
/// </summary>
public enum ForEachSource
{
    /// <summary>
    /// Read a <c>SecsList</c> from the last received SECS message at <see cref="ScenarioStep.ForEachPath"/>.
    /// Non-list items at the path produce a single-element iteration with the item's string value.
    /// </summary>
    SecsList,

    /// <summary>
    /// Read a Host field by name (<see cref="ScenarioStep.ForEachPath"/>) on the last received Host
    /// message; iterates over its <c>Children</c> when the field is an ArrayList / Object.
    /// </summary>
    HostArrayList,

    /// <summary>
    /// Split a string variable (<see cref="ScenarioStep.ForEachPath"/>) by
    /// <see cref="ScenarioStep.ForEachSeparator"/> (default <c>,</c>) and iterate the parts.
    /// Whitespace-only segments are kept verbatim — the user controls trimming.
    /// </summary>
    Variable,
}

/// <summary>
/// Source of the value assigned by a <see cref="ScenarioStepKind.SetVariable"/> step.
/// </summary>
public enum VariableSource
{
    /// <summary>Use <see cref="ScenarioStep.LiteralValue"/> verbatim (after <c>${var}</c> render).</summary>
    Literal,

    /// <summary>Read a path inside the most recently captured SECS message (Receive/Reply input).</summary>
    LastSecsField,

    /// <summary>Read a field name inside the most recently captured Host message (HostReceive input).</summary>
    LastHostField,
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

    /// <summary>
    /// Optional label to jump to when this step throws (timeout, IO failure, template lookup
    /// failed, expression error, …). Empty = legacy behaviour: the engine fails the whole
    /// scenario with the exception message.
    /// <para>While the error branch is running, three variables are populated:</para>
    /// <list type="bullet">
    ///   <item><c>$error.message</c> — the exception's <see cref="Exception.Message"/></item>
    ///   <item><c>$error.kind</c>    — exception type name (e.g. <c>TimeoutException</c>)</item>
    ///   <item><c>$error.step</c>    — index of the failing step in this scenario</item>
    /// </list>
    /// <para>For Receive / HostReceive, a timeout only enters this branch when
    /// <see cref="OnTimeout"/> is <see cref="ReceiveTimeoutAction.Fail"/>; <c>Skip</c> and
    /// <c>Continue</c> keep their existing semantics.</para>
    /// </summary>
    [JsonProperty("onErrorLabel")]
    public string OnErrorLabel { get; set; } = "";

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

    // ─── SetVariable ───

    /// <summary>Target variable name (e.g. "lotId"). Required for SetVariable.</summary>
    [JsonProperty("variableName")]
    public string VariableName { get; set; } = "";

    /// <summary>Where the value comes from.</summary>
    [JsonProperty("variableSource")]
    public VariableSource VariableSource { get; set; } = VariableSource.Literal;

    /// <summary>Path / field name when <see cref="VariableSource"/> is a captured-field source.</summary>
    [JsonProperty("variablePath")]
    public string VariablePath { get; set; } = "";

    /// <summary>Literal text when <see cref="VariableSource"/> is <see cref="VariableSource.Literal"/>. Supports nested <c>${var}</c>.</summary>
    [JsonProperty("literalValue")]
    public string LiteralValue { get; set; } = "";

    // ─── Loop / EndLoop ───

    /// <summary>
    /// Pairing key between a <see cref="ScenarioStepKind.Loop"/> and its closing <see cref="ScenarioStepKind.EndLoop"/>.
    /// Allows nested loops by giving each level a distinct id (e.g. "L1" outer, "L2" inner).
    /// </summary>
    [JsonProperty("loopId")]
    public string LoopId { get; set; } = "";

    /// <summary>
    /// Iteration count for a Loop step. 0 = run until <see cref="LoopWhile"/> goes false (or
    /// scenario stops if no guard is set — caller's responsibility to break out).
    /// </summary>
    [JsonProperty("loopTimes")]
    public int LoopTimes { get; set; } = 0;

    /// <summary>
    /// Reserved for a future expression-based loop guard (e.g. <c>${count} &lt; 10</c>).
    /// Currently unused by the engine; kept on the model so JSON written today survives
    /// the upgrade without re-saves.
    /// </summary>
    [JsonProperty("loopWhile")]
    public string LoopWhile { get; set; } = "";

    // ─── CallScenario ───

    /// <summary>Name of the scenario to invoke as a sub-routine.</summary>
    [JsonProperty("subScenarioName")]
    public string SubScenarioName { get; set; } = "";

    // ─── ForEach / EndForEach ───

    /// <summary>
    /// Pairing key between a <see cref="ScenarioStepKind.ForEach"/> step and its closing
    /// <see cref="ScenarioStepKind.EndForEach"/>. Mirrors <see cref="LoopId"/> but kept separate
    /// so loops and foreaches can coexist in a single scenario without id collisions.
    /// </summary>
    [JsonProperty("forEachId")]
    public string ForEachId { get; set; } = "";

    /// <summary>Where the collection comes from. See <see cref="ForEachSource"/>.</summary>
    [JsonProperty("forEachSource")]
    public ForEachSource ForEachSource { get; set; } = ForEachSource.SecsList;

    /// <summary>
    /// Source-specific selector: SECS path for <see cref="ForEachSource.SecsList"/>, Host field
    /// name for <see cref="ForEachSource.HostArrayList"/>, variable name (no <c>${}</c>) for
    /// <see cref="ForEachSource.Variable"/>. Empty for SECS = iterate the root if it's a list.
    /// </summary>
    [JsonProperty("forEachPath")]
    public string ForEachPath { get; set; } = "";

    /// <summary>
    /// Optional alias variable name. When non-empty, each iteration also writes the current item
    /// to <c>${name}</c> so templates can read it without the <c>$foreach.&lt;Id&gt;.item</c>
    /// prefix. Empty = only the namespaced name is available.
    /// </summary>
    [JsonProperty("forEachItemVariable")]
    public string ForEachItemVariable { get; set; } = "";

    /// <summary>
    /// Separator used when <see cref="ForEachSource"/> is <see cref="ForEachSource.Variable"/>.
    /// Defaults to a single comma. Multi-char separators are supported verbatim.
    /// </summary>
    [JsonProperty("forEachSeparator")]
    public string ForEachSeparator { get; set; } = ",";

    [JsonIgnore]
    public string DisplayText
    {
        get
        {
            var label = string.IsNullOrEmpty(Label) ? "" : $" — {Label}";
            // Append an error-arrow suffix so the step list shows protected steps at a glance.
            var onErr = string.IsNullOrEmpty(OnErrorLabel) ? "" : $"  ⚠→{OnErrorLabel}";
            return (Kind switch
            {
                ScenarioStepKind.Send => $"▶ Send {(string.IsNullOrEmpty(TemplateName) ? "(未设置)" : TemplateName)}{label}",
                ScenarioStepKind.Receive => BuildReceiveDisplay() + label,
                ScenarioStepKind.Reply => $"↩ Reply {(string.IsNullOrEmpty(TemplateName) ? "(未设置)" : TemplateName)}{label}",
                ScenarioStepKind.Delay => $"⏱ Delay {DelayMs} ms{label}",
                ScenarioStepKind.Log => $"📝 Log {Message}{label}",
                ScenarioStepKind.Branch => BuildBranchDisplay() + label,
                ScenarioStepKind.HostSend => $"▶ HostSend [{(string.IsNullOrEmpty(HostChannelName) ? "*" : HostChannelName)}] {(string.IsNullOrEmpty(HostMessageName) ? "(未设置)" : HostMessageName)}{label}",
                ScenarioStepKind.HostReceive => BuildHostReceiveDisplay() + label,
                ScenarioStepKind.SetVariable => BuildSetVariableDisplay() + label,
                ScenarioStepKind.Loop => BuildLoopDisplay() + label,
                ScenarioStepKind.EndLoop => $"⤴ EndLoop {(string.IsNullOrEmpty(LoopId) ? "(无 LoopId)" : LoopId)}{label}",
                ScenarioStepKind.CallScenario => $"⏎ Call {(string.IsNullOrEmpty(SubScenarioName) ? "(未设置)" : SubScenarioName)}{label}",
                ScenarioStepKind.ForEach => BuildForEachDisplay() + label,
                ScenarioStepKind.EndForEach => $"⤴ EndForEach {(string.IsNullOrEmpty(ForEachId) ? "(无 ForEachId)" : ForEachId)}{label}",
                _ => $"? {Kind}{label}",
            }) + onErr;
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

    private string BuildSetVariableDisplay()
    {
        var name = string.IsNullOrEmpty(VariableName) ? "?" : VariableName;
        var rhs = VariableSource switch
        {
            VariableSource.Literal => $"\"{LiteralValue}\"",
            VariableSource.LastSecsField => $"secs[{VariablePath}]",
            VariableSource.LastHostField => $"host.{VariablePath}",
            _ => "?",
        };
        return $"𝑥 Set {name} = {rhs}";
    }

    private string BuildLoopDisplay()
    {
        var id = string.IsNullOrEmpty(LoopId) ? "?" : LoopId;
        if (LoopTimes > 0) return $"⟳ Loop {id} × {LoopTimes}";
        if (!string.IsNullOrEmpty(LoopWhile)) return $"⟳ Loop {id} while {LoopWhile}";
        return $"⟳ Loop {id} ∞";
    }

    private string BuildForEachDisplay()
    {
        var id = string.IsNullOrEmpty(ForEachId) ? "?" : ForEachId;
        var src = ForEachSource switch
        {
            ForEachSource.SecsList => $"secs[{(string.IsNullOrEmpty(ForEachPath) ? "*" : ForEachPath)}]",
            ForEachSource.HostArrayList => $"host[{(string.IsNullOrEmpty(ForEachPath) ? "*" : ForEachPath)}]",
            ForEachSource.Variable => $"split(${{{ForEachPath}}}, \"{ForEachSeparator}\")",
            _ => "?",
        };
        var alias = string.IsNullOrEmpty(ForEachItemVariable) ? "" : $" as ${ForEachItemVariable}";
        return $"⟳⃗ ForEach {id} ← {src}{alias}";
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

    /// <summary>
    /// 当为 true，并且 <see cref="TriggerStream"/>/<see cref="TriggerFunction"/> 命中入站消息时
    /// (并通过可选 <see cref="TriggerConditions"/>)，引擎会在本场景未运行的情况下自动 Start。
    /// 例：S6F11 + Conditions=[secs[0/1] == "1001"] → 每次收到 CEID=1001 的事件就拉起对应场景。
    /// </summary>
    [JsonProperty("triggerOnMessage")]
    public bool TriggerOnMessage { get; set; } = false;

    [JsonProperty("triggerStream")]
    public byte TriggerStream { get; set; }

    [JsonProperty("triggerFunction")]
    public byte TriggerFunction { get; set; }

    [JsonProperty("triggerConditions")]
    public List<FieldCondition> TriggerConditions { get; set; } = [];

    /// <summary>
    /// Persisted positions for the flow-canvas editor, keyed by step index. Layout is recomputed
    /// from the step list on every open (so adding/deleting steps shifts indices correctly); the
    /// stored x/y only override the default vertical positions for steps the user has dragged.
    /// Null on legacy files — fine, the layout engine falls back to its column default.
    /// </summary>
    [JsonProperty("layout")]
    public ScenarioFlowPersistedLayout? Layout { get; set; }
}

/// <summary>
/// Slim on-disk shape for canvas positions. Reuses <see cref="ScenarioLayout"/>'s vocabulary
/// (separate NodePosition list) so we can grow into <see cref="ScenarioLayout"/> later without
/// re-migrating files; for now we only need step → (x, y).
/// </summary>
public class ScenarioFlowPersistedLayout
{
    [JsonProperty("nodes")]
    public List<ScenarioFlowPersistedNode> Nodes { get; set; } = [];

    [JsonProperty("zoom")]
    public double Zoom { get; set; } = 1.0;

    [JsonProperty("offsetX")]
    public double OffsetX { get; set; }

    [JsonProperty("offsetY")]
    public double OffsetY { get; set; }
}

/// <summary>Position override for a single step in a scenario's layout.</summary>
public class ScenarioFlowPersistedNode
{
    [JsonProperty("stepIndex")]
    public int StepIndex { get; set; }

    [JsonProperty("x")]
    public double X { get; set; }

    [JsonProperty("y")]
    public double Y { get; set; }
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
        step.OnErrorLabel = obj.Value<string>("onErrorLabel") ?? "";
        step.DelayMs = obj.Value<int?>("delayMs") ?? 1_000;
        step.Message = obj.Value<string>("message") ?? "";
        step.Cases = obj["cases"]?.ToObject<List<BranchCase>>(serializer) ?? [];
        step.DefaultLabel = obj.Value<string>("defaultLabel") ?? "";
        // Host channel name (added with multi-channel host routing).
        step.HostMessageName = obj.Value<string>("hostMessageName") ?? "";
        step.HostChannelName = obj.Value<string>("hostChannelName") ?? "";
        // SetVariable / Loop / CallScenario fields. All optional — older files lack them.
        step.VariableName = obj.Value<string>("variableName") ?? "";
        step.VariableSource = obj.Value<string>("variableSource") is { } vsStr
            && Enum.TryParse<VariableSource>(vsStr, true, out var vs)
                ? vs
                : VariableSource.Literal;
        step.VariablePath = obj.Value<string>("variablePath") ?? "";
        step.LiteralValue = obj.Value<string>("literalValue") ?? "";
        step.LoopId = obj.Value<string>("loopId") ?? "";
        step.LoopTimes = obj.Value<int?>("loopTimes") ?? 0;
        step.LoopWhile = obj.Value<string>("loopWhile") ?? "";
        step.SubScenarioName = obj.Value<string>("subScenarioName") ?? "";
        // ForEach fields. Older files lack them; defaults match the model.
        step.ForEachId = obj.Value<string>("forEachId") ?? "";
        step.ForEachSource = obj.Value<string>("forEachSource") is { } fesStr
            && Enum.TryParse<ForEachSource>(fesStr, true, out var fes)
                ? fes
                : ForEachSource.SecsList;
        step.ForEachPath = obj.Value<string>("forEachPath") ?? "";
        step.ForEachItemVariable = obj.Value<string>("forEachItemVariable") ?? "";
        step.ForEachSeparator = obj.Value<string>("forEachSeparator") ?? ",";
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
