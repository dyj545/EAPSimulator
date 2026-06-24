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

    private void DrawEdge(ScenarioFlowLayoutResult layout, FlowEdge edge)
    {
        var from = layout.Nodes[edge.FromIndex];
        var to = layout.Nodes[edge.ToIndex];

        // Anchor points: bottom-center of source for forward edges, side anchors for back-arcs.
        bool isBackArc = edge.ToIndex < edge.FromIndex && (edge.Kind == FlowEdgeKind.LoopBack || edge.Kind == FlowEdgeKind.ForEachBack);
        double x1, y1, x2, y2;
        if (isBackArc)
        {
            // Exit right side of source, re-enter right side of target.
            x1 = from.X + ScenarioFlowLayout.NodeWidth;
            y1 = from.Y + ScenarioFlowLayout.NodeHeight / 2;
            x2 = to.X + ScenarioFlowLayout.NodeWidth;
            y2 = to.Y + ScenarioFlowLayout.NodeHeight / 2;
        }
        else
        {
            x1 = from.X + ScenarioFlowLayout.NodeWidth / 2;
            y1 = from.Y + ScenarioFlowLayout.NodeHeight;
            x2 = to.X + ScenarioFlowLayout.NodeWidth / 2;
            y2 = to.Y;
        }

        var brush = EdgeBrush(edge.Kind);
        var thickness = edge.Kind == FlowEdgeKind.OnError ? 1.5 : 1.5;
        var dash = edge.Kind == FlowEdgeKind.OnError ? new AvaloniaList<double>(new[] { 4.0, 3.0 }) : null;

        Shape path;
        if (isBackArc)
        {
            // Cubic bezier curving out to the right, then back to the target.
            var bend = 60 + Math.Abs(y1 - y2) * 0.2;
            var fig = new PathFigure
            {
                StartPoint = new Point(x1, y1),
                IsClosed = false,
                Segments = new PathSegments
                {
                    new BezierSegment
                    {
                        Point1 = new Point(x1 + bend, y1),
                        Point2 = new Point(x2 + bend, y2),
                        Point3 = new Point(x2, y2),
                    },
                },
            };
            path = new AvPath
            {
                Stroke = brush,
                StrokeThickness = thickness,
                Data = new PathGeometry { Figures = new PathFigures { fig } },
            };
        }
        else if (edge.Kind == FlowEdgeKind.BranchCase || edge.Kind == FlowEdgeKind.BranchDefault || edge.Kind == FlowEdgeKind.OnError)
        {
            // Branch / OnError jumps that aren't a strict downward line — curve through a midpoint
            // offset to avoid overlapping the rectangle.
            var midX = (x1 + x2) / 2 + 80;
            var fig = new PathFigure
            {
                StartPoint = new Point(x1, y1),
                IsClosed = false,
                Segments = new PathSegments
                {
                    new BezierSegment
                    {
                        Point1 = new Point(midX, y1 + 20),
                        Point2 = new Point(midX, y2 - 20),
                        Point3 = new Point(x2, y2),
                    },
                },
            };
            path = new AvPath
            {
                Stroke = brush,
                StrokeThickness = thickness,
                StrokeDashArray = dash,
                Data = new PathGeometry { Figures = new PathFigures { fig } },
            };
        }
        else
        {
            // Plain straight line — the most common case (sequential flow).
            path = new Line
            {
                StartPoint = new Point(x1, y1),
                EndPoint = new Point(x2, y2),
                Stroke = brush,
                StrokeThickness = thickness,
            };
        }
        _canvas.Children.Add(path);

        // Tip arrowhead — a small filled triangle pointing into the destination.
        DrawArrowHead(x2, y2, brush, isBackArc);

        // Caption — small text near the source, only when non-empty.
        if (!string.IsNullOrEmpty(edge.Caption))
        {
            var capX = isBackArc ? x1 + 30 : (x1 + x2) / 2 + 6;
            var capY = isBackArc ? (y1 + y2) / 2 : (y1 + y2) / 2 - 8;
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

    private void DrawArrowHead(double x, double y, IBrush brush, bool fromSide)
    {
        // 8x8 triangle pointing down for top-edge anchors; left for side anchors.
        Polygon arrow;
        if (fromSide)
        {
            arrow = new Polygon
            {
                Points = new Points { new Point(x, y), new Point(x + 8, y - 4), new Point(x + 8, y + 4) },
                Fill = brush,
            };
        }
        else
        {
            arrow = new Polygon
            {
                Points = new Points { new Point(x, y), new Point(x - 4, y - 8), new Point(x + 4, y - 8) },
                Fill = brush,
            };
        }
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
