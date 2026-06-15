using System.Windows;
using System.Windows.Controls;

namespace EAPSimulator.Wpf.Views;

public partial class MessageLogView : UserControl
{
    public MessageLogView()
    {
        InitializeComponent();
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MessageLogViewModel vm)
            vm.LogEntries.Clear();
    }

    private void OnToggleAllFilters(object sender, RoutedEventArgs e)
    {
        var allChecked = ShowSystem.IsChecked == true
                      && ShowSend.IsChecked == true
                      && ShowRecv.IsChecked == true
                      && ShowError.IsChecked == true;
        var newValue = !allChecked;
        ShowSystem.IsChecked = newValue;
        ShowSend.IsChecked = newValue;
        ShowRecv.IsChecked = newValue;
        ShowError.IsChecked = newValue;
    }
}
