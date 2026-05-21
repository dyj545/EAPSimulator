using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using EAPSimulator.UI.ViewModels;
using Avalonia.Threading;

namespace EAPSimulator.UI.Views;

public partial class MessageEditorView : UserControl
{
    private SecsItemViewModel? _dragSource;
    private Avalonia.Point _dragStartPoint;
    private bool _dragStarted;
    private TreeViewItem? _dropTargetTvi;

    public MessageEditorView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MessageEditorViewModel vm)
                vm.ExpandSelectedRequested += ExpandSelectedNode;
        };

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

        // Clear drag state — the second PointerPressed of the double-click
        // already set _dragSource; without clearing it, mouse movement
        // inside the modal dialog would be interpreted as a drag gesture.
        _dragSource = null;
        _dragStarted = false;
        if (_dropTargetTvi != null)
        {
            _dropTargetTvi.Classes.Remove("drag-over");
            _dropTargetTvi = null;
        }
        DragPreview.IsVisible = false;

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
}
