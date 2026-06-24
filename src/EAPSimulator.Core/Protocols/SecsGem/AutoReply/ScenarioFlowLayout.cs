namespace EAPSimulator.Core.Protocols.SecsGem.AutoReply;

/// <summary>
/// Lightweight, deterministic layout for a scenario's step graph. Produces a node-per-step
/// vertical column with a list of typed edges; the UI wraps that into a Canvas with absolute
/// positioning. Pure data — no Avalonia / WPF dependency, so the algorithm can be unit-tested.
///
/// <para>The layout is intentionally NOT a full Sugiyama / dagre-style multi-column drawer.
/// Scenarios stay readable when steps run top-to-bottom and jumps render as side-routed
/// curves; the simple column avoids node overlap, keeps PCs visually aligned with the
/// ListBox view, and lets the user drag nodes to refine without fighting a layout engine.</para>
/// </summary>
public static class ScenarioFlowLayout
{
    public const double NodeWidth = 220;
    public const double NodeHeight = 44;
    public const double HorizontalGap = 60;     // horizontal lane for jump-back edges
    public const double VerticalGap = 24;       // gap between adjacent nodes' bounding boxes

    /// <summary>
    /// Build a fresh layout from <paramref name="scenario"/>. When <paramref name="overrides"/>
    /// has a position for a step index that matches, that x/y wins — letting the user drag a
    /// node and re-open the canvas without losing the tweak. Edges are always recomputed: they
    /// are derived from the step list, not stored.
    /// </summary>
    public static ScenarioFlowLayoutResult Build(
        ScenarioDefinition scenario,
        IReadOnlyDictionary<int, (double X, double Y)>? overrides = null)
    {
        var nodes = new List<FlowNode>(scenario.Steps.Count);
        var stepToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var labelToIndex = new Dictionary<string, int>(StringComparer.Ordinal);

        // Pre-scan: collect labels so jump edges can resolve them without re-scanning per step.
        for (int i = 0; i < scenario.Steps.Count; i++)
        {
            var s = scenario.Steps[i];
            if (!string.IsNullOrEmpty(s.Label))
                labelToIndex.TryAdd(s.Label, i);
        }

        // Default vertical positions; the caller's overrides can move any node afterwards.
        for (int i = 0; i < scenario.Steps.Count; i++)
        {
            var s = scenario.Steps[i];
            double x = 0, y = i * (NodeHeight + VerticalGap);
            if (overrides != null && overrides.TryGetValue(i, out var pos))
            {
                x = pos.X;
                y = pos.Y;
            }
            nodes.Add(new FlowNode
            {
                StepIndex = i,
                Step = s,
                X = x,
                Y = y,
                Kind = s.Kind,
            });
        }

        var edges = new List<FlowEdge>();
        var loopStack = new Stack<(string Id, int StartIdx)>();
        var foreachStack = new Stack<(string Id, int StartIdx)>();

        for (int i = 0; i < scenario.Steps.Count; i++)
        {
            var s = scenario.Steps[i];
            var nextIdx = i + 1;

            switch (s.Kind)
            {
                case ScenarioStepKind.Loop:
                    if (!string.IsNullOrEmpty(s.LoopId))
                        loopStack.Push((s.LoopId, i));
                    if (nextIdx < scenario.Steps.Count)
                        edges.Add(new FlowEdge(i, nextIdx, FlowEdgeKind.Sequential, ""));
                    break;
                case ScenarioStepKind.EndLoop:
                    // Edge back to the matching Loop head. Pop only when ids match — caller may
                    // have left a half-edited scenario, which we don't try to repair.
                    if (loopStack.Count > 0 && (string.IsNullOrEmpty(s.LoopId) || loopStack.Peek().Id == s.LoopId))
                    {
                        var (_, startIdx) = loopStack.Pop();
                        edges.Add(new FlowEdge(i, startIdx, FlowEdgeKind.LoopBack, "↻"));
                    }
                    if (nextIdx < scenario.Steps.Count)
                        edges.Add(new FlowEdge(i, nextIdx, FlowEdgeKind.Sequential, ""));
                    break;
                case ScenarioStepKind.ForEach:
                    if (!string.IsNullOrEmpty(s.ForEachId))
                        foreachStack.Push((s.ForEachId, i));
                    if (nextIdx < scenario.Steps.Count)
                        edges.Add(new FlowEdge(i, nextIdx, FlowEdgeKind.Sequential, ""));
                    break;
                case ScenarioStepKind.EndForEach:
                    if (foreachStack.Count > 0 && (string.IsNullOrEmpty(s.ForEachId) || foreachStack.Peek().Id == s.ForEachId))
                    {
                        var (_, startIdx) = foreachStack.Pop();
                        edges.Add(new FlowEdge(i, startIdx, FlowEdgeKind.ForEachBack, "↻"));
                    }
                    if (nextIdx < scenario.Steps.Count)
                        edges.Add(new FlowEdge(i, nextIdx, FlowEdgeKind.Sequential, ""));
                    break;
                case ScenarioStepKind.Branch:
                    // Each case is one labelled edge; default label is a separate edge; if neither
                    // matched, control falls through to the next step (a Sequential edge).
                    foreach (var c in s.Cases)
                    {
                        if (string.IsNullOrEmpty(c.TargetLabel)) continue;
                        if (labelToIndex.TryGetValue(c.TargetLabel, out var target))
                            edges.Add(new FlowEdge(i, target, FlowEdgeKind.BranchCase, c.Summary));
                    }
                    if (!string.IsNullOrEmpty(s.DefaultLabel) && labelToIndex.TryGetValue(s.DefaultLabel, out var defTarget))
                        edges.Add(new FlowEdge(i, defTarget, FlowEdgeKind.BranchDefault, "else"));
                    // The fall-through edge is suppressed when DefaultLabel is set — the engine
                    // doesn't fall through in that case, so showing the arrow would mislead.
                    if (string.IsNullOrEmpty(s.DefaultLabel) && nextIdx < scenario.Steps.Count)
                        edges.Add(new FlowEdge(i, nextIdx, FlowEdgeKind.Sequential, ""));
                    break;
                default:
                    if (nextIdx < scenario.Steps.Count)
                        edges.Add(new FlowEdge(i, nextIdx, FlowEdgeKind.Sequential, ""));
                    break;
            }

            // OnErrorLabel — applies to every step kind.
            if (!string.IsNullOrEmpty(s.OnErrorLabel) && labelToIndex.TryGetValue(s.OnErrorLabel, out var errTarget))
                edges.Add(new FlowEdge(i, errTarget, FlowEdgeKind.OnError, "⚠"));
        }

        return new ScenarioFlowLayoutResult { Nodes = nodes, Edges = edges };
    }
}

/// <summary>Output of <see cref="ScenarioFlowLayout.Build"/>.</summary>
public sealed class ScenarioFlowLayoutResult
{
    public List<FlowNode> Nodes { get; init; } = [];
    public List<FlowEdge> Edges { get; init; } = [];
}

/// <summary>A node in the scenario flow graph. Position is in canvas-pixel coordinates.</summary>
public sealed class FlowNode
{
    public int StepIndex { get; init; }
    public required ScenarioStep Step { get; init; }
    public ScenarioStepKind Kind { get; init; }
    public double X { get; set; }
    public double Y { get; set; }
}

/// <summary>How an edge between two nodes should be rendered.</summary>
public enum FlowEdgeKind
{
    /// <summary>Straight, neutral colour. Default flow.</summary>
    Sequential,

    /// <summary>Curved back-arc from EndLoop to Loop head.</summary>
    LoopBack,

    /// <summary>Curved back-arc from EndForEach to ForEach head.</summary>
    ForEachBack,

    /// <summary>One of the Branch cases — labelled with the case summary.</summary>
    BranchCase,

    /// <summary>Branch's default fallback — labelled "else".</summary>
    BranchDefault,

    /// <summary>OnErrorLabel jump — dashed red.</summary>
    OnError,
}

/// <summary>A directed edge from one node to another, plus an optional caption.</summary>
public sealed class FlowEdge
{
    public int FromIndex { get; }
    public int ToIndex { get; }
    public FlowEdgeKind Kind { get; }
    public string Caption { get; }

    public FlowEdge(int from, int to, FlowEdgeKind kind, string caption)
    {
        FromIndex = from;
        ToIndex = to;
        Kind = kind;
        Caption = caption ?? "";
    }
}
