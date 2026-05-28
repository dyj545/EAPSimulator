using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.VisualTree;
using EAPSimulator.UI.ViewModels;
using Avalonia.Threading;
using Avalonia.Media;

namespace EAPSimulator.UI.Views;

public partial class MessageEditorView : UserControl
{
    private SecsItemViewModel? _dragSource;
    private Avalonia.Point _dragStartPoint;
    private bool _dragStarted;
    private TreeViewItem? _dropTargetTvi;
    private TreeStyleViewModel? _styleVm;
    private Popup? _colorPopup;

    public MessageEditorView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MessageEditorViewModel vm)
                vm.ExpandSelectedRequested += ExpandSelectedNode;
        };

        // Initialize tree style
        _styleVm = new TreeStyleViewModel(TreeStyleConfig.Instance);
        _styleVm.StyleChanged += ApplyTreeStyles;
        StyleSettingsBtn.Tag = _styleVm;
        ApplyTreeStyles();

        // 键盘快捷键
        this.AddHandler(KeyDownEvent, OnTreeViewKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);

        // 拖拽
        this.AddHandler(PointerPressedEvent, OnDragPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        this.AddHandler(PointerMovedEvent, OnDragPointerMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        this.AddHandler(PointerReleasedEvent, OnDragPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private void OnDragPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Button or TextBox or ComboBox) return;
        if (e.Source is not Control source) return;

        var vis = source as Avalonia.Visual;
        SecsItemViewModel? item = null;
        while (vis != null)
        {
            if (vis is TreeViewItem tvi && tvi.DataContext is SecsItemViewModel vm)
            {
                item = vm;
                break;
            }
            vis = vis.GetVisualParent();
        }
        if (item == null) return;

        var props = e.GetCurrentPoint(this).Properties;
        if (!props.IsLeftButtonPressed) return;

        _dragSource = item;
        _dragStartPoint = e.GetPosition(this);
        _dragStarted = false;
    }

    private void OnDragPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragSource == null) return;

        var pos = e.GetPosition(this);

        if (!_dragStarted)
        {
            var dx = Math.Abs(pos.X - _dragStartPoint.X);
            var dy = Math.Abs(pos.Y - _dragStartPoint.Y);
            if (dx < 5 && dy < 5) return;

            // Start drag: show preview
            _dragStarted = true;
            DragPreviewText.Text = _dragSource.TypeName == "L"
                ? $"L [{_dragSource.Children.Count}]"
                : $"{_dragSource.TypeName} {_dragSource.ValueText}";
            DragPreview.IsVisible = true;

            // Capture pointer so we keep receiving events even outside the window
            e.Pointer.Capture(this);
        }

        // Move preview to follow cursor
        DragPreview.Margin = new Avalonia.Thickness(pos.X + 12, pos.Y + 12, 0, 0);

        // Highlight drop target
        var element = this.InputHitTest(pos);
        TreeViewItem? targetTvi = null;
        if (element is Avalonia.Visual hit)
        {
            var p = hit;
            while (p != null)
            {
                if (p is TreeViewItem t && t.DataContext is SecsItemViewModel)
                {
                    targetTvi = t;
                    break;
                }
                p = p.GetVisualParent();
            }
        }

        // Remove previous highlight
        if (_dropTargetTvi != null && _dropTargetTvi != targetTvi)
        {
            _dropTargetTvi.Classes.Remove("drag-over");
        }

        // Add highlight to new target (skip self)
        if (targetTvi != null && targetTvi.DataContext != _dragSource)
        {
            _dropTargetTvi = targetTvi;
            if (!_dropTargetTvi.Classes.Contains("drag-over"))
                _dropTargetTvi.Classes.Add("drag-over");
        }
        else
        {
            _dropTargetTvi = null;
        }
    }

    private void OnDragPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragStarted || _dragSource == null)
        {
            _dragSource = null;
            _dragStarted = false;
            return;
        }

        // Get drop target
        var pos = e.GetPosition(this);
        var element = this.InputHitTest(pos);
        SecsItemViewModel? target = null;
        if (element is Avalonia.Visual hit)
        {
            var p = hit;
            while (p != null)
            {
                if (p is TreeViewItem tvi && tvi.DataContext is SecsItemViewModel vm)
                {
                    target = vm;
                    break;
                }
                p = p.GetVisualParent();
            }
        }

        // Perform reorder
        if (target != null && target != _dragSource && target.Parent != null && _dragSource.Parent != null)
        {
            var sourceParent = _dragSource.Parent;
            var targetParent = target.Parent;
            var oldIdx = sourceParent.Children.IndexOf(_dragSource);
            var targetIdx = targetParent.Children.IndexOf(target);

            if (sourceParent == targetParent)
            {
                sourceParent.Children.RemoveAt(oldIdx);
                if (targetIdx > oldIdx) targetIdx--;
                targetIdx = Math.Clamp(targetIdx, 0, sourceParent.Children.Count);
                sourceParent.Children.Insert(targetIdx, _dragSource);
            }
            else
            {
                sourceParent.Children.RemoveAt(oldIdx);
                targetIdx = Math.Clamp(targetIdx, 0, targetParent.Children.Count);
                targetParent.Children.Insert(targetIdx, _dragSource);
                _dragSource.Parent = targetParent;
            }
        }

        // Cleanup
        if (_dropTargetTvi != null)
        {
            _dropTargetTvi.Classes.Remove("drag-over");
            _dropTargetTvi = null;
        }
        DragPreview.IsVisible = false;
        e.Pointer.Capture(null);
        _dragSource = null;
        _dragStarted = false;
    }


    private MessageEditorViewModel? ViewModel => DataContext as MessageEditorViewModel;

    private static async Task<bool> ConfirmDelete(Window owner, string message)
    {
        var confirmed = false;
        var dialog = new Window
        {
            Title = "确认删除",
            Width = 320, Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new DockPanel
            {
                Margin = new Avalonia.Thickness(16),
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        [DockPanel.DockProperty] = Dock.Bottom,
                        Children =
                        {
                            new Button
                            {
                                Content = "确定", Width = 72, Padding = new Avalonia.Thickness(4),
                                Classes = { "accent" },
                                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                            },
                            new Button
                            {
                                Content = "取消", Width = 72, Padding = new Avalonia.Thickness(4),
                                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                            }
                        }
                    },
                    new TextBlock
                    {
                        Text = message, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    }
                }
            }
        };

        var buttons = ((StackPanel)((DockPanel)dialog.Content).Children[0]).Children;
        ((Button)buttons[0]).Click += (_, _) => { confirmed = true; dialog.Close(); };
        ((Button)buttons[1]).Click += (_, _) => dialog.Close();
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; dialog.Close(); }
        };

        await dialog.ShowDialog(owner);
        return confirmed;
    }

    private Window? FindOwnerWindow() =>
        TopLevel.GetTopLevel(this) as Window;

    /// <summary>Expand the selected TreeViewItem by one level (Ctrl+F).</summary>
    public void ExpandSelectedNode()
    {
        var selectedItem = MessageTreeView.SelectedItem;
        if (selectedItem == null) return;

        var tvi = FindTreeViewItemForData(MessageTreeView, selectedItem);
        if (tvi != null)
            tvi.IsExpanded = true;
    }

    /// <summary>Recursively find the TreeViewItem whose DataContext matches the given item.</summary>
    private static TreeViewItem? FindTreeViewItemForData(ItemsControl container, object dataItem)
    {
        for (int i = 0; i < container.ItemCount; i++)
        {
            if (container.ContainerFromIndex(i) is TreeViewItem tvi)
            {
                if (tvi.DataContext == dataItem) return tvi;
                var result = FindTreeViewItemForData(tvi, dataItem);
                if (result != null) return result;
            }
        }
        return null;
    }

    private void OnTreeViewSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel == null) return;
        var newValue = e.AddedItems.Count > 0 ? e.AddedItems[0] : null;
        if (newValue == null) return;

        ViewModel.SelectedTreeItem = newValue;

        if (newValue is SecsMessageViewModel msg)
            ViewModel.SelectedMessage = msg;
        else if (newValue is MessagePairViewModel pair)
        {
            if (pair.Messages.Count > 0)
                ViewModel.SelectedMessage = pair.Messages[0];
        }
    }

    // ===== Tree-level context menu (expand all / collapse all) =====

    private void OnExpandAll(object? sender, RoutedEventArgs e) =>
        ViewModel?.ExpandAllCommand.Execute(null);

    private void OnCollapseAll(object? sender, RoutedEventArgs e) =>
        ViewModel?.CollapseAllCommand.Execute(null);

    // ===== Right-click selection: ensure the right-clicked item is selected before context menu opens =====

    private void OnTreeViewContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Source is not Control source) return;
        var parent = source as Avalonia.Visual;
        while (parent != null)
        {
            if (parent is TreeViewItem tvi)
            {
                tvi.IsSelected = true;
                break;
            }
            parent = parent.GetVisualParent();
        }
    }

    // ===== Message-level context menu =====

    private void OnMsgAddChild(object? sender, RoutedEventArgs e)
    {
        var msg = GetMessageFromMenu(sender);
        if (msg?.RootItem == null) return;
        msg.RootItem.AddChildCommand.Execute("A");
    }

    private void OnMsgDuplicate(object? sender, RoutedEventArgs e)
    {
        var msg = GetMessageFromMenu(sender);
        if (msg == null || ViewModel == null) return;
        ViewModel.DuplicateMessageCommand.Execute(msg);
    }

    private async void OnMsgDelete(object? sender, RoutedEventArgs e)
    {
        var msg = GetMessageFromMenu(sender);
        if (msg == null || ViewModel == null) return;
        var owner = FindOwnerWindow();
        if (owner != null && !await ConfirmDelete(owner, $"确定删除消息 S{msg.Stream}F{msg.Function}？")) return;
        ViewModel.DeleteMessageCommand.Execute(msg);
    }

    // ===== Inline button Click handlers (inside MessageNodeTemplate) =====

    private void OnMsgCloneClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var msg = btn.Tag as SecsMessageViewModel;
        if (msg == null || ViewModel == null) return;
        ViewModel.DuplicateMessageCommand.Execute(msg);
    }

    private async void OnMsgDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var msg = btn.Tag as SecsMessageViewModel;
        if (msg == null || ViewModel == null) return;
        var owner = FindOwnerWindow();
        if (owner != null && !await ConfirmDelete(owner, $"确定删除消息 S{msg.Stream}F{msg.Function}？")) return;
        ViewModel.DeleteMessageCommand.Execute(msg);
    }

    // ===== Inline button Click handlers (inside PairNodeTemplate) =====

    private void OnPairCloneClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var pair = btn.Tag as MessagePairViewModel;
        if (pair == null || ViewModel == null) return;
        ViewModel.DuplicatePairCommand.Execute(pair);
    }

    private async void OnPairDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var pair = btn.Tag as MessagePairViewModel;
        if (pair == null || ViewModel == null) return;
        var owner = FindOwnerWindow();
        if (owner != null && !await ConfirmDelete(owner, $"确定删除消息组 {pair.Title}？")) return;
        ViewModel.DeletePairCommand.Execute(pair);
    }

    // ===== Toolbar Click handlers =====

    private void OnAddMessageClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        var selectedType = AddMessageCombo.SelectedItem as string;
        ViewModel.AddMessageCommand.Execute(selectedType);
    }

    private async void OnToolbarDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        var item = ViewModel.SelectedTreeItem;
        var owner = FindOwnerWindow();

        if (item is MessagePairViewModel pair)
        {
            if (owner != null && !await ConfirmDelete(owner, $"确定删除消息组 {pair.Title}？")) return;
            ViewModel.DeletePairCommand.Execute(pair);
        }
        else
        {
            var msg = ViewModel.SelectedMessage;
            if (msg == null) return;
            if (owner != null && !await ConfirmDelete(owner, $"确定删除消息 S{msg.Stream}F{msg.Function}？")) return;
            ViewModel.DeleteMessageCommand.Execute(msg);
        }
    }

    private void OnToolbarDuplicateClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        var item = ViewModel.SelectedTreeItem;

        if (item is MessagePairViewModel pair)
            ViewModel.DuplicatePairCommand.Execute(pair);
        else
            ViewModel.DuplicateMessageCommand.Execute(ViewModel.SelectedMessage);
    }

    // ===== Keyboard shortcuts =====

    private void OnTreeViewKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel == null) return;

        if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            ViewModel.CopySelectedCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            ViewModel.PasteClipboardCommand.Execute(null);
            e.Handled = true;
        }
    }

    private SecsMessageViewModel? GetMessageFromMenu(object? sender)
    {
        if (sender is not MenuItem mi) return null;
        if (mi.Parent is not ContextMenu cm) return null;
        // 尝试从 PlacementTarget 向上查找 DataContext
        if (cm.PlacementTarget is Control ctrl)
        {
            var dc = ctrl.DataContext as SecsMessageViewModel;
            if (dc != null) return dc;
            var parent = ctrl as Avalonia.Visual;
            while (parent != null)
            {
                if (parent is TreeViewItem tvi && tvi.DataContext is SecsMessageViewModel m)
                    return m;
                parent = parent.GetVisualParent();
            }
        }
        // 回退：使用当前选中项（右键点击时 SelectionChanged 会更新选中项）
        return ViewModel?.SelectedMessage;
    }

    // ===== File dialog handlers =====

    private async void OnBrowseFileClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "打开消息模板文件",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("JSON Files") { Patterns = ["*.json"] }, FilePickerFileTypes.All],
        });

        if (files.Count > 0 && ViewModel != null)
        {
            ViewModel.LoadFromFile(files[0].Path.LocalPath);
        }
    }

    private async void OnSaveFileClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;

        if (!string.IsNullOrEmpty(ViewModel.FilePath))
        {
            ViewModel.SaveFileCommand.Execute(null);
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "保存消息模板文件",
            SuggestedFileName = "secs_message_templates.json",
            FileTypeChoices = [new FilePickerFileType("JSON Files") { Patterns = ["*.json"] }],
        });

        if (file != null)
        {
            ViewModel.FilePath = file.Path.LocalPath;
            ViewModel.SaveFileCommand.Execute(null);
        }
    }

    // ===== Item-level context menu =====

    private SecsItemViewModel? GetItemFromMenu(object? sender)
    {
        if (sender is not MenuItem mi) return null;
        if (mi.Parent is not ContextMenu cm) return null;
        if (cm.PlacementTarget is Control ctrl)
        {
            var dc = ctrl.DataContext as SecsItemViewModel;
            if (dc != null) return dc;
            var parent = ctrl as Avalonia.Visual;
            while (parent != null)
            {
                if (parent is TreeViewItem tvi && tvi.DataContext is SecsItemViewModel item)
                    return item;
                parent = parent.GetVisualParent();
            }
        }
        // 回退：使用当前选中项
        if (ViewModel?.SelectedTreeItem is SecsItemViewModel selectedItem)
            return selectedItem;
        return null;
    }

    private void OnAddChild(object? sender, RoutedEventArgs e) => GetItemFromMenu(sender)?.AddChildCommand.Execute("L");
    private void OnAddSibling(object? sender, RoutedEventArgs e) => GetItemFromMenu(sender)?.AddSiblingCommand.Execute("A");
    private void OnCopyItem(object? sender, RoutedEventArgs e) => GetItemFromMenu(sender)?.CopyThisCommand.Execute(null);
    private async void OnDeleteItem(object? sender, RoutedEventArgs e)
    {
        var item = GetItemFromMenu(sender);
        if (item == null) return;
        var owner = FindOwnerWindow();
        if (owner != null && !await ConfirmDelete(owner, $"确定删除节点 {item.TypeName} {item.ValueText}？")) return;
        item.DeleteThisCommand.Execute(null);
    }
    private void OnMoveUp(object? sender, RoutedEventArgs e) => GetItemFromMenu(sender)?.MoveUpCommand.Execute(null);
    private void OnMoveDown(object? sender, RoutedEventArgs e) => GetItemFromMenu(sender)?.MoveDownCommand.Execute(null);

    // ===== Field Edit =====

    private async void OnItemDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (sender is not Control ctrl) return;
        var item = ctrl.DataContext as SecsItemViewModel;
        if (item == null) return;

        // 清除拖拽状态
        _dragSource = null;
        _dragStarted = false;
        if (_dropTargetTvi != null)
        {
            _dropTargetTvi.Classes.Remove("drag-over");
            _dropTargetTvi = null;
        }
        DragPreview.IsVisible = false;

        // LIST 节点：双击切换展开/折叠
        if (item.IsList)
        {
            e.Handled = true;
            var tvi = FindTreeViewItemForData(MessageTreeView, item);
            item.IsExpanded = !item.IsExpanded;
            if (tvi != null)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    tvi.IsExpanded = item.IsExpanded;
                }, Avalonia.Threading.DispatcherPriority.Loaded);
            }
            return;
        }

        // 非 LIST 节点：双击弹出字段编辑
        var owner = FindOwnerWindow();
        if (owner == null) return;
        await item.EditFieldAsync(owner);
    }

    private async void OnEditField(object? sender, RoutedEventArgs e)
    {
        var item = GetItemFromMenu(sender);
        if (item == null) return;
        var owner = FindOwnerWindow();
        if (owner == null) return;
        await item.EditFieldAsync(owner);
    }

    // ===== Tree Style =====

    private void ApplyTreeStyles()
    {
        if (_styleVm == null) return;
        var config = TreeStyleConfig.Instance;

        // Update color resources (DynamicResource bindings in DataTemplates will refresh)
        this.Resources["TreeTypeNameColor"] = new SolidColorBrush(Color.Parse(config.TypeNameColor));
        this.Resources["TreeValueColor"] = new SolidColorBrush(Color.Parse(config.ValueColor));
        this.Resources["TreeAliasColor"] = new SolidColorBrush(Color.Parse(config.AliasColor));
        this.Resources["TreeListInfoColor"] = new SolidColorBrush(Color.Parse(config.ListInfoColor));
        this.Resources["TreeGroupColor"] = new SolidColorBrush(Color.Parse(config.GroupColor));
        this.Resources["TreePairTitleColor"] = new SolidColorBrush(Color.Parse(config.PairTitleColor));
        this.Resources["TreePairDescColor"] = new SolidColorBrush(Color.Parse(config.PairDescColor));
        this.Resources["TreeStreamFuncColor"] = new SolidColorBrush(Color.Parse(config.StreamFuncColor));
        this.Resources["TreeStreamFuncPrefixColor"] = new SolidColorBrush(Color.Parse(config.StreamFuncPrefixColor));
        this.Resources["TreeWBitColor"] = new SolidColorBrush(Color.Parse(config.WBitColor));
        this.Resources["TreeMessageNameColor"] = new SolidColorBrush(Color.Parse(config.MessageNameColor));
        this.Resources["TreeSelectedColor"] = new SolidColorBrush(Color.Parse(config.SelectedColor));

        // Update TreeView font properties directly
        MessageTreeView.FontFamily = new FontFamily(config.FontFamily);
        MessageTreeView.FontSize = config.FontSize;
        MessageTreeView.Foreground = new SolidColorBrush(Color.Parse(config.ValueColor));
    }

    // ===== Color Picker =====

    private void OnColorSwatchPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border swatch || _styleVm == null) return;
        var propertyName = swatch.Tag as string;
        if (string.IsNullOrEmpty(propertyName)) return;

        // Get current color from ViewModel
        var currentColor = propertyName switch
        {
            "TypeNameColor" => _styleVm.TypeNameColor,
            "ValueColor" => _styleVm.ValueColor,
            "AliasColor" => _styleVm.AliasColor,
            "ListInfoColor" => _styleVm.ListInfoColor,
            "GroupColor" => _styleVm.GroupColor,
            "PairTitleColor" => _styleVm.PairTitleColor,
            "PairDescColor" => _styleVm.PairDescColor,
            "StreamFuncColor" => _styleVm.StreamFuncColor,
            "StreamFuncPrefixColor" => _styleVm.StreamFuncPrefixColor,
            "WbitColor" => _styleVm.WbitColor,
            "MessageNameColor" => _styleVm.MessageNameColor,
            "SelectedColor" => _styleVm.SelectedColor,
            _ => "#FFFFFF"
        };

        // Close any existing popup
        CloseColorPopup();

        // Build color swatches grid
        var panel = new StackPanel { Margin = new Avalonia.Thickness(8) };
        panel.Children.Add(new TextBlock
        {
            Text = $"选择 {propertyName}",
            FontWeight = FontWeight.SemiBold,
            FontSize = 11,
            Margin = new Avalonia.Thickness(0, 0, 0, 6)
        });

        var colors = TreeStyleViewModel.PresetColors;
        int cols = 10;
        int rows = (colors.Length + cols - 1) / cols;

        for (int r = 0; r < rows; r++)
        {
            var row = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Margin = new Avalonia.Thickness(0, 0, 0, 2)
            };

            for (int c = 0; c < cols; c++)
            {
                int idx = r * cols + c;
                if (idx >= colors.Length) break;

                var colorHex = colors[idx];
                var isSelected = string.Equals(colorHex, currentColor, StringComparison.OrdinalIgnoreCase);

                var cell = new Border
                {
                    Width = 22,
                    Height = 22,
                    Margin = new Avalonia.Thickness(1),
                    CornerRadius = new Avalonia.CornerRadius(3),
                    Background = new SolidColorBrush(Color.Parse(colorHex)),
                    BorderBrush = isSelected ? new SolidColorBrush(Colors.White) : new SolidColorBrush(Color.Parse("#333333")),
                    BorderThickness = new Avalonia.Thickness(isSelected ? 2 : 1),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Tag = colorHex,
                };
                ToolTip.SetTip(cell, colorHex);

                cell.PointerPressed += OnColorCellPressed;
                row.Children.Add(cell);
            }

            panel.Children.Add(row);
        }

        // Current color display
        var currentRow = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Margin = new Avalonia.Thickness(0, 6, 0, 0)
        };
        currentRow.Children.Add(new TextBlock
        {
            Text = "当前: ",
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            FontSize = 11
        });
        currentRow.Children.Add(new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new Avalonia.CornerRadius(2),
            Background = new SolidColorBrush(Color.Parse(currentColor)),
            BorderBrush = new SolidColorBrush(Color.Parse("#555555")),
            BorderThickness = new Avalonia.Thickness(1),
            Margin = new Avalonia.Thickness(4, 0, 4, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });
        currentRow.Children.Add(new TextBlock
        {
            Text = currentColor,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            FontSize = 11,
            FontFamily = new FontFamily("Consolas")
        });
        panel.Children.Add(currentRow);

        // Create popup
        _colorPopup = new Popup
        {
            PlacementTarget = swatch,
            Placement = PlacementMode.RightEdgeAlignedTop,
            IsLightDismissEnabled = true,
            WindowManagerAddShadowHint = false,
            Child = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#2D2D2D")),
                BorderBrush = new SolidColorBrush(Color.Parse("#555555")),
                BorderThickness = new Avalonia.Thickness(1),
                CornerRadius = new Avalonia.CornerRadius(6),
                Padding = new Avalonia.Thickness(4),
                Child = panel
            }
        };

        _colorPopup.Closed += (_, _) => _colorPopup = null;

        // Store property name and VM reference for the handler
        _colorPopup.Tag = (propertyName, _styleVm);

        // Add to visual tree and open
        swatch.Resources["ActiveColorPopup"] = _colorPopup;
        _colorPopup.PlacementTarget = swatch;
        _colorPopup.IsOpen = true;
    }

    private void OnColorCellPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border cell) return;
        var colorHex = cell.Tag as string;
        if (string.IsNullOrEmpty(colorHex)) return;

        // Find the popup and get the property name
        var parent = cell.GetVisualParent();
        while (parent is not Popup && parent != null)
            parent = parent.GetVisualParent();

        if (parent is not Popup popup) return;
        if (popup.Tag is not (string propertyName, TreeStyleViewModel vm)) return;

        // Set color on ViewModel
        switch (propertyName)
        {
            case "TypeNameColor": vm.TypeNameColor = colorHex; break;
            case "ValueColor": vm.ValueColor = colorHex; break;
            case "AliasColor": vm.AliasColor = colorHex; break;
            case "ListInfoColor": vm.ListInfoColor = colorHex; break;
            case "GroupColor": vm.GroupColor = colorHex; break;
            case "PairTitleColor": vm.PairTitleColor = colorHex; break;
            case "PairDescColor": vm.PairDescColor = colorHex; break;
            case "StreamFuncColor": vm.StreamFuncColor = colorHex; break;
            case "StreamFuncPrefixColor": vm.StreamFuncPrefixColor = colorHex; break;
            case "WbitColor": vm.WbitColor = colorHex; break;
            case "MessageNameColor": vm.MessageNameColor = colorHex; break;
            case "SelectedColor": vm.SelectedColor = colorHex; break;
        }

        popup.IsOpen = false;
    }

    private void CloseColorPopup()
    {
        if (_colorPopup != null)
        {
            _colorPopup.IsOpen = false;
            _colorPopup = null;
        }
    }

    // ===== Preset Style =====

    private void OnApplyPresetClick(object? sender, RoutedEventArgs e)
    {
        if (_styleVm == null) return;
        var selected = PresetStyleCombo.SelectedItem as string;
        if (string.IsNullOrEmpty(selected)) return;
        _styleVm.ApplyPresetCommand.Execute(selected);
    }

    private void OnStyleFlyoutKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        _styleVm?.SaveCommand.Execute(null);
        StyleSettingsBtn.Flyout?.Hide();
    }
}
