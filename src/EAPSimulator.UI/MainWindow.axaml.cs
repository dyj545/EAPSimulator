using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace EAPSimulator.UI;

public partial class MainWindow : Window
{
    private ViewModels.MainViewModel ViewModel => (ViewModels.MainViewModel)DataContext!;

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OnMainWindowKeyDown(object? sender, KeyEventArgs e)
    {
        var editor = ViewModel.MessageEditor;

        // Ctrl+S: send selected message template
        if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            if (editor.SelectedMessage != null)
            {
                await ViewModel.SendEditorMessageAsync(editor.SelectedMessage);
            }
        }
        // Ctrl+F: expand next level of selected node
        else if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            editor.ExpandSelectedCommand.Execute(null);
        }
    }

    private void OnEditorTabClick(object? sender, RoutedEventArgs e)
    {
        EditorView.IsVisible = true;
        AutoReplyView.IsVisible = false;
        HostEditorView.IsVisible = false;
        BridgeMappingView.IsVisible = false;
        RestorePanels();
    }

    private void OnAutoReplyTabClick(object? sender, RoutedEventArgs e)
    {
        EditorView.IsVisible = false;
        AutoReplyView.IsVisible = true;
        HostEditorView.IsVisible = false;
        BridgeMappingView.IsVisible = false;
        ExpandLeftPanel();
    }

    private void OnHostEditorTabClick(object? sender, RoutedEventArgs e)
    {
        EditorView.IsVisible = false;
        AutoReplyView.IsVisible = false;
        HostEditorView.IsVisible = true;
        BridgeMappingView.IsVisible = false;
        ExpandLeftPanel();
        ViewModel.HostEditor.RefreshPreview();
    }

    private void OnBridgeMappingTabClick(object? sender, RoutedEventArgs e)
    {
        EditorView.IsVisible = false;
        AutoReplyView.IsVisible = false;
        HostEditorView.IsVisible = false;
        BridgeMappingView.IsVisible = true;
        ExpandLeftPanel();
    }

    private void RestorePanels()
    {
        // Restore 3-panel layout
        Grid.SetColumnSpan(LeftPanel, 1);
        Splitter1.IsVisible = true;
        MessageLogPanel.IsVisible = true;
        Splitter2.IsVisible = true;
        StatusPanel.IsVisible = true;
    }

    private void ExpandLeftPanel()
    {
        // Full-screen left panel: hide right panels
        Grid.SetColumnSpan(LeftPanel, 5);
        Splitter1.IsVisible = false;
        MessageLogPanel.IsVisible = false;
        Splitter2.IsVisible = false;
        StatusPanel.IsVisible = false;
    }

    private async void OnConfigButtonClick(object? sender, RoutedEventArgs e)
    {
        var configWindow = new ConfigWindow
        {
            DataContext = ViewModel,
        };
        await configWindow.ShowDialog(this);
    }
}
