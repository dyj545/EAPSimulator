using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EAPSimulator.Wpf.ViewModels.FlowCanvas;

namespace EAPSimulator.Wpf.Controls;

/// <summary>
/// Flow canvas control using DrawingVisual for rendering swimlanes, nodes, and connections.
/// </summary>
public class SwimlaneCanvas : FrameworkElement
{
    private readonly DrawingVisual _backgroundVisual = new();
    private readonly DrawingVisual _connectionVisual = new();
    private readonly DrawingVisual _nodeVisual = new();
    private readonly DrawingVisual _overlayVisual = new();

    // Colors
    private static readonly SolidColorBrush BackgroundBrush = new(Color.FromRgb(0x1E, 0x1E, 0x1E));
    private static readonly SolidColorBrush EquipmentBrush = new(Color.FromRgb(0x21, 0x96, 0xF3));
    private static readonly SolidColorBrush EapBrush = new(Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly SolidColorBrush HostBrush = new(Color.FromRgb(0xFF, 0x98, 0x00));
    private static readonly SolidColorBrush LaneBg1 = new(Color.FromRgb(0x25, 0x25, 0x25));
    private static readonly SolidColorBrush LaneBg2 = new(Color.FromRgb(0x28, 0x28, 0x28));
    private static readonly SolidColorBrush NodeBg = new(Color.FromRgb(0x2D, 0x2D, 0x2D));
    private static readonly SolidColorBrush NodeBorderBrush = new(Color.FromRgb(0x3F, 0x3F, 0x46));
    private static readonly SolidColorBrush NodeSelectedBorder = new(Color.FromRgb(0x56, 0x9C, 0xD6));
    private static readonly SolidColorBrush NodeSelectedBg = new(Color.FromRgb(0x3A, 0x3A, 0x6A));
    private static readonly SolidColorBrush TextBrush = new(Color.FromRgb(0xD4, 0xD4, 0xD4));
    private static readonly SolidColorBrush BadgeBrush = new(Color.FromRgb(0x56, 0x9C, 0xD6));
    private static readonly SolidColorBrush ConnectionBrush = new(Color.FromRgb(0x56, 0x9C, 0xD6));
    private static readonly SolidColorBrush SelectedLineBrush = new(Colors.White);
    private static readonly SolidColorBrush ApBrush = new(Color.FromRgb(0xFF, 0x98, 0x00));
    private static readonly SolidColorBrush ApHoverBrush = new(Color.FromRgb(0xFF, 0xC1, 0x07));
    private static readonly SolidColorBrush TempLineBrush = new(Color.FromArgb(0x88, 0xFF, 0x98, 0x00));

    private const double LaneHeaderHeight = 28;
    private const double NodeWidth = 180;
    private const double NodeHeight = 44;
    private const double SnapSize = 20;
    private const double ApHitRadius = 10;
    private const double DragThreshold = 5;

    // Drag state
    private FlowNodeViewModel? _dragNode;
    private Point _dragOffset;
    private Point _dragStart;
    private bool _isDragging;
    private bool _isPanning;
    private Point _panStart;
    private Point _panOffsetStart;

    // Connection creation state
    private FlowNodeViewModel? _connSourceNode;
    private string _connSourceApId = "";
    private Point _connDragEnd;
    private bool _isCreatingConnection;

    // Waypoint drag state
    private FlowConnectionViewModel? _dragWpConn;
    private int _dragWpIndex = -1;

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(FlowCanvasViewModel), typeof(SwimlaneCanvas),
            new PropertyMetadata(null, OnViewModelChanged));

    public FlowCanvasViewModel? ViewModel
    {
        get => (FlowCanvasViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public SwimlaneCanvas()
    {
        AddVisualChild(_backgroundVisual);
        AddVisualChild(_connectionVisual);
        AddVisualChild(_nodeVisual);
        AddVisualChild(_overlayVisual);
        ClipToBounds = true;
        Focusable = true;
        Loaded += (_, _) => Redraw();
    }

    private FlowCanvasViewModel? _previousVm;

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var canvas = (SwimlaneCanvas)d;
        if (canvas._previousVm != null)
            canvas._previousVm.CanvasInvalidationRequested -= canvas.Redraw;

        canvas._previousVm = e.NewValue as FlowCanvasViewModel;
        if (canvas._previousVm != null)
            canvas._previousVm.CanvasInvalidationRequested += canvas.Redraw;

        canvas.Redraw();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(
            double.IsInfinity(availableSize.Width) ? 800 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 600 : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        return finalSize;
    }

    public void Redraw()
    {
        DrawBackground();
        DrawConnections();
        DrawNodes();
        DrawOverlay();
    }

    private void DrawBackground()
    {
        using var dc = _backgroundVisual.RenderOpen();
        dc.DrawRectangle(BackgroundBrush, null, new Rect(RenderSize));

        var totalWidth = RenderSize.Width;
        var laneWidth = totalWidth / 3.0;

        dc.DrawRectangle(LaneBg1, null, new Rect(0, 0, laneWidth, RenderSize.Height));
        dc.DrawRectangle(LaneBg2, null, new Rect(laneWidth, 0, laneWidth, RenderSize.Height));
        dc.DrawRectangle(LaneBg1, null, new Rect(laneWidth * 2, 0, laneWidth, RenderSize.Height));

        DrawLaneHeader(dc, 0, laneWidth, "Equipment", EquipmentBrush);
        DrawLaneHeader(dc, laneWidth, laneWidth, "EAP", EapBrush);
        DrawLaneHeader(dc, laneWidth * 2, laneWidth, "Host", HostBrush);

        var sepPen = new Pen(NodeBorderBrush, 1);
        dc.DrawLine(sepPen, new Point(laneWidth, 0), new Point(laneWidth, RenderSize.Height));
        dc.DrawLine(sepPen, new Point(laneWidth * 2, 0), new Point(laneWidth * 2, RenderSize.Height));
    }

    private void DrawLaneHeader(DrawingContext dc, double x, double width, string label, Brush color)
    {
        dc.DrawRectangle(color, null, new Rect(x, 0, width, LaneHeaderHeight));
        var ft = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("微软雅黑"), 12, Brushes.White, 1.0);
        dc.DrawText(ft, new Point(x + (width - ft.Width) / 2, (LaneHeaderHeight - ft.Height) / 2));
    }

    private void DrawConnections()
    {
        using var dc = _connectionVisual.RenderOpen();
        var vm = ViewModel;
        if (vm == null) return;

        foreach (var conn in vm.Document.Connections)
        {
            var fromNode = vm.Document.FindNode(conn.FromNodeId);
            var toNode = vm.Document.FindNode(conn.ToNodeId);
            if (fromNode == null || toNode == null) continue;

            var fromPt = GetNodeCenterBottom(fromNode);
            var toPt = GetNodeCenterTop(toNode);

            var color = conn.IsSelected ? SelectedLineBrush : ConnectionBrush;
            var pen = new Pen(color, conn.IsSelected ? 2.5 : 1.5);

            // Draw with waypoints
            var points = new List<Point> { fromPt };
            points.AddRange(conn.Waypoints);
            points.Add(toPt);

            var path = new StreamGeometry();
            using (var sg = path.Open())
            {
                sg.BeginFigure(points[0], true, false);
                for (int i = 1; i < points.Count; i++)
                    sg.LineTo(points[i], true, false);
            }
            dc.DrawGeometry(null, pen, path);

            // Arrowhead
            var lastPt = points.Count >= 2 ? points[^2] : fromPt;
            DrawArrowHead(dc, color, toPt, new Point(toPt.X - lastPt.X, toPt.Y - lastPt.Y));

            // Waypoint handles (when selected)
            if (conn.IsSelected)
            {
                foreach (var wp in conn.Waypoints)
                {
                    dc.DrawEllipse(ApBrush, new Pen(Brushes.White, 1), wp, 5, 5);
                }
            }

            // Label
            if (!string.IsNullOrEmpty(conn.Label))
            {
                var midIdx = points.Count / 2;
                var midPt = points[midIdx];
                var ft = new FormattedText(conn.Label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("微软雅黑"), 10, new SolidColorBrush(Color.FromRgb(0xB5, 0xCE, 0xA8)), 1.0);
                dc.DrawText(ft, new Point(midPt.X + 8, midPt.Y - 8));
            }
        }
    }

    private void DrawArrowHead(DrawingContext dc, Brush brush, Point tip, Point dir)
    {
        var len = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
        if (len < 0.001) return;
        var nx = dir.X / len; var ny = dir.Y / len;
        var px = -ny; var py = nx;
        var s = 8.0;
        var p1 = new Point(tip.X - nx * s + px * s * 0.4, tip.Y - ny * s + py * s * 0.4);
        var p2 = new Point(tip.X - nx * s - px * s * 0.4, tip.Y - ny * s - py * s * 0.4);
        var geo = new StreamGeometry();
        using (var sg = geo.Open()) { sg.BeginFigure(tip, true, false); sg.LineTo(p1, true, false); sg.LineTo(p2, true, true); }
        dc.DrawGeometry(brush, null, geo);
    }

    private void DrawNodes()
    {
        using var dc = _nodeVisual.RenderOpen();
        var vm = ViewModel;
        if (vm == null) return;

        foreach (var node in vm.Document.Nodes)
        {
            var rect = new Rect(node.X, node.Y, node.Width, node.Height);
            var bg = node.IsSelected ? NodeSelectedBg : NodeBg;
            var border = node.IsSelected ? NodeSelectedBorder : NodeBorderBrush;
            var rectGeo = new RectangleGeometry(rect, 6, 6);
            dc.DrawGeometry(bg, new Pen(border, node.IsSelected ? 2 : 1), rectGeo);

            if (node.NodeType == FlowNodeType.Trigger)
            {
                var badgeRect = new Rect(node.X - 4, node.Y - 4, 18, 18);
                var badgeGeo = new RectangleGeometry(badgeRect, 3, 3);
                dc.DrawGeometry(BadgeBrush, null, badgeGeo);
                var badgeFt = new FormattedText((node.StepIndex + 1).ToString(), CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, Brushes.White, 1.0);
                dc.DrawText(badgeFt, new Point(badgeRect.X + 3, badgeRect.Y + 2));
            }

            if (!string.IsNullOrEmpty(node.Label))
            {
                var ft = new FormattedText(node.Label, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("微软雅黑"), 10, TextBrush, 1.0);
                dc.DrawText(ft, new Point(node.X + 8, node.Y + (node.Height - ft.Height) / 2));
            }

            if (node.IsSelected)
            {
                var pts = AttachmentPointProvider.GetDefaultPoints();
                foreach (var ap in pts)
                {
                    var pos = ap.GetAbsolutePosition(node.X, node.Y, node.Width, node.Height);
                    dc.DrawEllipse(ApBrush, new Pen(Brushes.White, 0.5), pos, 3.5, 3.5);
                }
            }
        }
    }

    private void DrawOverlay()
    {
        using var dc = _overlayVisual.RenderOpen();

        // Draw temporary connection line
        if (_isCreatingConnection && _connSourceNode != null)
        {
            var srcPt = GetApPosition(_connSourceNode, _connSourceApId);
            var pen = new Pen(TempLineBrush, 1.5) { DashStyle = DashStyles.Dash };
            dc.DrawLine(pen, srcPt, _connDragEnd);
        }
    }

    // ===== Hit Testing =====

    private FlowNodeViewModel? HitTestNode(Point pos)
    {
        var vm = ViewModel;
        if (vm == null) return null;
        foreach (var node in vm.Document.Nodes)
        {
            if (pos.X >= node.X && pos.X <= node.X + node.Width &&
                pos.Y >= node.Y && pos.Y <= node.Y + node.Height)
                return node;
        }
        return null;
    }

    private AttachmentPoint? HitTestAttachmentPoint(Point pos)
    {
        var vm = ViewModel;
        if (vm?.SelectedNode == null) return null;
        var node = vm.SelectedNode;
        foreach (var ap in AttachmentPointProvider.GetDefaultPoints())
        {
            var apPos = ap.GetAbsolutePosition(node.X, node.Y, node.Width, node.Height);
            var dx = pos.X - apPos.X;
            var dy = pos.Y - apPos.Y;
            if (Math.Sqrt(dx * dx + dy * dy) < ApHitRadius)
                return ap;
        }
        return null;
    }

    private FlowConnectionViewModel? HitTestConnection(Point pos)
    {
        var vm = ViewModel;
        if (vm == null) return null;
        const double hitThickness = 6;

        foreach (var conn in vm.Document.Connections)
        {
            var fromNode = vm.Document.FindNode(conn.FromNodeId);
            var toNode = vm.Document.FindNode(conn.ToNodeId);
            if (fromNode == null || toNode == null) continue;

            var fromPt = GetNodeCenterBottom(fromNode);
            var toPt = GetNodeCenterTop(toNode);

            var points = new List<Point> { fromPt };
            points.AddRange(conn.Waypoints);
            points.Add(toPt);

            for (int i = 0; i < points.Count - 1; i++)
            {
                if (DistanceToSegment(pos, points[i], points[i + 1]) < hitThickness)
                    return conn;
            }
        }
        return null;
    }

    private int HitTestWaypoint(Point pos)
    {
        var vm = ViewModel;
        if (vm?.SelectedConnection == null) return -1;
        for (int i = 0; i < vm.SelectedConnection.Waypoints.Count; i++)
        {
            var wp = vm.SelectedConnection.Waypoints[i];
            var dx = pos.X - wp.X;
            var dy = pos.Y - wp.Y;
            if (Math.Sqrt(dx * dx + dy * dy) < 8)
                return i;
        }
        return -1;
    }

    // ===== Mouse Events =====

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var pos = e.GetPosition(this);
        var vm = ViewModel;
        if (vm == null) return;

        // Double-click: add waypoint on connection
        if (e.ClickCount == 2)
        {
            OnCanvasMouseDoubleClick(this, e);
            return;
        }

        _dragStart = pos;

        // Check waypoint drag on selected connection
        var wpIdx = HitTestWaypoint(pos);
        if (wpIdx >= 0)
        {
            _dragWpConn = vm.SelectedConnection;
            _dragWpIndex = wpIdx;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        // Check attachment point (for connection creation)
        var hitAp = HitTestAttachmentPoint(pos);
        if (hitAp != null && vm.SelectedNode != null)
        {
            _connSourceNode = vm.SelectedNode;
            _connSourceApId = hitAp.Id;
            _connDragEnd = pos;
            _isCreatingConnection = true;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        // Check node hit
        var hitNode = HitTestNode(pos);
        if (hitNode != null)
        {
            vm.SelectNode(hitNode);
            _dragNode = hitNode;
            _dragOffset = new Point(pos.X - hitNode.X, pos.Y - hitNode.Y);
            _isDragging = true;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        // Check connection hit
        var hitConn = HitTestConnection(pos);
        if (hitConn != null)
        {
            vm.SelectConnection(hitConn);
            e.Handled = true;
            return;
        }

        // Empty space: clear selection, start panning
        vm.ClearSelection();
        _isPanning = true;
        _panStart = pos;
        _panOffsetStart = new Point(vm.PanOffsetX, vm.PanOffsetY);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!IsMouseCaptured) return;

        var pos = e.GetPosition(this);
        var vm = ViewModel;
        if (vm == null) return;

        // Waypoint drag
        if (_dragWpConn != null && _dragWpIndex >= 0)
        {
            _dragWpConn.Waypoints[_dragWpIndex] = pos;
            Redraw();
            return;
        }

        // Connection creation drag
        if (_isCreatingConnection)
        {
            _connDragEnd = pos;
            Redraw();
            return;
        }

        // Node drag (with threshold)
        if (_isDragging && _dragNode != null)
        {
            var delta = pos - _dragStart;
            if (Math.Abs(delta.X) > DragThreshold || Math.Abs(delta.Y) > DragThreshold)
            {
                _dragNode.X = pos.X - _dragOffset.X;
                _dragNode.Y = Math.Max(LaneHeaderHeight, pos.Y - _dragOffset.Y);
                Redraw();
            }
            return;
        }

        // Panning
        if (_isPanning)
        {
            var delta = pos - _panStart;
            vm.PanOffsetX = _panOffsetStart.X + delta.X;
            vm.PanOffsetY = _panOffsetStart.Y + delta.Y;
            Redraw();
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        var pos = e.GetPosition(this);
        var vm = ViewModel;

        // Waypoint drag end
        if (_dragWpConn != null)
        {
            _dragWpConn = null;
            _dragWpIndex = -1;
            ReleaseMouseCapture();
            Redraw();
            return;
        }

        // Connection creation end
        if (_isCreatingConnection && vm != null)
        {
            _isCreatingConnection = false;

            // Find target node
            var targetNode = HitTestNode(pos);
            if (targetNode != null && _connSourceNode != null &&
                targetNode.NodeId != _connSourceNode.NodeId)
            {
                // Find closest attachment point on target
                var targetAp = FindClosestAp(targetNode, pos);

                // Validate: no duplicate
                var exists = vm.Document.Connections.Any(c =>
                    c.FromNodeId == _connSourceNode.NodeId && c.ToNodeId == targetNode.NodeId);

                if (!exists)
                {
                    vm.Document.Connections.Add(new FlowConnectionViewModel
                    {
                        FromNodeId = _connSourceNode.NodeId,
                        ToNodeId = targetNode.NodeId,
                        SourceAttachmentId = _connSourceApId,
                        TargetAttachmentId = targetAp?.Id ?? "",
                    });
                    vm.SelectConnection(vm.Document.Connections.Last());
                }
            }

            _connSourceNode = null;
            _connSourceApId = "";
            ReleaseMouseCapture();
            Redraw();
            return;
        }

        // Node drag end
        if (_isDragging && _dragNode != null)
        {
            _dragNode.X = Math.Round(_dragNode.X / SnapSize) * SnapSize;
            _dragNode.Y = Math.Max(LaneHeaderHeight, Math.Round(_dragNode.Y / SnapSize) * SnapSize);
        }

        _isDragging = false;
        _dragNode = null;
        _isPanning = false;
        ReleaseMouseCapture();
        Redraw();
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        var pos = e.GetPosition(this);
        var vm = ViewModel;
        if (vm == null) return;

        // Right-click on waypoint: delete it
        var wpIdx = HitTestWaypoint(pos);
        if (wpIdx >= 0 && vm.SelectedConnection != null)
        {
            vm.SelectedConnection.Waypoints.RemoveAt(wpIdx);
            Redraw();
            e.Handled = true;
            return;
        }

        // Right-click on connection: delete it
        var hitConn = HitTestConnection(pos);
        if (hitConn != null)
        {
            vm.Document.Connections.Remove(hitConn);
            vm.ClearSelection();
            Redraw();
            e.Handled = true;
            return;
        }
    }

    private void OnCanvasMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(this);
        var vm = ViewModel;
        if (vm == null) return;

        // Double-click on connection: add waypoint
        var hitConn = HitTestConnection(pos);
        if (hitConn != null)
        {
            hitConn.Waypoints.Add(pos);
            vm.SelectConnection(hitConn);
            Redraw();
            e.Handled = true;
            return;
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        var vm = ViewModel;
        if (vm == null) return;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            vm.Zoom = Math.Clamp(vm.Zoom + (e.Delta > 0 ? 0.1 : -0.1), 0.25, 3.0);
            Redraw();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var vm = ViewModel;
        if (vm == null) return;

        if (e.Key == Key.Delete)
        {
            vm.DeleteSelected();
            Redraw();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (_isCreatingConnection)
            {
                _isCreatingConnection = false;
                _connSourceNode = null;
                ReleaseMouseCapture();
            }
            vm.ClearSelection();
            Redraw();
            e.Handled = true;
        }
    }

    // ===== Helpers =====

    private static Point GetNodeCenterBottom(FlowNodeViewModel node)
        => new(node.X + node.Width / 2, node.Y + node.Height);

    private static Point GetNodeCenterTop(FlowNodeViewModel node)
        => new(node.X + node.Width / 2, node.Y);

    private static Point GetApPosition(FlowNodeViewModel node, string apId)
    {
        var ap = AttachmentPointProvider.GetDefaultPoints().FirstOrDefault(p => p.Id == apId);
        if (ap != null)
            return ap.GetAbsolutePosition(node.X, node.Y, node.Width, node.Height);
        return GetNodeCenterBottom(node);
    }

    private static AttachmentPoint? FindClosestAp(FlowNodeViewModel node, Point pos)
    {
        AttachmentPoint? best = null;
        double bestDist = double.MaxValue;
        foreach (var ap in AttachmentPointProvider.GetDefaultPoints())
        {
            var apPos = ap.GetAbsolutePosition(node.X, node.Y, node.Width, node.Height);
            var dx = pos.X - apPos.X;
            var dy = pos.Y - apPos.Y;
            var dist = dx * dx + dy * dy;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = ap;
            }
        }
        return best;
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        var abX = b.X - a.X;
        var abY = b.Y - a.Y;
        var apX = p.X - a.X;
        var apY = p.Y - a.Y;
        var abLenSq = abX * abX + abY * abY;
        if (abLenSq < 0.001) return Math.Sqrt(apX * apX + apY * apY);
        var t = Math.Clamp((apX * abX + apY * abY) / abLenSq, 0, 1);
        var closestX = a.X + abX * t;
        var closestY = a.Y + abY * t;
        var dx = p.X - closestX;
        var dy = p.Y - closestY;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
