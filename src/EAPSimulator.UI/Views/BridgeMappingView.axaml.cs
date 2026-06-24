using Avalonia.Controls;
using Avalonia.Interactivity;

namespace EAPSimulator.UI.Views;

public partial class BridgeMappingView : UserControl
{
    private ViewModels.BridgeMappingViewModel? ViewModel => DataContext as ViewModels.BridgeMappingViewModel;

    public BridgeMappingView()
    {
        InitializeComponent();
    }

    private void OnTableViewClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ViewModel.IsCanvasView = false;
    }

    private void OnCanvasViewClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ViewModel.IsCanvasView = true;
    }
}
