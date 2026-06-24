using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using EAPSimulator.Core.Protocols.SecsGem.AutoReply;
using EAPSimulator.UI.ViewModels;
using System.Collections.Specialized;
using System.ComponentModel;
using AvPath = Avalonia.Controls.Shapes.Path;

namespace EAPSimulator.UI.Controls;

/// <summary>
/// Renders a scenario as a directed flow graph: one rectangle per step plus arrows for
/// sequential flow, branch cases, loop / foreach back-arcs and OnError jumps.
///
/// <para>The control is a thin renderer over <see cref="ScenarioFlowLayout"/>. Layout is
/// computed in <c>EAPSimulator.Core</c> and only persisted x/y overrides come back from the
/// VM (via <see cref="ScenarioViewModel.LayoutOverrides"/>) — so the canvas is testable
/// without a UI, and dragging just mutates that dictionary.</para>
///
/// <para>Why a UserControl rather than a templated control: this view is used in exactly one
/// place (the scenario tab), tightly coupled to <see cref="ScenarioViewModel"/>, and small
/// enough that exposing styling slots would be ceremony without payoff.</para>
/// </summary>
public partial class ScenarioFlowCanvas : UserControl
{
    private const double NodePadding = 8;
    private static readonly IBrush SequentialBrush = new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90));
    private static readonly IBrush LoopBackBrush = new SolidColorBrush(Color.FromRgb(0x74, 0xC7, 0xEC));
    private static readonly IBrush ForEachBackBrush = new SolidColorBrush(Color.FromRgb(0x94, 0xE2, 0xD5));
    private static readonly IBrush BranchBrush = new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8));
    private static readonly IBrush BranchDefaultBrush = new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4));
    private static readonly IBrush OnErrorBrush = new SolidColorBrush(Color.FromRgb(0xE9, 0x4F, 0x4F));

    /// <summary>
    /// Backing scenario VM. The canvas listens to its <see cref="ScenarioViewModel.Steps"/>
    /// collection and to per-step property changes that affect topology (Label, LoopId,
    /// ForEachId, Branch cases, OnErrorLabel) so the flow rebuilds automatically.
    /// </summary>
    public static readonly StyledProperty<ScenarioViewModel?> ScenarioProperty =
        AvaloniaProperty.Register<ScenarioFlowCanvas, ScenarioViewModel?>(nameof(Scenario));

    public ScenarioViewModel? Scenario
    {
        get => GetValue(ScenarioProperty);
        set => SetValue(ScenarioProperty, value);
    }

    public static readonly StyledProperty<ScenarioStepViewModel?> SelectedStepProperty =
        AvaloniaProperty.Register<ScenarioFlowCanvas, ScenarioStepViewModel?>(
            nameof(SelectedStep), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public ScenarioStepViewModel? SelectedStep
    {
        get => GetValue(SelectedStepProperty);
        set => SetValue(SelectedStepProperty, value);
    }

    /// <summary>
    /// Step index the debugger is paused on, or -1 when not paused. The canvas paints a
    /// distinct outline on that node so the user can see where execution is parked.
    /// </summary>
    public static readonly StyledProperty<int> PausedStepIndexProperty =
        AvaloniaProperty.Register<ScenarioFlowCanvas, int>(nameof(PausedStepIndex), defaultValue: -1);

    public int PausedStepIndex
    {
        get => GetValue(PausedStepIndexProperty);
        set => SetValue(PausedStepIndexProperty, value);
    }

    private readonly Canvas _canvas;
    private readonly ScrollViewer _scroll;
    private readonly Dictionary<int, Border> _nodeShells = new();
    /// <summary>Node bounding rects keyed by step index — used by edge-drag hit-testing.</summary>
    private readonly Dictionary<int, Rect> _nodeBounds = new();
    private ScenarioViewModel? _wiredScenario;

    // Drag state
    private Border? _draggingNode;
    private int _draggingStepIndex = -1;
    private Point _dragStart;
    private double _dragOrigX;
    private double _dragOrigY;

    // Edge re-targeting state: when an edge thumb is being dragged, this carries the source
    // step + which kind of edge we're editing so PointerReleased knows what to mutate.
    private Ellipse? _draggingThumb;
    private Line? _ghostLine;
    private int _edgeSourceStep = -1;
    private int _edgeCaseIndex = -1;     // -1 = not a BranchCase edge
    private FlowEdgeKind _edgeKind;

    public ScenarioFlowCanvas()
    {
        _canvas = new Canvas
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
            // Big enough by default; Auto-grows as nodes get dragged outwards.
            Width = 2000,
            Height = 2000,
        };
        _scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _canvas,
        };
        Content = _scroll;

        PropertyChanged += OnPropertyChanged;
    }

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ScenarioProperty)
        {
            UnwireScenario(_wiredScenario);
            _wiredScenario = Scenario;
            WireScenario(_wiredScenario);
            Rebuild();
        }
        else if (e.Property == SelectedStepProperty)
        {
            HighlightSelected();
        }
        else if (e.Property == PausedStepIndexProperty)
        {
            HighlightSelected(); // re-derives borders, including the paused-on outline
        }
    }

    private void WireScenario(ScenarioViewModel? vm)
    {
        if (vm == null) return;
        vm.Steps.CollectionChanged += OnStepsChanged;
        foreach (var s in vm.Steps)
            s.PropertyChanged += OnStepPropertyChanged;
    }

    private void UnwireScenario(ScenarioViewModel? vm)
    {
        if (vm == null) return;
        vm.Steps.CollectionChanged -= OnStepsChanged;
        foreach (var s in vm.Steps)
            s.PropertyChanged -= OnStepPropertyChanged;
    }

    private void OnStepsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (ScenarioStepViewModel s in e.NewItems)
                s.PropertyChanged += OnStepPropertyChanged;
        if (e.OldItems != null)
            foreach (ScenarioStepViewModel s in e.OldItems)
                s.PropertyChanged -= OnStepPropertyChanged;
        Rebuild();
    }

    private void OnStepPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Property names that change the graph shape — anything else (TimeoutMs, DelayMs, …)
        // doesn't need a re-layout, just a label refresh, which DisplayText already drives.
        switch (e.PropertyName)
        {
            case nameof(ScenarioStepViewModel.Kind):
            case nameof(ScenarioStepViewModel.Label):
            case nameof(ScenarioStepViewModel.LoopId):
            case nameof(ScenarioStepViewModel.ForEachId):
            case nameof(ScenarioStepViewModel.DefaultLabel):
            case nameof(ScenarioStepViewModel.OnErrorLabel):
            case nameof(ScenarioStepViewModel.IsBreakpoint):
                Rebuild();
                break;
        }
    }

    /// <summary>
    /// Re-derive nodes + edges from the bound scenario and re-populate the canvas. Idempotent —
    /// safe to call after any step list / topology mutation.
    /// </summary>
    public void Rebuild()
    {
        _canvas.Children.Clear();
        _nodeShells.Clear();
        _nodeBounds.Clear();
        _sideFan.Clear();
        var vm = Scenario;
        if (vm == null) return;

        var def = vm.ToModelLayoutPreview();
        var overrides = vm.LayoutOverrides;
        var layout = ScenarioFlowLayout.Build(def, overrides);

        // Pre-compute the anchor side each edge will use. We need both endpoints' picks up
        // front so we can detect "in and out edges on the same side of the same node" and
        // route one of them away — otherwise the entry arrow and the exit arrow stack on top
        // of each other and you can't tell which direction is which.
        var pickedSides = ResolveAnchorSides(layout);

        // Edges first so node rectangles render on top.
        foreach (var edge in layout.Edges)
            DrawEdge(layout, edge, pickedSides);

        foreach (var node in layout.Nodes)
            DrawNode(vm, node);

        // Grow the canvas to fit the rightmost / bottommost node.
        double maxX = 0, maxY = 0;
        foreach (var n in layout.Nodes)
        {
            maxX = Math.Max(maxX, n.X + ScenarioFlowLayout.NodeWidth);
            maxY = Math.Max(maxY, n.Y + ScenarioFlowLayout.NodeHeight);
        }
        _canvas.Width = Math.Max(2000, maxX + 200);
        _canvas.Height = Math.Max(800, maxY + 200);

        HighlightSelected();
    }

    /// <summary>
    /// First pass: pick each edge's tentative (fromSide, toSide) via <see cref="PickAnchors"/>.
    /// Second pass: for every node, if its incoming and outgoing edges land on the same side,
    /// reroute one of them to the orthogonal side that's closer to the other endpoint —
    /// turning a stacked entry/exit pair into a clear in-from-left / out-to-bottom layout.
    /// </summary>
    private static Dictionary<int, (AnchorSide From, AnchorSide To)> ResolveAnchorSides(ScenarioFlowLayoutResult layout)
    {
        var sides = new Dictionary<int, (AnchorSide From, AnchorSide To)>();
        for (int i = 0; i < layout.Edges.Count; i++)
        {
            var e = layout.Edges[i];
            var (fs, ts) = PickAnchors(layout.Nodes[e.FromIndex], layout.Nodes[e.ToIndex], e.Kind);
            sides[i] = (fs, ts);
        }

        // Build per-node entry/exit side maps so we can detect collisions cheaply.
        var entrySidesPerNode = new Dictionary<int, List<int>>();   // stepIndex -> edge indices entering
        var exitSidesPerNode = new Dictionary<int, List<int>>();
        for (int i = 0; i < layout.Edges.Count; i++)
        {
            var e = layout.Edges[i];
            if (!exitSidesPerNode.TryGetValue(e.FromIndex, out var outs))
                exitSidesPerNode[e.FromIndex] = outs = new List<int>();
            outs.Add(i);
            if (!entrySidesPerNode.TryGetValue(e.ToIndex, out var ins))
                entrySidesPerNode[e.ToIndex] = ins = new List<int>();
            ins.Add(i);
        }

        // For each node that has both an entry and an exit landing on the same side, divert
        // the entry to the orthogonal axis closer to its peer. We touch the entry rather than
        // the exit because outgoing direction is usually more meaningful to the reader
        // ("which way does this step send control next?").
        foreach (var (nodeIdx, entryEdges) in entrySidesPerNode)
        {
            if (!exitSidesPerNode.TryGetValue(nodeIdx, out var exitEdges)) continue;
            foreach (var entryEdgeIdx in entryEdges)
            {
                var entrySide = sides[entryEdgeIdx].To;
                bool collides = exitEdges.Any(ex => ex != entryEdgeIdx && sides[ex].From == entrySide);
                if (!collides) continue;
                // Re-route this entry to a side not already used by an exit. Prefer the side
                // perpendicular to the current one for the most natural turn.
                var occupied = new HashSet<AnchorSide>(exitEdges.Select(ex => sides[ex].From));
                AnchorSide? candidate = entrySide switch
                {
                    AnchorSide.Left or AnchorSide.Right => occupied.Contains(AnchorSide.Top) ? AnchorSide.Bottom : AnchorSide.Top,
                    AnchorSide.Top or AnchorSide.Bottom => occupied.Contains(AnchorSide.Left) ? AnchorSide.Right : AnchorSide.Left,
                    _ => null,
                };
                if (candidate is { } newSide)
                {
                    var s = sides[entryEdgeIdx];
                    sides[entryEdgeIdx] = (s.From, newSide);
                }
            }
        }
        return sides;
    }

    private void DrawNode(ScenarioViewModel vm, FlowNode node)
    {
        var (fill, fg) = NodeColors(node.Kind);
        var label = new TextBlock
        {
            Text = node.Step.DisplayText,
            Foreground = fg,
            FontSize = 11,
            FontFamily = new FontFamily("Consolas,Microsoft YaHei,monospace"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(NodePadding, 4, NodePadding, 4),
        };
        var idxText = new TextBlock
        {
            Text = node.StepIndex.ToString(),
            Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
            FontSize = 10,
            FontFamily = new FontFamily("Consolas,monospace"),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Width = 22,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(4, 0, 4, 0),
        };
        var content = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(idxText, Dock.Left);
        content.Children.Add(idxText);
        content.Children.Add(label);

        var shell = new Border
        {
            Width = ScenarioFlowLayout.NodeWidth,
            Height = ScenarioFlowLayout.NodeHeight,
            CornerRadius = new CornerRadius(4),
            Background = fill,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            BorderThickness = new Thickness(1),
            Child = content,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        Canvas.SetLeft(shell, node.X);
        Canvas.SetTop(shell, node.Y);

        // Right-click menu: insert before / after, delete. Lets the user grow the scenario from
        // the canvas itself instead of bouncing back to the toolbar.
        shell.ContextMenu = BuildNodeContextMenu(vm, node.StepIndex);

        // Pointer wiring: click selects the step, drag moves the node.
        shell.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(shell).Properties.IsLeftButtonPressed)
            {
                if (node.StepIndex >= 0 && node.StepIndex < vm.Steps.Count)
                    SelectedStep = vm.Steps[node.StepIndex];
                _draggingNode = shell;
                _draggingStepIndex = node.StepIndex;
                _dragStart = e.GetPosition(_canvas);
                _dragOrigX = Canvas.GetLeft(shell);
                _dragOrigY = Canvas.GetTop(shell);
                e.Pointer.Capture(shell);
                e.Handled = true;
            }
        };
        shell.PointerMoved += (_, e) =>
        {
            if (_draggingNode != shell) return;
            var pos = e.GetPosition(_canvas);
            var nx = Math.Max(0, _dragOrigX + (pos.X - _dragStart.X));
            var ny = Math.Max(0, _dragOrigY + (pos.Y - _dragStart.Y));
            Canvas.SetLeft(shell, nx);
            Canvas.SetTop(shell, ny);
        };
        shell.PointerReleased += (_, e) =>
        {
            if (_draggingNode != shell) return;
            var nx = Canvas.GetLeft(shell);
            var ny = Canvas.GetTop(shell);
            // Only persist when the user moved more than a pixel — guards against click-only.
            if (Math.Abs(nx - _dragOrigX) > 1 || Math.Abs(ny - _dragOrigY) > 1)
            {
                vm.LayoutOverrides[_draggingStepIndex] = (nx, ny);
                Rebuild();  // re-route edges to the new position
            }
            _draggingNode = null;
            _draggingStepIndex = -1;
            e.Pointer.Capture(null);
        };

        _canvas.Children.Add(shell);
        _nodeShells[node.StepIndex] = shell;
        _nodeBounds[node.StepIndex] = new Rect(node.X, node.Y,
            ScenarioFlowLayout.NodeWidth, ScenarioFlowLayout.NodeHeight);

        // Breakpoint dot: small red disc in the top-left corner of nodes flagged IsBreakpoint.
        // Drawn after the shell so it renders on top.
        if (node.StepIndex < vm.Steps.Count && vm.Steps[node.StepIndex].IsBreakpoint)
        {
            var dot = new Ellipse
            {
                Width = 9,
                Height = 9,
                Fill = new SolidColorBrush(Color.FromRgb(0xE9, 0x4F, 0x4F)),
                Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
                StrokeThickness = 1,
                ZIndex = 4,
            };
            Canvas.SetLeft(dot, node.X - 4);
            Canvas.SetTop(dot, node.Y - 4);
            _canvas.Children.Add(dot);
        }
    }

    /// <summary>
    /// Side of a node rectangle where an edge anchors. Encodes the outward normal direction
    /// the line / curve should leave or enter at — used to keep arrows perpendicular to the
    /// rectangle and to compute bezier control points that don't backtrack into the box.
    /// </summary>
    private enum AnchorSide { Top, Right, Bottom, Left }

    /// <summary>
    /// Pick anchor sides for an edge based on the relative geometry of the two nodes.
    /// Greedy: whichever axis has the larger gap chooses the side; ties prefer vertical
    /// (matches the column-style default layout). Back-arcs are forced to right-side so the
    /// curve has room to bow out without crossing the spine of the diagram.
    /// </summary>
    private static (AnchorSide fromSide, AnchorSide toSide) PickAnchors(FlowNode from, FlowNode to, FlowEdgeKind kind)
    {
        if (kind is FlowEdgeKind.LoopBack or FlowEdgeKind.ForEachBack)
            return (AnchorSide.Right, AnchorSide.Right);

        var fromCenterX = from.X + ScenarioFlowLayout.NodeWidth / 2;
        var fromCenterY = from.Y + ScenarioFlowLayout.NodeHeight / 2;
        var toCenterX = to.X + ScenarioFlowLayout.NodeWidth / 2;
        var toCenterY = to.Y + ScenarioFlowLayout.NodeHeight / 2;
        var dx = toCenterX - fromCenterX;
        var dy = toCenterY - fromCenterY;

        // Bias slightly toward vertical so a small horizontal offset (e.g. user nudged a node
        // 30px right) doesn't immediately flip the wire to side-anchored — that flicker is more
        // distracting than a slightly off-axis arrow.
        if (Math.Abs(dx) > Math.Abs(dy) * 1.5)
        {
            return dx >= 0
                ? (AnchorSide.Right, AnchorSide.Left)
                : (AnchorSide.Left, AnchorSide.Right);
        }
        return dy >= 0
            ? (AnchorSide.Bottom, AnchorSide.Top)
            : (AnchorSide.Top, AnchorSide.Bottom);
    }

    /// <summary>
    /// Compute the anchor point on a node's rectangle for a given side, plus the outward
    /// normal vector. <paramref name="rank"/> is used to fan multiple edges out along the
    /// same side (e.g. two Branch cases leaving the bottom of one node) so they don't
    /// overlap visually.
    /// </summary>
    private static (Point Point, Point Normal) AnchorOn(FlowNode node, AnchorSide side, double rank)
    {
        // rank: 0 = centre; ±1, ±2 … step outwards by 14px along the side.
        const double Spread = 14;
        var w = ScenarioFlowLayout.NodeWidth;
        var h = ScenarioFlowLayout.NodeHeight;
        switch (side)
        {
            case AnchorSide.Top:
                return (new Point(node.X + w / 2 + rank * Spread, node.Y), new Point(0, -1));
            case AnchorSide.Bottom:
                return (new Point(node.X + w / 2 + rank * Spread, node.Y + h), new Point(0, 1));
            case AnchorSide.Left:
                return (new Point(node.X, node.Y + h / 2 + rank * Spread), new Point(-1, 0));
            case AnchorSide.Right:
            default:
                return (new Point(node.X + w, node.Y + h / 2 + rank * Spread), new Point(1, 0));
        }
    }

    /// <summary>
    /// Single per-(stepIdx, side) counter shared between incoming and outgoing edges. Earlier
    /// we kept separate in/out counters, but that gave an entry edge and an exit edge on the
    /// same side identical rank=0 → they landed at the same anchor point and the arrow tips
    /// stacked on top of each other. One pool ensures every anchor on a side gets a unique
    /// rank regardless of direction.
    /// </summary>
    private readonly Dictionary<(int, AnchorSide), int> _sideFan = new();

    /// <summary>
    /// Allocate the next rank for an anchor on (stepIdx, side). Ranks step outward from centre:
    /// 0, +1, -1, +2, -2, … Each rank corresponds to a 14px offset along the side, so multiple
    /// edges sharing a side fan out symmetrically.
    /// </summary>
    private double NextRank(int stepIdx, AnchorSide side)
    {
        var key = (stepIdx, side);
        _sideFan.TryGetValue(key, out var n);
        _sideFan[key] = n + 1;
        return n switch
        {
            0 => 0,
            _ => ((n + 1) / 2) * (n % 2 == 1 ? 1 : -1),
        };
    }

    private void DrawEdge(ScenarioFlowLayoutResult layout, FlowEdge edge,
        Dictionary<int, (AnchorSide From, AnchorSide To)> pickedSides)
    {
        var from = layout.Nodes[edge.FromIndex];
        var to = layout.Nodes[edge.ToIndex];
        bool isBackArc = edge.ToIndex < edge.FromIndex
            && (edge.Kind == FlowEdgeKind.LoopBack || edge.Kind == FlowEdgeKind.ForEachBack);

        // 1) Look up the anchor sides chosen by the pre-pass (which already broke entry/exit
        //    ties so they don't land on the same side).
        var edgeIdx = layout.Edges.IndexOf(edge);
        var (fromSide, toSide) = pickedSides[edgeIdx];

        // 2) Per-side fan ranks so multiple edges on the same side don't overlap, regardless
        //    of whether they are entries or exits.
        var fromRank = NextRank(edge.FromIndex, fromSide);
        var toRank = NextRank(edge.ToIndex, toSide);

        var (p1, n1) = AnchorOn(from, fromSide, fromRank);
        var (p2, n2) = AnchorOn(to, toSide, toRank);

        var brush = EdgeBrush(edge.Kind);
        const double thickness = 1.5;
        var dash = edge.Kind == FlowEdgeKind.OnError ? new AvaloniaList<double>(new[] { 4.0, 3.0 }) : null;

        // 3) Build the geometry.
        //    - Pure-axial short hops use a straight Line (cheap, crisp).
        //    - Back-arcs bow out along the +X side.
        //    - Everything else uses a cubic bezier whose control points "exit"/"enter" along the
        //      anchor normals; the longer the gap the further out the controls reach, which
        //      naturally routes the wire around any rectangles in between.
        Shape shape;
        if (isBackArc)
        {
            var bend = 60 + Math.Abs(p1.Y - p2.Y) * 0.25 + Math.Abs(fromRank) * 6;
            shape = new AvPath
            {
                Stroke = brush,
                StrokeThickness = thickness,
                StrokeDashArray = dash,
                Data = new PathGeometry
                {
                    Figures = new PathFigures
                    {
                        new PathFigure
                        {
                            StartPoint = p1,
                            IsClosed = false,
                            Segments = new PathSegments
                            {
                                new BezierSegment
                                {
                                    Point1 = new Point(p1.X + bend, p1.Y),
                                    Point2 = new Point(p2.X + bend, p2.Y),
                                    Point3 = p2,
                                },
                            },
                        },
                    },
                },
            };
        }
        else if (CanUseStraightLine(p1, n1, p2, n2) && !StraightLineHitsObstacle(layout, edge, p1, p2))
        {
            shape = new Line
            {
                StartPoint = p1,
                EndPoint = p2,
                Stroke = brush,
                StrokeThickness = thickness,
                StrokeDashArray = dash,
            };
        }
        else
        {
            // Cubic bezier with control points extruded along the anchor normals.
            // Reach scales with the gap so short jumps stay tight, long ones bow generously.
            var gap = Math.Max(Math.Abs(p2.X - p1.X), Math.Abs(p2.Y - p1.Y));
            var reach = Math.Clamp(gap * 0.4, 40, 160);
            var c1 = new Point(p1.X + n1.X * reach, p1.Y + n1.Y * reach);
            var c2 = new Point(p2.X + n2.X * reach, p2.Y + n2.Y * reach);

            // Obstacle avoidance: if any unrelated node sits on the straight line between
            // p1 and p2, nudge the bezier sideways so the curve clearly bows around it.
            // The offset direction is chosen to point away from the obstacle's centre.
            var offset = ComputeAvoidanceOffset(layout, edge, p1, p2);
            if (offset != default)
            {
                c1 = new Point(c1.X + offset.X, c1.Y + offset.Y);
                c2 = new Point(c2.X + offset.X, c2.Y + offset.Y);
            }

            shape = new AvPath
            {
                Stroke = brush,
                StrokeThickness = thickness,
                StrokeDashArray = dash,
                Data = new PathGeometry
                {
                    Figures = new PathFigures
                    {
                        new PathFigure
                        {
                            StartPoint = p1,
                            IsClosed = false,
                            Segments = new PathSegments
                            {
                                new BezierSegment { Point1 = c1, Point2 = c2, Point3 = p2 },
                            },
                        },
                    },
                },
            };
        }
        _canvas.Children.Add(shape);

        // 4) Arrowhead — perpendicular to the destination anchor normal.
        DrawArrowHead(p2, n2, brush);

        // 5) Caption — placed a bit off the source anchor so it stays clear of the line.
        if (!string.IsNullOrEmpty(edge.Caption))
        {
            // Offset the caption along a vector that's mostly along the line direction so it
            // doesn't sit on top of the curve. For side-anchored edges we drop it just below
            // the source; for top/bottom-anchored ones we shift it sideways.
            double capX, capY;
            if (n1.Y != 0) { capX = p1.X + 6; capY = p1.Y + n1.Y * 6; }
            else            { capX = p1.X + n1.X * 6; capY = p1.Y + 4; }
            var caption = new TextBlock
            {
                Text = edge.Caption,
                Foreground = brush,
                FontSize = 10,
                FontFamily = new FontFamily("Consolas,monospace"),
            };
            Canvas.SetLeft(caption, capX);
            Canvas.SetTop(caption, capY);
            _canvas.Children.Add(caption);
        }

        // 6) Drag thumb on the destination end of editable edges. The thumb is a small disc
        //    the user can grab to reroute the edge to a different node. We only attach it to
        //    BranchCase / BranchDefault / OnError edges — sequential and loop-back edges are
        //    derived from the step ordering itself, dragging them wouldn't have anywhere to
        //    write the change.
        if (IsEditableEdge(edge.Kind))
            AddEdgeThumb(edge, p2, n2, brush);
    }

    private static bool IsEditableEdge(FlowEdgeKind kind) =>
        kind is FlowEdgeKind.BranchCase or FlowEdgeKind.BranchDefault or FlowEdgeKind.OnError;

    private void AddEdgeThumb(FlowEdge edge, Point tip, Point normal, IBrush brush)
    {
        // Position the thumb just outside the destination node so it doesn't overlap the arrow
        // head. We use the same outward-normal offset used by DrawArrowHead.
        const double Offset = 10;
        const double Size = 9;
        var cx = tip.X + normal.X * Offset;
        var cy = tip.Y + normal.Y * Offset;
        var thumb = new Ellipse
        {
            Width = Size,
            Height = Size,
            Stroke = brush,
            StrokeThickness = 1.5,
            Fill = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
            Cursor = new Cursor(StandardCursorType.Hand),
            ZIndex = 6,
        };
        Canvas.SetLeft(thumb, cx - Size / 2);
        Canvas.SetTop(thumb, cy - Size / 2);
        ToolTip.SetTip(thumb, "拖动到目标节点以更改跳转;松开在空白处取消");

        thumb.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(thumb).Properties.IsLeftButtonPressed) return;
            _draggingThumb = thumb;
            _edgeSourceStep = edge.FromIndex;
            _edgeCaseIndex = edge.CaseIndex;
            _edgeKind = edge.Kind;
            // Lay down a ghost line that follows the pointer.
            var src = AnchorCenter(_nodeBounds[edge.FromIndex]);
            _ghostLine = new Line
            {
                StartPoint = src,
                EndPoint = new Point(cx, cy),
                Stroke = brush,
                StrokeThickness = 2,
                StrokeDashArray = new AvaloniaList<double>(new[] { 5.0, 4.0 }),
                ZIndex = 7,
            };
            _canvas.Children.Add(_ghostLine);
            e.Pointer.Capture(thumb);
            e.Handled = true;
        };
        thumb.PointerMoved += (_, e) =>
        {
            if (_draggingThumb != thumb || _ghostLine == null) return;
            var pos = e.GetPosition(_canvas);
            _ghostLine.EndPoint = pos;
            // Highlight the node currently under the pointer (if any) by a brief border accent.
            HighlightDropTarget(pos);
        };
        thumb.PointerReleased += (_, e) =>
        {
            if (_draggingThumb != thumb) return;
            var pos = e.GetPosition(_canvas);
            var target = HitTestNode(pos);
            ClearDropTargetHighlight();
            if (_ghostLine != null) { _canvas.Children.Remove(_ghostLine); _ghostLine = null; }
            _draggingThumb = null;
            e.Pointer.Capture(null);
            if (target >= 0 && target != _edgeSourceStep)
                CommitEdgeRetarget(target);
        };
    }

    /// <summary>Centre point of a node rect — used as the ghost line's start during drag.</summary>
    private static Point AnchorCenter(Rect r) => new Point(r.X + r.Width / 2, r.Y + r.Height / 2);

    /// <summary>Return the step index whose bounding rect contains <paramref name="p"/>, or -1.</summary>
    private int HitTestNode(Point p)
    {
        foreach (var (idx, r) in _nodeBounds)
            if (r.Contains(p)) return idx;
        return -1;
    }

    private int _highlightedDropTarget = -1;

    private void HighlightDropTarget(Point p)
    {
        var hit = HitTestNode(p);
        if (hit == _highlightedDropTarget) return;
        ClearDropTargetHighlight();
        if (hit >= 0 && hit != _edgeSourceStep && _nodeShells.TryGetValue(hit, out var shell))
        {
            shell.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xB4, 0x54));
            shell.BorderThickness = new Thickness(2);
            _highlightedDropTarget = hit;
        }
    }

    private void ClearDropTargetHighlight()
    {
        if (_highlightedDropTarget < 0) return;
        if (_nodeShells.TryGetValue(_highlightedDropTarget, out var shell))
        {
            // Restore normal border. HighlightSelected() will repaint the green outline if this
            // node happens to be the SelectedStep.
            shell.BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
            shell.BorderThickness = new Thickness(1);
        }
        _highlightedDropTarget = -1;
        HighlightSelected();
    }

    /// <summary>
    /// Mutate the underlying scenario to point the edge at <paramref name="targetStepIdx"/>.
    /// Three kinds of edits are supported (mirrors <see cref="IsEditableEdge"/>):
    /// <list type="bullet">
    ///   <item>BranchCase → set the corresponding <c>Cases[i].TargetLabel</c></item>
    ///   <item>BranchDefault → set the step's <c>DefaultLabel</c></item>
    ///   <item>OnError → set the step's <c>OnErrorLabel</c></item>
    /// </list>
    /// If the target node has no Label, we auto-generate one (<c>L{stepIdx}</c>) so the edge
    /// has somewhere to resolve to. Otherwise the existing Label is reused.
    /// </summary>
    private void CommitEdgeRetarget(int targetStepIdx)
    {
        var vm = Scenario;
        if (vm == null) return;
        if (_edgeSourceStep < 0 || _edgeSourceStep >= vm.Steps.Count) return;
        if (targetStepIdx < 0 || targetStepIdx >= vm.Steps.Count) return;

        var srcStep = vm.Steps[_edgeSourceStep];
        var dstStep = vm.Steps[targetStepIdx];
        var label = string.IsNullOrEmpty(dstStep.Label) ? AutoLabel(targetStepIdx, vm) : dstStep.Label;
        if (dstStep.Label != label)
        {
            dstStep.Label = label;       // triggers RefreshAvailableLabels via ScenarioViewModel
        }

        switch (_edgeKind)
        {
            case FlowEdgeKind.BranchCase:
                if (_edgeCaseIndex >= 0 && _edgeCaseIndex < srcStep.Cases.Count)
                    srcStep.Cases[_edgeCaseIndex].TargetLabel = label;
                break;
            case FlowEdgeKind.BranchDefault:
                srcStep.DefaultLabel = label;
                break;
            case FlowEdgeKind.OnError:
                srcStep.OnErrorLabel = label;
                break;
        }
        // Branch case Summary / step display caches are derived; nudge them so the canvas
        // caption and ListBox refresh.
        srcStep.UpdateDisplayText();
        Rebuild();
    }

    /// <summary>Pick an unused short label name for a step that doesn't have one yet.</summary>
    private static string AutoLabel(int stepIdx, ScenarioViewModel vm)
    {
        var taken = new HashSet<string>(
            vm.Steps.Where(s => !string.IsNullOrEmpty(s.Label)).Select(s => s.Label),
            StringComparer.Ordinal);
        var prefix = $"L{stepIdx}";
        if (!taken.Contains(prefix)) return prefix;
        for (int i = 2; ; i++)
        {
            var candidate = $"{prefix}_{i}";
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    /// <summary>
    /// Lightweight wrapper around <see cref="SegmentIntersectsRect"/>: any unrelated node
    /// blocks the straight path? Used to demote a Sequential line to a bezier when the user
    /// has dragged nodes into the wire's way.
    /// </summary>
    private static bool StraightLineHitsObstacle(
        ScenarioFlowLayoutResult layout, FlowEdge edge, Point p1, Point p2)
    {
        const double Padding = 4;
        foreach (var node in layout.Nodes)
        {
            if (node.StepIndex == edge.FromIndex || node.StepIndex == edge.ToIndex) continue;
            var rect = new Rect(node.X - Padding, node.Y - Padding,
                ScenarioFlowLayout.NodeWidth + 2 * Padding,
                ScenarioFlowLayout.NodeHeight + 2 * Padding);
            if (SegmentIntersectsRect(p1, p2, rect)) return true;
        }
        return false;
    }

    /// <summary>
    /// True when the two anchors are roughly co-axial on the side they leave — e.g. both
    /// vertical anchors with nearly the same X coord — so a straight line looks fine and a
    /// bezier would be wasted curve.
    /// </summary>
    private static bool CanUseStraightLine(Point p1, Point n1, Point p2, Point n2)
    {
        // Opposite normals required (top↔bottom or left↔right) — otherwise the line would
        // exit the source in the wrong direction.
        var opposing = (n1.X == -n2.X && n1.Y == -n2.Y);
        if (!opposing) return false;
        // Vertical pair: nearly identical X
        if (n1.Y != 0 && Math.Abs(p1.X - p2.X) < 4) return true;
        // Horizontal pair: nearly identical Y
        if (n1.X != 0 && Math.Abs(p1.Y - p2.Y) < 4) return true;
        return false;
    }

    /// <summary>
    /// If a node other than the edge's endpoints intersects the straight segment p1→p2,
    /// return a perpendicular offset to apply to the bezier control points so the curve bows
    /// around it. Returns <see cref="default"/> (zero offset) when the path is clear.
    /// <para>This is intentionally a one-pass nudge rather than full A* routing: cheap, runs
    /// per-rebuild, handles the common "long jump grazes an intermediate step" case. Heavy
    /// scenarios with many overlaps will still look busy but won't have lines disappear
    /// behind nodes.</para>
    /// </summary>
    private static Point ComputeAvoidanceOffset(
        ScenarioFlowLayoutResult layout, FlowEdge edge, Point p1, Point p2)
    {
        const double Padding = 8;
        const double NudgeBase = 90;     // base sideways shift if a hit is found
        double dx = p2.X - p1.X;
        double dy = p2.Y - p1.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return default;
        // Perpendicular unit vector — sign is decided by which side of the line the obstacle
        // centre lies on (cross product sign).
        double nx = -dy / len;
        double ny = dx / len;

        double accumulatedSign = 0;
        int hitCount = 0;
        foreach (var node in layout.Nodes)
        {
            if (node.StepIndex == edge.FromIndex || node.StepIndex == edge.ToIndex) continue;
            var rect = new Rect(node.X - Padding, node.Y - Padding,
                ScenarioFlowLayout.NodeWidth + 2 * Padding,
                ScenarioFlowLayout.NodeHeight + 2 * Padding);
            if (!SegmentIntersectsRect(p1, p2, rect)) continue;
            // Decide which side of the line the node centre falls on.
            double cx = node.X + ScenarioFlowLayout.NodeWidth / 2;
            double cy = node.Y + ScenarioFlowLayout.NodeHeight / 2;
            double cross = (cx - p1.X) * dy - (cy - p1.Y) * dx;
            accumulatedSign += cross >= 0 ? -1 : 1;
            hitCount++;
        }
        if (hitCount == 0) return default;
        // Sign: push to the opposite side from the obstacle centre(s). If hits cancel out,
        // default to +1 (right of the line direction).
        double signed = accumulatedSign == 0 ? 1 : Math.Sign(accumulatedSign);
        double magnitude = NudgeBase + (hitCount - 1) * 30;
        return new Point(nx * signed * magnitude, ny * signed * magnitude);
    }

    /// <summary>
    /// Cheap segment-vs-rect intersection: true if the line p1→p2 enters the rectangle. Uses
    /// the Liang-Barsky-style parameter clipping; precise enough for "is this node in the way?".
    /// </summary>
    private static bool SegmentIntersectsRect(Point p1, Point p2, Rect r)
    {
        // Quick reject: both endpoints on the same outside half-plane.
        if (p1.X < r.X && p2.X < r.X) return false;
        if (p1.X > r.Right && p2.X > r.Right) return false;
        if (p1.Y < r.Y && p2.Y < r.Y) return false;
        if (p1.Y > r.Bottom && p2.Y > r.Bottom) return false;
        // If either endpoint is inside, it definitely intersects.
        if (r.Contains(p1) || r.Contains(p2)) return true;
        // Else clip against the rectangle bounds parametrically.
        double t0 = 0, t1 = 1;
        double dx = p2.X - p1.X, dy = p2.Y - p1.Y;
        double[] p = { -dx, dx, -dy, dy };
        double[] q = { p1.X - r.X, r.Right - p1.X, p1.Y - r.Y, r.Bottom - p1.Y };
        for (int i = 0; i < 4; i++)
        {
            if (p[i] == 0)
            {
                if (q[i] < 0) return false;
            }
            else
            {
                double t = q[i] / p[i];
                if (p[i] < 0) { if (t > t1) return false; if (t > t0) t0 = t; }
                else          { if (t < t0) return false; if (t < t1) t1 = t; }
            }
        }
        return true;
    }

    private void DrawArrowHead(Point tip, Point normal, IBrush brush)
    {
        // Tip sits on the destination rectangle's edge with `normal` pointing outwards (away
        // from the node centre). The arrow visually points into the node, so the triangle's
        // base must extend outward — bx,by = tip + normal*Len. Earlier this used `tip - normal*Len`
        // which put the base inside the rectangle, where DrawNode then painted over it; that's
        // why arrows disappeared after the smart-anchor refactor.
        const double Len = 9;
        const double Half = 4.5;
        var bx = tip.X + normal.X * Len;
        var by = tip.Y + normal.Y * Len;
        // Perpendicular vector (rotate normal 90°)
        var px = -normal.Y * Half;
        var py = normal.X * Half;
        var arrow = new Polygon
        {
            Points = new Points
            {
                tip,
                new Point(bx + px, by + py),
                new Point(bx - px, by - py),
            },
            Fill = brush,
            // Render the arrow above any node rectangle — without this, arrows whose tip sits on
            // a node edge can get visually clipped if rendering order ever puts the node on top.
            ZIndex = 5,
        };
        _canvas.Children.Add(arrow);
    }

    private static (IBrush fill, IBrush fg) NodeColors(ScenarioStepKind kind) => kind switch
    {
        ScenarioStepKind.Send         => (new SolidColorBrush(Color.FromRgb(0x1E, 0x4D, 0x2B)), new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1))),
        ScenarioStepKind.Receive      => (new SolidColorBrush(Color.FromRgb(0x1E, 0x3A, 0x5F)), new SolidColorBrush(Color.FromRgb(0x89, 0xB4, 0xFA))),
        ScenarioStepKind.Reply        => (new SolidColorBrush(Color.FromRgb(0x3D, 0x2B, 0x5C)), new SolidColorBrush(Color.FromRgb(0xCB, 0xA6, 0xF7))),
        ScenarioStepKind.Delay        => (new SolidColorBrush(Color.FromRgb(0x5C, 0x43, 0x26)), new SolidColorBrush(Color.FromRgb(0xFA, 0xB3, 0x87))),
        ScenarioStepKind.Log          => (new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)), new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4))),
        ScenarioStepKind.Branch       => (new SolidColorBrush(Color.FromRgb(0x5C, 0x2B, 0x3D)), new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8))),
        ScenarioStepKind.HostSend     => (new SolidColorBrush(Color.FromRgb(0x4A, 0x37, 0x1A)), new SolidColorBrush(Color.FromRgb(0xFF, 0xB4, 0x54))),
        ScenarioStepKind.HostReceive  => (new SolidColorBrush(Color.FromRgb(0x4A, 0x37, 0x1A)), new SolidColorBrush(Color.FromRgb(0xFF, 0xB4, 0x54))),
        ScenarioStepKind.SetVariable  => (new SolidColorBrush(Color.FromRgb(0x3A, 0x4A, 0x2C)), new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1))),
        ScenarioStepKind.Loop         => (new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x5C)), new SolidColorBrush(Color.FromRgb(0x74, 0xC7, 0xEC))),
        ScenarioStepKind.EndLoop      => (new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x5C)), new SolidColorBrush(Color.FromRgb(0x74, 0xC7, 0xEC))),
        ScenarioStepKind.ForEach      => (new SolidColorBrush(Color.FromRgb(0x2C, 0x5C, 0x4A)), new SolidColorBrush(Color.FromRgb(0x94, 0xE2, 0xD5))),
        ScenarioStepKind.EndForEach   => (new SolidColorBrush(Color.FromRgb(0x2C, 0x5C, 0x4A)), new SolidColorBrush(Color.FromRgb(0x94, 0xE2, 0xD5))),
        ScenarioStepKind.CallScenario => (new SolidColorBrush(Color.FromRgb(0x4A, 0x2C, 0x5C)), new SolidColorBrush(Color.FromRgb(0xCB, 0xA6, 0xF7))),
        _                             => (new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)), new SolidColorBrush(Colors.White)),
    };

    private static IBrush EdgeBrush(FlowEdgeKind kind) => kind switch
    {
        FlowEdgeKind.Sequential => SequentialBrush,
        FlowEdgeKind.LoopBack => LoopBackBrush,
        FlowEdgeKind.ForEachBack => ForEachBackBrush,
        FlowEdgeKind.BranchCase => BranchBrush,
        FlowEdgeKind.BranchDefault => BranchDefaultBrush,
        FlowEdgeKind.OnError => OnErrorBrush,
        _ => SequentialBrush,
    };

    private void HighlightSelected()
    {
        if (Scenario == null) return;
        var selIdx = SelectedStep == null ? -1 : Scenario.Steps.IndexOf(SelectedStep);
        var pausedIdx = PausedStepIndex;
        foreach (var (idx, shell) in _nodeShells)
        {
            // Priority: paused outline beats selection beats default. Paused = bright orange
            // so the debugger marker is obvious even when the selected step is the same one.
            if (idx == pausedIdx)
            {
                shell.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xB4, 0x54));
                shell.BorderThickness = new Thickness(3);
            }
            else if (idx == selIdx)
            {
                shell.BorderBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
                shell.BorderThickness = new Thickness(2);
            }
            else
            {
                shell.BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
                shell.BorderThickness = new Thickness(1);
            }
        }
    }

    /// <summary>Reset all node positions to the layout default (column).</summary>
    public void ResetLayout()
    {
        Scenario?.LayoutOverrides.Clear();
        Rebuild();
    }

    /// <summary>
    /// Build the right-click menu for a single node: insert before / after with a kind picker,
    /// plus delete. The kind list mirrors the toolbar order; selecting one calls back into the
    /// top-level <see cref="AutoReplyViewModel"/> which already owns the insert/delete helpers
    /// (they need the scenario lookup that ScenarioViewModel alone doesn't have).
    /// </summary>
    private static ContextMenu BuildNodeContextMenu(ScenarioViewModel vm, int stepIndex)
    {
        var menu = new ContextMenu();
        var parent = AutoReplyViewModel.Instance;
        menu.Items.Add(BuildInsertSubmenu("插入到此之前", k =>
        {
            parent?.InsertStepBefore(stepIndex, k);
        }));
        menu.Items.Add(BuildInsertSubmenu("插入到此之后", k =>
        {
            parent?.InsertStepAfter(stepIndex, k);
        }));
        menu.Items.Add(new Separator());
        var bp = new MenuItem { Header = "切换断点 🔴" };
        bp.Click += (_, _) =>
        {
            if (stepIndex >= 0 && stepIndex < vm.Steps.Count)
                vm.Steps[stepIndex].IsBreakpoint = !vm.Steps[stepIndex].IsBreakpoint;
        };
        menu.Items.Add(bp);
        menu.Items.Add(new Separator());
        var del = new MenuItem { Header = "删除此步骤" };
        del.Click += (_, _) =>
        {
            if (stepIndex >= 0 && stepIndex < vm.Steps.Count)
            {
                vm.Steps.RemoveAt(stepIndex);
                vm.RemoveLayoutOverrideAndShift(stepIndex);
            }
        };
        menu.Items.Add(del);
        return menu;
    }

    private static MenuItem BuildInsertSubmenu(string header, Action<ScenarioStepKind> onPick)
    {
        var root = new MenuItem { Header = header };
        // Order matches the toolbar so the muscle memory carries over.
        foreach (var k in new[]
        {
            ScenarioStepKind.Send, ScenarioStepKind.Receive, ScenarioStepKind.Reply,
            ScenarioStepKind.Delay, ScenarioStepKind.Log, ScenarioStepKind.Branch,
            ScenarioStepKind.SetVariable, ScenarioStepKind.Loop, ScenarioStepKind.EndLoop,
            ScenarioStepKind.ForEach, ScenarioStepKind.EndForEach,
            ScenarioStepKind.HostSend, ScenarioStepKind.HostReceive,
            ScenarioStepKind.CallScenario,
        })
        {
            var kind = k; // capture
            var item = new MenuItem { Header = kind.ToString() };
            item.Click += (_, _) => onPick(kind);
            root.Items.Add(item);
        }
        return root;
    }
}
