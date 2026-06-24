using EAPSimulator.Core.Protocols.SecsGem.AutoReply;
using Xunit;

namespace EAPSimulator.Core.Tests;

/// <summary>
/// Tests for the pure-data flow layout. Exercises edge generation (sequential, loop-back,
/// foreach-back, branch cases, error jump) — the UI canvas is just a thin renderer of these.
/// </summary>
public class ScenarioFlowLayoutTests
{
    [Fact]
    public void Sequential_StepsHaveOneEdgeEach()
    {
        var scenario = new ScenarioDefinition
        {
            Steps =
            {
                new ScenarioStep { Kind = ScenarioStepKind.Send },
                new ScenarioStep { Kind = ScenarioStepKind.Delay },
                new ScenarioStep { Kind = ScenarioStepKind.Log },
            },
        };

        var layout = ScenarioFlowLayout.Build(scenario);
        Assert.Equal(3, layout.Nodes.Count);
        // 2 sequential edges (0→1, 1→2). The terminal node has no outgoing edge.
        Assert.Equal(2, layout.Edges.Count);
        Assert.All(layout.Edges, e => Assert.Equal(FlowEdgeKind.Sequential, e.Kind));
        Assert.Equal((0, 1), (layout.Edges[0].FromIndex, layout.Edges[0].ToIndex));
        Assert.Equal((1, 2), (layout.Edges[1].FromIndex, layout.Edges[1].ToIndex));
    }

    [Fact]
    public void Loop_GeneratesBackEdge()
    {
        var scenario = new ScenarioDefinition
        {
            Steps =
            {
                new ScenarioStep { Kind = ScenarioStepKind.Loop, LoopId = "L1", LoopTimes = 3 },
                new ScenarioStep { Kind = ScenarioStepKind.Send },
                new ScenarioStep { Kind = ScenarioStepKind.EndLoop, LoopId = "L1" },
            },
        };

        var layout = ScenarioFlowLayout.Build(scenario);
        // 0→1, 1→2 sequential, 2→0 LoopBack. (Last step has no further sequential edge.)
        Assert.Contains(layout.Edges, e => e.FromIndex == 2 && e.ToIndex == 0 && e.Kind == FlowEdgeKind.LoopBack);
        Assert.Equal(3, layout.Edges.Count);
    }

    [Fact]
    public void ForEach_GeneratesBackEdge()
    {
        var scenario = new ScenarioDefinition
        {
            Steps =
            {
                new ScenarioStep { Kind = ScenarioStepKind.ForEach, ForEachId = "F1" },
                new ScenarioStep { Kind = ScenarioStepKind.Send },
                new ScenarioStep { Kind = ScenarioStepKind.EndForEach, ForEachId = "F1" },
            },
        };

        var layout = ScenarioFlowLayout.Build(scenario);
        Assert.Contains(layout.Edges, e => e.FromIndex == 2 && e.ToIndex == 0 && e.Kind == FlowEdgeKind.ForEachBack);
    }

    [Fact]
    public void Branch_ProducesCaseAndDefaultEdges_NoFallthroughWhenDefaultSet()
    {
        var scenario = new ScenarioDefinition
        {
            Steps =
            {
                new ScenarioStep
                {
                    Kind = ScenarioStepKind.Branch,
                    Cases = { new BranchCase { TargetLabel = "ok" } },
                    DefaultLabel = "fail",
                },
                new ScenarioStep { Kind = ScenarioStepKind.Send, Label = "ok" },
                new ScenarioStep { Kind = ScenarioStepKind.Send, Label = "fail" },
            },
        };

        var layout = ScenarioFlowLayout.Build(scenario);
        // Branch has one BranchCase edge to "ok" (idx 1), one BranchDefault edge to "fail" (idx 2),
        // and NO sequential fall-through (DefaultLabel set).
        Assert.Contains(layout.Edges, e => e.FromIndex == 0 && e.ToIndex == 1 && e.Kind == FlowEdgeKind.BranchCase);
        Assert.Contains(layout.Edges, e => e.FromIndex == 0 && e.ToIndex == 2 && e.Kind == FlowEdgeKind.BranchDefault);
        Assert.DoesNotContain(layout.Edges, e => e.FromIndex == 0 && e.Kind == FlowEdgeKind.Sequential);
    }

    [Fact]
    public void Branch_WithoutDefault_FallsThroughToNextStep()
    {
        var scenario = new ScenarioDefinition
        {
            Steps =
            {
                new ScenarioStep
                {
                    Kind = ScenarioStepKind.Branch,
                    Cases = { new BranchCase { TargetLabel = "ok" } },
                },
                new ScenarioStep { Kind = ScenarioStepKind.Send }, // fall-through target
                new ScenarioStep { Kind = ScenarioStepKind.Send, Label = "ok" },
            },
        };

        var layout = ScenarioFlowLayout.Build(scenario);
        // With no default, fall-through to next step is shown via a Sequential edge.
        Assert.Contains(layout.Edges, e => e.FromIndex == 0 && e.ToIndex == 1 && e.Kind == FlowEdgeKind.Sequential);
        Assert.Contains(layout.Edges, e => e.FromIndex == 0 && e.ToIndex == 2 && e.Kind == FlowEdgeKind.BranchCase);
    }

    [Fact]
    public void OnErrorLabel_AddsDashedRedEdge()
    {
        var scenario = new ScenarioDefinition
        {
            Steps =
            {
                new ScenarioStep { Kind = ScenarioStepKind.Send, OnErrorLabel = "rescue" },
                new ScenarioStep { Kind = ScenarioStepKind.Log, Label = "rescue" },
            },
        };

        var layout = ScenarioFlowLayout.Build(scenario);
        Assert.Contains(layout.Edges, e => e.FromIndex == 0 && e.ToIndex == 1 && e.Kind == FlowEdgeKind.OnError);
    }

    [Fact]
    public void OverrideXY_RespectedOnMatchingStepIndex()
    {
        var scenario = new ScenarioDefinition
        {
            Steps =
            {
                new ScenarioStep { Kind = ScenarioStepKind.Send },
                new ScenarioStep { Kind = ScenarioStepKind.Delay },
            },
        };
        var overrides = new Dictionary<int, (double X, double Y)> { [1] = (300, 100) };

        var layout = ScenarioFlowLayout.Build(scenario, overrides);
        Assert.Equal(300, layout.Nodes[1].X);
        Assert.Equal(100, layout.Nodes[1].Y);
        // Step 0 unaffected — still at default column position.
        Assert.Equal(0, layout.Nodes[0].X);
    }

    [Fact]
    public void Branch_CaseIndex_PreservedOnEdges()
    {
        // The canvas needs to know which Cases[i] each BranchCase edge represents so dragging
        // the edge updates the right TargetLabel. Verify the index round-trips through layout.
        var scenario = new ScenarioDefinition
        {
            Steps =
            {
                new ScenarioStep
                {
                    Kind = ScenarioStepKind.Branch,
                    Cases =
                    {
                        new BranchCase { TargetLabel = "a" },
                        new BranchCase { TargetLabel = "b" },
                    },
                },
                new ScenarioStep { Kind = ScenarioStepKind.Send, Label = "a" },
                new ScenarioStep { Kind = ScenarioStepKind.Send, Label = "b" },
            },
        };
        var layout = ScenarioFlowLayout.Build(scenario);
        var branchEdges = layout.Edges.Where(e => e.Kind == FlowEdgeKind.BranchCase).ToList();
        Assert.Equal(2, branchEdges.Count);
        // Case 0 → label "a" → step 1; case 1 → label "b" → step 2.
        Assert.Equal(0, branchEdges.Single(e => e.ToIndex == 1).CaseIndex);
        Assert.Equal(1, branchEdges.Single(e => e.ToIndex == 2).CaseIndex);
    }

    [Fact]
    public void NonBranchEdges_HaveCaseIndexMinusOne()
    {
        // Anything that isn't a BranchCase should report -1 — the edge editor uses this to
        // decide whether to update Cases[i] or DefaultLabel / OnErrorLabel.
        var scenario = new ScenarioDefinition
        {
            Steps =
            {
                new ScenarioStep { Kind = ScenarioStepKind.Send, OnErrorLabel = "rescue" },
                new ScenarioStep { Kind = ScenarioStepKind.Log, Label = "rescue" },
            },
        };
        var layout = ScenarioFlowLayout.Build(scenario);
        Assert.All(layout.Edges, e => Assert.Equal(-1, e.CaseIndex));
    }
}
