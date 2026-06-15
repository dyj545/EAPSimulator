using System.Windows;
using System.Windows.Controls;

namespace EAPSimulator.Wpf.Views;

public partial class StatusPanelView : UserControl
{
    public StatusPanelView()
    {
        InitializeComponent();
    }

    private void OnSwitchToLocal(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.StatusPanelViewModel vm)
            vm.EquipmentStatus = "Online/Local";
    }

    private void OnSwitchToRemote(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.StatusPanelViewModel vm)
            vm.EquipmentStatus = "Online/Remote";
    }

    private void OnSwitchToOffline(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.StatusPanelViewModel vm)
            vm.EquipmentStatus = "Offline";
    }
}
