using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace EAPSimulator.UI.Views;

public partial class ConfigView : UserControl
{
    private ViewModels.ConfigViewModel? ViewModel => DataContext as ViewModels.ConfigViewModel;

    public ConfigView()
    {
        InitializeComponent();
    }

    private async void OnBrowseCustomConfigClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择配置文件",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("JSON Files") { Patterns = ["*.json"] }, FilePickerFileTypes.All],
        });

        if (files.Count > 0 && ViewModel != null)
        {
            ViewModel.CustomConfigPath = files[0].Path.LocalPath;
        }
    }
}
