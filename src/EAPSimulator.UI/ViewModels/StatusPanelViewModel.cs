using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace EAPSimulator.UI.ViewModels;

public partial class StatusPanelViewModel : ObservableObject
{
    [ObservableProperty]
    private string _connectionState = "Disconnected";

    [ObservableProperty]
    private string _connectionStateColor = "#D32F2F";

    [ObservableProperty]
    private string _gemState = "Offline";

    [ObservableProperty]
    private string _gemStateColor = "#FF9800";

    [ObservableProperty]
    private string _protocolType = "SECS/GEM";

    [ObservableProperty]
    private string _role = "Equipment";

    [ObservableProperty]
    private string _remoteEndpoint = "-";

    [ObservableProperty]
    private bool _canSwitchState;

    [ObservableProperty]
    private bool _isOnlineLocal;

    [ObservableProperty]
    private bool _isOnlineRemote;

    [ObservableProperty]
    private bool _isOffline = true;

    public ObservableCollection<StatusVariableViewModel> StatusVariables { get; } = new();
    public ObservableCollection<AlarmViewModel> Alarms { get; } = new();
    public ObservableCollection<CollectionEventViewModel> CollectionEvents { get; } = new();

    /// <summary>
    /// Host channels list shared with <see cref="ConfigViewModel.HostChannels"/>. Set by
    /// MainViewModel after Config has loaded the channel definitions; rendered as a row
    /// of indicator lights in the status panel and main toolbar. Each
    /// <see cref="HostChannelViewModel"/> drives its own connect / disconnect commands
    /// and runtime <c>IsConnected</c>/<c>StatusText</c>/<c>StatusColor</c> already.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<HostChannelViewModel>? _hostChannels;

    /// <summary>
    /// Event fired when user clicks a state transition button.
    /// MainViewModel subscribes to this to trigger GEM state machine and send SECS messages.
    /// </summary>
    public event EventHandler<string>? GemStateChangeRequested;

    [RelayCommand]
    private void SwitchToOnlineLocal() => GemStateChangeRequested?.Invoke(this, "OnlineLocal");

    [RelayCommand]
    private void SwitchToOnlineRemote() => GemStateChangeRequested?.Invoke(this, "OnlineRemote");

    [RelayCommand]
    private void SwitchToOffline() => GemStateChangeRequested?.Invoke(this, "Offline");

    public void UpdateConnectionState(bool connected)
    {
        ConnectionState = connected ? "Connected" : "Disconnected";
        ConnectionStateColor = connected ? "#4CAF50" : "#D32F2F";
        CanSwitchState = connected;
    }

    public void UpdateGemState(string state)
    {
        GemState = state;
        GemStateColor = state switch
        {
            "OnlineLocal" => "#4CAF50",
            "OnlineRemote" => "#2196F3",
            "AttemptOnline" => "#FF9800",
            _ => "#FF9800",
        };

        IsOnlineLocal = state == "OnlineLocal";
        IsOnlineRemote = state == "OnlineRemote";
        IsOffline = state == "Offline" || state == "AttemptOnline";
    }
}

public class StatusVariableViewModel
{
    public ushort Svid { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
}

public class AlarmViewModel
{
    public ushort Alid { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsSet { get; set; }
    public string Status => IsSet ? "SET" : "CLEAR";
    public string StatusColor => IsSet ? "#D32F2F" : "#4CAF50";
}

public class CollectionEventViewModel
{
    public ushort Ceid { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}
