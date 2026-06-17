using Avalonia.Controls;
using Avalonia.Input;

namespace EAPSimulator.UI.Views;

public partial class HostMessageEditorView : UserControl
{
    private ViewModels.HostMessageEditorViewModel? VM => DataContext as ViewModels.HostMessageEditorViewModel;

    public HostMessageEditorView()
    {
        InitializeComponent();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+S: save
        if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            VM?.SaveCommand.Execute(null);
        }
        // Ctrl+Enter: send test
        if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            VM?.SendTestCommand.Execute(null);
        }
    }
}
