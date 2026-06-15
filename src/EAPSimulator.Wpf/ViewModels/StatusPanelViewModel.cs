using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EAPSimulator.Wpf.ViewModels;

public partial class StatusPanelViewModel : ObservableObject
{
    [ObservableProperty] private string _connectionStatus = "未连接";
    [ObservableProperty] private string _connectionColor = "#F44747";
    [ObservableProperty] private string _equipmentStatus = "Offline";
    [ObservableProperty] private string _protocolType = "SECS/GEM";
    [ObservableProperty] private string _role = "Active";
    [ObservableProperty] private int _messageCount;
    [ObservableProperty] private string _lastMessageTime = "-";

    public ObservableCollection<StatusVariableEntry> StatusVariables { get; } = new();
    public ObservableCollection<AlarmEntry> Alarms { get; } = new();
    public ObservableCollection<CollectionEventEntry> CollectionEvents { get; } = new();
}

public class StatusVariableEntry
{
    public int Svid { get; set; }
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public string Unit { get; set; } = "";
}

public class AlarmEntry
{
    public int Alid { get; set; }
    public string Name { get; set; } = "";
    public string Status { get; set; } = "-";
    public SolidColorBrush StatusColor { get; set; } = new(Color.FromRgb(0x80, 0x80, 0x80));
}

public class CollectionEventEntry
{
    public int Ceid { get; set; }
    public string Name { get; set; } = "";
    public bool Enabled { get; set; }
}
