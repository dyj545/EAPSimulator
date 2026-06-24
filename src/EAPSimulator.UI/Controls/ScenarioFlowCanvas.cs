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

    private readonly Canvas _canvas;
    private readonly ScrollViewer _scroll;
    private readonly Dictionary<int, Border> _nodeShells = new();
    private ScenarioViewModel? _wiredScenario;

    // Drag state
    private Border? _draggingNode;
    private int _draggingStepIndex = -1;
    private Point _dragStart;
    private double _dragOrigX;
    private double _dragOrigY;

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
        _outFan.Clear();
        _inFan.Clear();
        var vm = Scenario;
        if (vm == null) return;

        var def = vm.ToModelLayoutPreview();
        var overrides = vm.LayoutOverrides;
        var layout = ScenarioFlowLayout.Build(def, overrides);

        // Edges first so node rectangles render on top.
        foreach (var edge in layout.Edges)
            DrawEdge(layout, edge);

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
    /// Per-side fan-out counters keyed by (stepIndex, side) — used so that when one node has
    /// multiple outgoing edges leaving the same side they fan out instead of overlap.
    /// Reset per Rebuild().
    /// </summary>
    private readonly Dictionary<(int, AnchorSide), int> _outFan = new();
    private readonly Dictionary<(int, AnchorSide), int> _inFan = new();

    private double NextOutRank(int stepIdx, AnchorSide side)
    {
        var key = (stepIdx, side);
        _outFan.TryGetValue(key, out var n);
        _outFan[key] = n + 1;
        // 0, +1, -1, +2, -2, ... — keeps the first edge centred, fans alternately.
        return n switch
        {
            0 => 0,
            _ => ((n + 1) / 2) * (n % 2 == 1 ? 1 : -1),
        };
    }
    private double NextInRank(int stepIdx, AnchorSide side)
    {
        var key = (stepIdx, side);
        _inFan.TryGetValue(key, out var n);
        _inFan[key] = n + 1;
        return n switch
        {
            0 => 0,
            _ => ((n + 1) / 2) * (n % 2 == 1 ? 1 : -1),
        };
    }

    private void DrawEdge(ScenarioFlowLayoutResult layout, FlowEdge edge)
    {
        var from = layout.Nodes[edge.FromIndex];
        var to = layout.Nodes[edge.ToIndex];
        bool isBackArc = edge.ToIndex < edge.FromIndex
            && (edge.Kind == FlowEdgeKind.LoopBack || edge.Kind == FlowEdgeKind.ForEachBack);

        // 1) Pick the two anchor sides based on relative geometry.
        var (fromSide, toSide) = PickAnchors(from, to, edge.Kind);

        // 2) Per-side fan ranks so multiple edges on the same side don't overlap.
        var fromRank = NextOutRank(edge.FromIndex, fromSide);
        var toRank = NextInRank(edge.ToIndex, toSide);

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
        // Tip is on the edge of the destination rect; normal points outwards. We draw the
        // triangle's base 8px back along -normal, with 4px shoulders perpendicular to it.
        const double Len = 8;
        const double Half = 4;
        var bx = tip.X - normal.X * Len;
        var by = tip.Y - normal.Y * Len;
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
        foreach (var (idx, shell) in _nodeShells)
        {
            shell.BorderBrush = idx == selIdx
                ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50))
                : new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
            shell.BorderThickness = new Thickness(idx == selIdx ? 2 : 1);
        }
    }

    /// <summary>Reset all node positions to the layout default (column).</summary>
    public void ResetLayout()
    {
        Scenario?.LayoutOverrides.Clear();
        Rebuild();
    }
}
