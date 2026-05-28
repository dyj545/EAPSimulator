using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Avalonia.Threading;
using EAPSimulator.UI.ViewModels;

namespace EAPSimulator.UI.Views;

public partial class AutoReplyView : UserControl
{
    private ViewModels.AutoReplyViewModel? ViewModel => DataContext as ViewModels.AutoReplyViewModel;

    public AutoReplyView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        SetupNullVisibility();
    }

    private void SetupNullVisibility()
    {
        var vm = ViewModel;
        if (vm == null) return;

        vm.PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(vm.SelectedQuickReply):
                    var hasQuickReply = vm.SelectedQuickReply != null;
                    if (QuickReplyDetailContent != null)
                        QuickReplyDetailContent.IsVisible = hasQuickReply;
                    if (QuickReplyEmptyState != null)
                        QuickReplyEmptyState.IsVisible = !hasQuickReply;
                    break;

                case nameof(vm.SelectedScenario):
                    if (ScenarioSettingsBorder != null)
                        ScenarioSettingsBorder.IsVisible = vm.SelectedScenario != null;
                    break;

                case nameof(vm.SelectedStep):
                    var hasStep = vm.SelectedStep != null;
                    if (StepDetailBorder != null)
                        StepDetailBorder.IsVisible = hasStep;
                    if (StepEmptyState != null)
                        StepEmptyState.IsVisible = !hasStep;
                    break;
            }
        };

        // Initialize visibility
        if (QuickReplyDetailContent != null)
            QuickReplyDetailContent.IsVisible = vm.SelectedQuickReply != null;
        if (QuickReplyEmptyState != null)
            QuickReplyEmptyState.IsVisible = vm.SelectedQuickReply == null;
        if (ScenarioSettingsBorder != null)
            ScenarioSettingsBorder.IsVisible = vm.SelectedScenario != null;
        if (StepDetailBorder != null)
            StepDetailBorder.IsVisible = vm.SelectedStep != null;
        if (StepEmptyState != null)
            StepEmptyState.IsVisible = vm.SelectedStep == null;
    }

    private void OnQuickReplyTabClick(object? sender, RoutedEventArgs e)
    {
        QuickReplyPanel.IsVisible = true;
        ScenarioPanel.IsVisible = false;
    }

    private void OnScenarioTabClick(object? sender, RoutedEventArgs e)
    {
        QuickReplyPanel.IsVisible = false;
        ScenarioPanel.IsVisible = true;
    }

    private async void OnBrowseFileClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择自动回复规则文件",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("JSON Files") { Patterns = ["*.json"] }, FilePickerFileTypes.All],
        });

        if (files.Count > 0 && ViewModel != null)
        {
            var path = files[0].Path.LocalPath;
            ViewModel.LoadFromPath(path);
            ViewModel.StatusMessage = $"已加载 {System.IO.Path.GetFileName(path)}";
        }
    }

    private void OnActionTemplateSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && e.AddedItems.Count > 0 && e.AddedItems[0] is string selected)
        {
            if (cb.DataContext is ViewModels.ScenarioStepViewModel step)
                step.ActionTemplateName = selected;
            ViewModel?.UpdateDisplayedMessage(selected);
        }
    }

    private void OnReplyTemplateSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && e.AddedItems.Count > 0 && e.AddedItems[0] is string selected)
        {
            if (cb.DataContext is ViewModels.QuickReplyRuleViewModel rule)
                rule.ReplyTemplateName = selected;
            ViewModel?.UpdateDisplayedMessage(selected);
        }
    }

    // ===== Helper methods =====

    private Window? FindOwnerWindow() =>
        TopLevel.GetTopLevel(this) as Window;

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
        return null;
    }

    // ===== TreeView event handlers =====

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

    private async void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control ctrl) return;
        var item = ctrl.DataContext as SecsItemViewModel;
        if (item == null) return;

        // LIST 节点：双击切换展开/折叠
        if (item.IsList)
        {
            e.Handled = true;
            // Find the TreeView that contains this item
            var treeView = ctrl is TreeView tv ? tv : FindParentTreeView(ctrl);
            var tvi = treeView != null ? FindTreeViewItemForData(treeView, item) : null;
            item.IsExpanded = !item.IsExpanded;
            if (tvi != null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    tvi.IsExpanded = item.IsExpanded;
                }, DispatcherPriority.Loaded);
            }
            return;
        }

        // 非 LIST 节点：双击弹出字段编辑
        var owner = FindOwnerWindow();
        if (owner == null) return;
        await item.EditFieldAsync(owner);
    }

    private static TreeView? FindParentTreeView(Control ctrl)
    {
        var parent = ctrl as Avalonia.Visual;
        while (parent != null)
        {
            if (parent is TreeView tv) return tv;
            parent = parent.GetVisualParent();
        }
        return null;
    }

    private async void OnEditField(object? sender, RoutedEventArgs e)
    {
        var item = GetItemFromMenu(sender);
        if (item == null) return;
        var owner = FindOwnerWindow();
        if (owner == null) return;
        await item.EditFieldAsync(owner);
    }

    private void OnAddChild(object? sender, RoutedEventArgs e) =>
        GetItemFromMenu(sender)?.AddChildCommand.Execute("L");

    private void OnAddSibling(object? sender, RoutedEventArgs e) =>
        GetItemFromMenu(sender)?.AddSiblingCommand.Execute("A");

    private void OnCopyItem(object? sender, RoutedEventArgs e) =>
        GetItemFromMenu(sender)?.CopyThisCommand.Execute(null);

    private async void OnDeleteItem(object? sender, RoutedEventArgs e)
    {
        var item = GetItemFromMenu(sender);
        if (item == null) return;
        var owner = FindOwnerWindow();
        if (owner != null)
        {
            var confirmed = await ConfirmDelete(owner, $"确定删除节点 {item.TypeName} {item.ValueText}？");
            if (!confirmed) return;
        }
        item.DeleteThisCommand.Execute(null);
    }

    private void OnMoveUp(object? sender, RoutedEventArgs e) =>
        GetItemFromMenu(sender)?.MoveUpCommand.Execute(null);

    private void OnMoveDown(object? sender, RoutedEventArgs e) =>
        GetItemFromMenu(sender)?.MoveDownCommand.Execute(null);

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
}
