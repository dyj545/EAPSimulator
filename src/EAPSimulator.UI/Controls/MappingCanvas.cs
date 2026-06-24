using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using EAPSimulator.Core.Protocols.Bridge;
using EAPSimulator.Core.Protocols.HostProtocol;
using EAPSimulator.Core.Protocols.SecsGem.SecsII;
using EAPSimulator.UI.ViewModels;
using System.Collections.Specialized;
using System.ComponentModel;
using AvPath = Avalonia.Controls.Shapes.Path;

namespace EAPSimulator.UI.Controls;

/// <summary>
/// Drag-and-drop mapping canvas for <see cref="BridgeMappingViewModel"/>.
///
/// Layout: three columns — left SECS field tree, centre connection canvas, right Host field
/// tree. Each leaf row exposes a tiny circular anchor at the side facing the canvas. Mappings
/// in <see cref="MappingGroupViewModel.Mappings"/> render as bezier curves between the two
/// anchors that correspond to their SECS path and Host field name. Dragging from a SECS
/// anchor to a Host anchor adds a new mapping; right-clicking a curve deletes it.
///
/// <para>Why a custom UserControl rather than two TreeViews stacked side-by-side: we need
/// pixel-precise anchor coordinates against a shared coordinate space to draw the bezier and
/// to hit-test pointers — a TreeView with template-based DataTemplates won't give us that
/// without each item posting position updates back. Rendering the rows ourselves keeps the
/// data flow simple: rebuild on mapping/group change, re-anchor on scroll.</para>
/// </summary>
public partial class MappingCanvas : UserControl
{
    private const double RowHeight = 22;
    private const double Indent = 14;
    private const double AnchorSize = 9;

    public static readonly StyledProperty<BridgeMappingViewModel?> ViewModelProperty =
        AvaloniaProperty.Register<MappingCanvas, BridgeMappingViewModel?>(nameof(ViewModel));

    public BridgeMappingViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private readonly Grid _grid;
    private readonly StackPanel _secsList;
    private readonly StackPanel _hostList;
    private readonly Canvas _wireCanvas;
    private readonly ScrollViewer _secsScroll;
    private readonly ScrollViewer _hostScroll;

    /// <summary>Leaf rows keyed by their path / field name, with screen-space anchor points.</summary>
    private readonly Dictionary<string, AnchorInfo> _secsAnchors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AnchorInfo> _hostAnchors = new(StringComparer.Ordinal);

    private MappingGroupViewModel? _wiredGroup;

    // Drag state
    private Line? _ghost;
    private bool _draggingFromSecs;
    private string? _dragSourceKey;

    private sealed class AnchorInfo
    {
        public required string Key;     // SECS path or Host field full name
        public required Ellipse Dot;
        public required Border Row;     // for highlight
        public double X;                // canvas-space anchor centre
        public double Y;
    }

    public MappingCanvas()
    {
        _grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(220, GridUnitType.Pixel),
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(220, GridUnitType.Pixel),
            },
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
        };

        _secsList = new StackPanel { Spacing = 0, Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)) };
        _hostList = new StackPanel { Spacing = 0, Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)) };
        _secsScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _secsList,
        };
        _hostScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _hostList,
        };
        _wireCanvas = new Canvas
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
        };
        Grid.SetColumn(_secsScroll, 0);
        Grid.SetColumn(_wireCanvas, 1);
        Grid.SetColumn(_hostScroll, 2);
        _grid.Children.Add(_secsScroll);
        _grid.Children.Add(_wireCanvas);
        _grid.Children.Add(_hostScroll);
        Content = _grid;

        // Re-anchor when either side scrolls or the canvas resizes. Avalonia exposes scrolling
        // and bounds as ScrollChanged / LayoutUpdated events — simpler and lighter than the
        // GetObservable() route, and stays compatible without pulling in ReactiveUI.
        _secsScroll.ScrollChanged += (_, _) => RefreshAnchorsAndWires();
        _hostScroll.ScrollChanged += (_, _) => RefreshAnchorsAndWires();
        _wireCanvas.LayoutUpdated += (_, _) => RefreshAnchorsAndWires();

        // Pointer move/release on the canvas drive the drag ghost.
        _wireCanvas.PointerMoved += OnCanvasPointerMoved;
        _wireCanvas.PointerReleased += OnCanvasPointerReleased;

        PropertyChanged += OnAvaloniaPropertyChanged;
    }

    private void OnAvaloniaPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ViewModelProperty)
        {
            UnwireViewModel();
            WireViewModel();
            Rebuild();
        }
    }

    private void WireViewModel()
    {
        var vm = ViewModel;
        if (vm == null) return;
        vm.PropertyChanged += OnVmPropertyChanged;
        WireGroup(vm.SelectedGroup);
        _wiredGroup = vm.SelectedGroup;
    }

    private void UnwireViewModel()
    {
        var vm = ViewModel;
        if (vm == null) return;
        vm.PropertyChanged -= OnVmPropertyChanged;
        UnwireGroup(_wiredGroup);
        _wiredGroup = null;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BridgeMappingViewModel.SelectedGroup))
        {
            UnwireGroup(_wiredGroup);
            _wiredGroup = ViewModel?.SelectedGroup;
            WireGroup(_wiredGroup);
            Rebuild();
        }
    }

    private void WireGroup(MappingGroupViewModel? g)
    {
        if (g == null) return;
        g.PropertyChanged += OnGroupPropertyChanged;
        g.Mappings.CollectionChanged += OnMappingsChanged;
    }

    private void UnwireGroup(MappingGroupViewModel? g)
    {
        if (g == null) return;
        g.PropertyChanged -= OnGroupPropertyChanged;
        g.Mappings.CollectionChanged -= OnMappingsChanged;
    }

    private void OnGroupPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Template name change → re-derive tree contents.
        if (e.PropertyName == nameof(MappingGroupViewModel.SecsTemplate)
            || e.PropertyName == nameof(MappingGroupViewModel.HostTemplate))
            Rebuild();
    }

    private void OnMappingsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RefreshAnchorsAndWires();

    /// <summary>
    /// Full rebuild: redraw both side trees from the selected group's templates, then
    /// recompute anchor positions and wires. Idempotent.
    /// </summary>
    public void Rebuild()
    {
        _secsList.Children.Clear();
        _hostList.Children.Clear();
        _secsAnchors.Clear();
        _hostAnchors.Clear();
        _wireCanvas.Children.Clear();
        if (_ghost != null) { _wireCanvas.Children.Add(_ghost); }

        var vm = ViewModel;
        var group = vm?.SelectedGroup;
        if (vm == null || group == null) return;

        // Build SECS tree: walk SecsItem and emit indented rows. Leaves get an anchor.
        if (vm.SecsTemplateLookup.TryGetValue(group.SecsTemplate, out var secsTpl))
        {
            try
            {
                var msg = secsTpl.BuildMessage();
                BuildSecsRows(msg.RootItem, "", 0);
            }
            catch
            {
                // Malformed ItemXml — just show a placeholder so the canvas doesn't go blank.
                _secsList.Children.Add(MakePlaceholder("SECS 模板解析失败"));
            }
        }
        else
        {
            _secsList.Children.Add(MakePlaceholder(
                string.IsNullOrEmpty(group.SecsTemplate) ? "(未选择 SECS 模板)" : $"未找到模板 {group.SecsTemplate}"));
        }

        if (vm.HostTemplateLookup.TryGetValue(group.HostTemplate, out var hostTpl))
        {
            try
            {
                var hostMsg = hostTpl.BuildMessage();
                foreach (var (name, field) in hostMsg.Fields)
                    BuildHostRows(field, "", 0);
            }
            catch
            {
                _hostList.Children.Add(MakePlaceholder("Host 模板解析失败"));
            }
        }
        else
        {
            _hostList.Children.Add(MakePlaceholder(
                string.IsNullOrEmpty(group.HostTemplate) ? "(未选择 Host 模板)" : $"未找到模板 {group.HostTemplate}"));
        }

        // Defer wire layout to the next layout pass so row Bounds are real.
        Dispatcher.UIThread.Post(RefreshAnchorsAndWires, DispatcherPriority.Loaded);
    }

    private TextBlock MakePlaceholder(string msg) => new()
    {
        Text = msg, Opacity = 0.45, FontSize = 11, Margin = new Thickness(8, 6),
    };

    /// <summary>
    /// Walk a SECS item tree, depth-first. For lists, emit one row per index using the path
    /// "0/1/2" convention; for leaves (scalars), emit a value row with an anchor on the right.
    /// </summary>
    private void BuildSecsRows(SecsItem? item, string path, int depth)
    {
        if (item == null) return;
        var label = $"[{(string.IsNullOrEmpty(path) ? "*" : path)}] {item.Format}";
        if (item is SecsList list)
        {
            _secsList.Children.Add(MakeRow(label, depth, isLeaf: false, isSecs: true, key: path));
            for (int i = 0; i < list.Items.Length; i++)
            {
                var childPath = string.IsNullOrEmpty(path) ? i.ToString() : $"{path}/{i}";
                BuildSecsRows(list.Items[i], childPath, depth + 1);
            }
        }
        else
        {
            var valueText = TryGetValueText(item);
            var leafLabel = $"[{(string.IsNullOrEmpty(path) ? "*" : path)}] {item.Format} = {valueText}";
            _secsList.Children.Add(MakeRow(leafLabel, depth, isLeaf: true, isSecs: true, key: path));
        }
    }

    private static string TryGetValueText(SecsItem item) => item switch
    {
        SecsAscii a => $"\"{a.Value}\"",
        _ => item.ToString() ?? "",
    };

    /// <summary>Walk a Host field tree; field path is the dot-separated name lineage.</summary>
    private void BuildHostRows(HostField field, string parentPath, int depth)
    {
        var path = string.IsNullOrEmpty(parentPath) ? field.Name : $"{parentPath}.{field.Name}";
        var hasChildren = field.Children.Count > 0;
        var label = hasChildren
            ? $"{field.Name} ({field.Type})"
            : $"{field.Name} ({field.Type}) = {field.Value}";
        _hostList.Children.Add(MakeRow(label, depth, isLeaf: !hasChildren, isSecs: false, key: path));
        foreach (var child in field.Children)
            BuildHostRows(child, path, depth + 1);
    }

    private Border MakeRow(string text, int depth, bool isLeaf, bool isSecs, string key)
    {
        var indent = depth * Indent + 6;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

        var txt = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("Consolas,monospace"),
            FontSize = 11,
            Foreground = new SolidColorBrush(isLeaf
                ? Color.FromRgb(0xCD, 0xD6, 0xF4)
                : Color.FromRgb(0x90, 0x90, 0x90)),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(indent, 0, 4, 0),
        };
        Grid.SetColumn(txt, 0);
        grid.Children.Add(txt);

        Ellipse? dot = null;
        if (isLeaf)
        {
            dot = new Ellipse
            {
                Width = AnchorSize,
                Height = AnchorSize,
                Stroke = new SolidColorBrush(isSecs ? Color.FromRgb(0x89, 0xB4, 0xFA) : Color.FromRgb(0xFA, 0xB3, 0x87)),
                StrokeThickness = 1.5,
                Fill = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                Cursor = new Cursor(StandardCursorType.Cross),
                Margin = new Thickness(4, 0, 4, 0),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            Grid.SetColumn(dot, 1);
            grid.Children.Add(dot);
        }

        var row = new Border
        {
            Height = RowHeight,
            Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid,
        };

        if (isLeaf && dot != null)
        {
            var info = new AnchorInfo { Key = key, Dot = dot, Row = row };
            if (isSecs) _secsAnchors[key] = info;
            else _hostAnchors[key] = info;

            dot.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(dot).Properties.IsLeftButtonPressed) return;
                StartDrag(key, isSecs, dot);
                // Capture the pointer so PointerMoved / PointerReleased keep flowing into
                // the canvas even when the cursor leaves the small Ellipse hit-rect.
                e.Pointer.Capture(_wireCanvas);
                e.Handled = true;
            };
        }
        return row;
    }

    private void StartDrag(string key, bool fromSecs, Ellipse dot)
    {
        _dragSourceKey = key;
        _draggingFromSecs = fromSecs;
        var startInCanvas = dot.TranslatePoint(new Point(AnchorSize / 2, AnchorSize / 2), _wireCanvas)
                            ?? new Point(0, 0);
        _ghost = new Line
        {
            StartPoint = startInCanvas,
            EndPoint = startInCanvas,
            Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xB4, 0x54)),
            StrokeThickness = 2,
            StrokeDashArray = new AvaloniaList<double>(new[] { 5.0, 4.0 }),
            ZIndex = 10,
        };
        _wireCanvas.Children.Add(_ghost);
        // Capture pointer on the canvas so we keep getting Moved/Released even outside the dot.
        Dispatcher.UIThread.Post(() =>
        {
            // ensure cursor pointer transfers — Avalonia will route subsequent events through canvas
        });
    }

    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_ghost == null) return;
        var pos = e.GetPosition(_wireCanvas);
        _ghost.EndPoint = pos;
    }

    private void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_ghost == null) return;
        var pos = e.GetPosition(_wireCanvas);
        // Hit-test the target side's anchors.
        var targetKey = HitTestAnchor(pos, _draggingFromSecs ? _hostAnchors : _secsAnchors);
        if (targetKey != null && _dragSourceKey != null && ViewModel?.SelectedGroup is { } g)
        {
            var secsPath = _draggingFromSecs ? _dragSourceKey : targetKey;
            var hostField = _draggingFromSecs ? targetKey : _dragSourceKey;
            // Avoid duplicates: same (secsPath, hostFieldName) already mapped? Skip.
            var exists = g.Mappings.Any(m =>
                m.SecsPath == secsPath && m.HostFieldName == hostField);
            if (!exists)
            {
                g.Mappings.Add(new FieldMappingViewModel
                {
                    Source = _draggingFromSecs ? FieldMappingSource.Secs : FieldMappingSource.Host,
                    Target = _draggingFromSecs ? FieldMappingTarget.Host : FieldMappingTarget.Secs,
                    SecsPath = secsPath,
                    HostFieldName = hostField,
                });
            }
        }
        _wireCanvas.Children.Remove(_ghost);
        _ghost = null;
        _dragSourceKey = null;
        e.Pointer.Capture(null);
        RefreshAnchorsAndWires();
    }

    private string? HitTestAnchor(Point p, Dictionary<string, AnchorInfo> anchors)
    {
        const double Tolerance = 14;
        foreach (var (key, info) in anchors)
        {
            var dx = p.X - info.X;
            var dy = p.Y - info.Y;
            if (dx * dx + dy * dy <= Tolerance * Tolerance) return key;
        }
        return null;
    }

    /// <summary>
    /// Recompute anchor screen coordinates (rows scroll, columns resize) and redraw every
    /// mapping wire. Cheap enough to call on each scroll tick — wire count is bounded by the
    /// group's mapping list, which is small in practice.
    /// </summary>
    private void RefreshAnchorsAndWires()
    {
        // Recompute anchor positions in canvas-space.
        UpdateAnchorPositions(_secsAnchors, isSecs: true);
        UpdateAnchorPositions(_hostAnchors, isSecs: false);

        // Strip previous wires (keep the ghost on top).
        var toRemove = _wireCanvas.Children.Where(c => c is AvPath || c is Polygon).ToList();
        foreach (var c in toRemove) _wireCanvas.Children.Remove(c);

        var group = ViewModel?.SelectedGroup;
        if (group == null) return;
        foreach (var m in group.Mappings)
        {
            if (!_secsAnchors.TryGetValue(m.SecsPath, out var sa)) continue;
            if (!_hostAnchors.TryGetValue(m.HostFieldName, out var ha)) continue;
            DrawWire(sa, ha, m);
        }
    }

    private void UpdateAnchorPositions(Dictionary<string, AnchorInfo> anchors, bool isSecs)
    {
        foreach (var info in anchors.Values)
        {
            var center = new Point(AnchorSize / 2, AnchorSize / 2);
            var posInCanvas = info.Dot.TranslatePoint(center, _wireCanvas);
            if (posInCanvas is { } p)
            {
                info.X = p.X;
                info.Y = p.Y;
            }
            else
            {
                // Row scrolled out of view — push the anchor off-canvas so the wire fades out.
                info.X = double.NaN;
                info.Y = double.NaN;
            }
        }
    }

    private void DrawWire(AnchorInfo sa, AnchorInfo ha, FieldMappingViewModel mapping)
    {
        if (double.IsNaN(sa.X) || double.IsNaN(ha.X)) return;
        // Bezier control points are pulled horizontally toward each other so the curve enters
        // / exits each anchor horizontally — matches the right/left anchor geometry.
        var dx = Math.Max(60, Math.Abs(ha.X - sa.X) / 2);
        var c1 = new Point(sa.X + dx, sa.Y);
        var c2 = new Point(ha.X - dx, ha.Y);
        var path = new AvPath
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1)),
            StrokeThickness = 1.5,
            Data = new PathGeometry
            {
                Figures = new PathFigures
                {
                    new PathFigure
                    {
                        StartPoint = new Point(sa.X, sa.Y),
                        IsClosed = false,
                        Segments = new PathSegments
                        {
                            new BezierSegment { Point1 = c1, Point2 = c2, Point3 = new Point(ha.X, ha.Y) },
                        },
                    },
                },
            },
        };

        // Right-click anywhere along the path deletes the mapping. We attach the menu to the
        // path itself rather than the canvas so other paths' menus don't all fire at once.
        var menu = new ContextMenu();
        var del = new MenuItem { Header = "删除此映射" };
        del.Click += (_, _) =>
        {
            ViewModel?.SelectedGroup?.Mappings.Remove(mapping);
        };
        menu.Items.Add(del);
        path.ContextMenu = menu;
        _wireCanvas.Children.Add(path);
    }
}
