using CommunityToolkit.Mvvm.ComponentModel;

namespace EAPSimulator.Wpf.ViewModels;

public partial class ConfigViewModel : ObservableObject
{
    [ObservableProperty] private string _localHost = "0.0.0.0";
    [ObservableProperty] private int _localPort = 5000;
    [ObservableProperty] private string _remoteHost = "127.0.0.1";
    [ObservableProperty] private int _remotePort = 5000;
    [ObservableProperty] private string _deviceId = "1";
    [ObservableProperty] private bool _acceptCommunication = true;
    [ObservableProperty] private int _t3Timeout = 45;
    [ObservableProperty] private int _t5Timeout = 10;
    [ObservableProperty] private int _t6Timeout = 5;
    [ObservableProperty] private int _t7Timeout = 10;
    [ObservableProperty] private int _t8Timeout = 5;
    [ObservableProperty] private string _customHost = "127.0.0.1";
    [ObservableProperty] private int _customPort = 6000;
    [ObservableProperty] private string _customConfigPath = "custom_protocol.json";
    [ObservableProperty] private bool _customIsServer;
}
