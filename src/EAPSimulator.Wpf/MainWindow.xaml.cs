using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MahApps.Metro.Controls;

namespace EAPSimulator.Wpf;

public partial class MainWindow : MetroWindow
{
    private ViewModels.MainViewModel ViewModel => (ViewModels.MainViewModel)DataContext!;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnEditorTabClick(object sender, RoutedEventArgs e)
    {
        EditorView.Visibility = Visibility.Visible;
        AutoReplyView.Visibility = Visibility.Collapsed;

        // Restore 3-panel layout: left panel back to 1 column span
        Grid.SetColumnSpan(LeftPanel, 1);
        Splitter1.Visibility = Visibility.Visible;
        MessageLogPanel.Visibility = Visibility.Visible;
        Splitter2.Visibility = Visibility.Visible;
        StatusPanel.Visibility = Visibility.Visible;
    }

    private void OnAutoReplyTabClick(object sender, RoutedEventArgs e)
    {
        EditorView.Visibility = Visibility.Collapsed;
        AutoReplyView.Visibility = Visibility.Visible;

        // Full-width for auto-reply: hide right panels
        Grid.SetColumnSpan(LeftPanel, 5);
    }

    private void OnStartListening(object sender, RoutedEventArgs e)
    {
        tbStatus.Text = "正在连接...";
        tbStatus.Foreground = new SolidColorBrush(Colors.Orange);
    }

    private void OnDisconnect(object sender, RoutedEventArgs e)
    {
        tbStatus.Text = "已断开";
        tbStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
    }

    private void OnConfigClick(object sender, RoutedEventArgs e)
    {
        var configWindow = new ConfigWindow
        {
            DataContext = ViewModel,
            Owner = this,
        };
        configWindow.ShowDialog();
    }
}
