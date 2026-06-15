using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace EAPSimulator.Wpf.Views;

public partial class ConfigView : UserControl
{
    public ConfigView()
    {
        InitializeComponent();
    }

    private void OnBrowseCustomConfigClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "JSON Files (*.json)|*.json", DefaultExt = ".json" };
        if (dlg.ShowDialog() == true)
        {
            if (DataContext is ViewModels.ConfigViewModel vm)
                vm.CustomConfigPath = dlg.FileName;
        }
    }
}
