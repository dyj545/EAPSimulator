using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EAPSimulator.UI.ViewModels;

public partial class StatusPanelViewModel : ObservableObject
{
    [ObservableProperty]
    private string _connectionState = "Disconnected";

    [ObservableProperty]
    private string _connectionStateColor = "#F44336";

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

    public ObservableCollection<StatusVariableViewModel> StatusVariables { get; } = new();
    public ObservableCollection<AlarmViewModel> Alarms { get; } = new();
    public ObservableCollection<CollectionEventViewModel> CollectionEvents { get; } = new();

    public void UpdateConnectionState(bool connected)
    {
        ConnectionState = connected ? "Connected" : "Disconnected";
        ConnectionStateColor = connected ? "#4CAF50" : "#F44336";
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
    public string StatusColor => IsSet ? "#F44336" : "#4CAF50";
}

public class CollectionEventViewModel
{
    public ushort Ceid { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}
