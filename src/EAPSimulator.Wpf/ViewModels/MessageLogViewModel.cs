using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EAPSimulator.Wpf.ViewModels;

public partial class MessageLogViewModel : ObservableObject
{
    public ObservableCollection<LogEntry> LogEntries { get; } = [];

    [ObservableProperty]
    private string _filterText = "";

    public void AddLog(string direction, string stream, string function, string content)
    {
        LogEntries.Add(new LogEntry
        {
            Timestamp = DateTime.Now.ToString("HH:mm:ss.fff"),
            Direction = direction,
            StreamFunction = $"S{stream}F{function}",
            Content = content
        });

        if (LogEntries.Count > 1000)
            LogEntries.RemoveAt(0);
    }
}

public class LogEntry
{
    public string Timestamp { get; set; } = "";
    public string Direction { get; set; } = "";
    public string StreamFunction { get; set; } = "";
    public string Content { get; set; } = "";
}
