using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace EAPSimulator.UI.ViewModels;

public partial class MessageLogViewModel : ObservableObject
{
    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private MessageLogEntry? _selectedEntry;

    [ObservableProperty]
    private string _detailText = string.Empty;

    public ObservableCollection<MessageLogEntry> Entries { get; } = new();

    partial void OnSelectedEntryChanged(MessageLogEntry? value)
    {
        DetailText = value?.Detail ?? string.Empty;
    }

    public void AddEntry(MessageLogEntry entry)
    {
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            Entries.Insert(0, entry);
            // Keep max 5000 entries
            while (Entries.Count > 5000)
                Entries.RemoveAt(Entries.Count - 1);
        });
    }

    [RelayCommand]
    private void Clear()
    {
        Entries.Clear();
        DetailText = string.Empty;
    }
}

public class MessageLogEntry
{
    public DateTime Timestamp { get; set; }
    public string Direction { get; set; } = string.Empty;
    public string DirectionColor => Direction switch
    {
        "<<" => "#4CAF50",   // Receive = green
        ">>" => "#2196F3",   // Send = blue
        "SYS" => "#FF9800",  // System = orange
        "ERR" => "#F44336",  // Error = red
        _ => "#CCCCCC",
    };
    public string MessageId { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}
