using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EAPSimulator.Wpf.ViewModels;
using Microsoft.Win32;

namespace EAPSimulator.Wpf.Views;

public partial class MessageEditorView : UserControl
{
    private MessageEditorViewModel VM => (MessageEditorViewModel)DataContext!;

    public MessageEditorView()
    {
        InitializeComponent();
        AddMessageCombo.ItemsSource = MessageEditorViewModel.CommonMessageTypes;
    }

    // ─── Toolbar ───

    private void OnSendClick(object sender, RoutedEventArgs e) => VM.SendSelectedCommand.Execute(null);
    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "JSON Files (*.json)|*.json", DefaultExt = ".json" };
        if (dlg.ShowDialog() == true)
        {
            VM.LoadFromFile(dlg.FileName);
            FilePathText.Text = dlg.FileName;
        }
    }
    private void OnSaveClick(object sender, RoutedEventArgs e) => VM.SaveToFile();
    private void OnAddMessageClick(object sender, RoutedEventArgs e)
    {
        VM.AddMessageCommand.Execute(AddMessageCombo.SelectedItem as string);
        RebuildAndExpand();
    }
    private void OnDeleteClick(object sender, RoutedEventArgs e) { VM.DeleteMessageCommand.Execute(null); RebuildAndExpand(); }
    private void OnDuplicateClick(object sender, RoutedEventArgs e) { VM.DuplicateMessageCommand.Execute(null); RebuildAndExpand(); }

    // ─── Tree Selection ───

    private void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        VM.SelectedTreeItem = e.NewValue;
        if (e.NewValue is SecsMessageViewModel msg) VM.SelectedMessage = msg;
    }

    // ─── Pair Buttons ───

    private void OnPairCloneClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is MessagePairViewModel pair)
            VM.DuplicatePairCommand.Execute(pair);
    }
    private void OnPairDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is MessagePairViewModel pair)
            VM.DeletePairCommand.Execute(pair);
    }

    // ─── Message Context Menu / Buttons ───

    private void OnMsgAddChild(object sender, RoutedEventArgs e)
    {
        if (VM.SelectedMessage?.RootItem?.IsList == true)
            VM.SelectedMessage.RootItem.AddChildCommand.Execute("A");
    }
    private void OnMsgAddChildBtn(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is SecsMessageViewModel msg && msg.RootItem?.IsList == true)
            msg.RootItem.AddChildCommand.Execute("A");
    }
    private void OnMsgDuplicate(object sender, RoutedEventArgs e) => VM.DuplicateMessageCommand.Execute(null);
    private void OnMsgCloneBtn(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is SecsMessageViewModel msg)
            VM.DuplicateMessageCommand.Execute(msg);
    }
    private void OnMsgDelete(object sender, RoutedEventArgs e) => VM.DeleteMessageCommand.Execute(null);
    private void OnMsgDeleteBtn(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is SecsMessageViewModel msg)
            VM.DeleteMessageCommand.Execute(msg);
    }

    // ─── Item Context Menu / Buttons ───

    private void OnEditField(object sender, RoutedEventArgs e)
    {
        if (VM.SelectedTreeItem is SecsItemViewModel item)
        {
            var dlg = new FieldEditDialog(item) { Owner = Window.GetWindow(this) };
            dlg.ShowDialog();
        }
    }
    private void OnAddChild(object sender, RoutedEventArgs e)
    {
        if (VM.SelectedTreeItem is SecsItemViewModel item)
            item.AddChildCommand.Execute("A");
    }
    private void OnAddSibling(object sender, RoutedEventArgs e)
    {
        if (VM.SelectedTreeItem is SecsItemViewModel item)
            item.AddSiblingCommand.Execute("A");
    }
    private void OnCopyItem(object sender, RoutedEventArgs e)
    {
        if (VM.SelectedTreeItem is SecsItemViewModel item)
            item.CopyThisCommand.Execute(null);
    }
    private void OnDeleteItem(object sender, RoutedEventArgs e)
    {
        if (VM.SelectedTreeItem is SecsItemViewModel item)
            item.DeleteThisCommand.Execute(null);
    }
    private void OnMoveUp(object sender, RoutedEventArgs e)
    {
        if (VM.SelectedTreeItem is SecsItemViewModel item)
            item.MoveUpCommand.Execute(null);
    }
    private void OnMoveDown(object sender, RoutedEventArgs e)
    {
        if (VM.SelectedTreeItem is SecsItemViewModel item)
            item.MoveDownCommand.Execute(null);
    }
    private void OnItemAddChild(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is SecsItemViewModel item)
            item.AddChildCommand.Execute("A");
    }
    private void OnItemDelete(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is SecsItemViewModel item)
            item.DeleteThisCommand.Execute(null);
    }

    // ─── Double-click Item → Edit ───

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (VM.SelectedTreeItem is SecsItemViewModel item)
        {
            var dlg = new FieldEditDialog(item) { Owner = Window.GetWindow(this) };
            dlg.ShowDialog();
        }
    }

    // ─── Tree Context Menu ───

    private void OnExpandAll(object sender, RoutedEventArgs e) => VM.ExpandAllCommand.Execute(null);
    private void OnCollapseAll(object sender, RoutedEventArgs e) => VM.CollapseAllCommand.Execute(null);

    // ─── Keyboard ───

    private void OnTreePreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && VM.SelectedTreeItem is SecsItemViewModel item)
        {
            item.DeleteThisCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            VM.CopySelectedCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            VM.PasteClipboardCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ─── Drag & Drop (simplified) ───

    private Point _dragStart;
    private void OnTreeMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            var pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _dragStart.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(pos.Y - _dragStart.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                // Basic drag — could be expanded for actual reordering
            }
        }
    }
    private void OnTreePreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
    }

    private void RebuildAndExpand() => VM.RebuildGroups();
}
