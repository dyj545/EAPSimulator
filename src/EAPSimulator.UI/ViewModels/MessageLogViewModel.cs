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

    // Filter toggles
    [ObservableProperty]
    private bool _showSystem = true;

    [ObservableProperty]
    private bool _showSend = true;

    [ObservableProperty]
    private bool _showReceive = true;

    [ObservableProperty]
    private bool _showError = true;

    [ObservableProperty]
    private bool _headerFilterEnabled;

    // Log size limit
    [ObservableProperty]
    private int _maxEntries = 5000;

    // Statistics
    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _filteredCount;

    /// <summary>Full log entries (source of truth).</summary>
    public ObservableCollection<MessageLogEntry> Entries { get; } = new();

    /// <summary>Filtered entries for display.</summary>
    public ObservableCollection<MessageLogEntry> FilteredEntries { get; } = new();

    /// <summary>Available message headers for filtering (e.g. S1F1, S6F11).</summary>
    public ObservableCollection<MessageHeaderFilter> HeaderFilters { get; } = new();

    /// <summary>Quick lookup: header name -> filter item.</summary>
    private readonly Dictionary<string, MessageHeaderFilter> _headerLookup = new();

    partial void OnSelectedEntryChanged(MessageLogEntry? value)
    {
        DetailText = value?.Detail ?? string.Empty;
    }

    partial void OnShowSystemChanged(bool value) => RebuildFiltered();
    partial void OnShowSendChanged(bool value) => RebuildFiltered();
    partial void OnShowReceiveChanged(bool value) => RebuildFiltered();
    partial void OnShowErrorChanged(bool value) => RebuildFiltered();
    partial void OnFilterTextChanged(string value) => RebuildFiltered();
    partial void OnHeaderFilterEnabledChanged(bool value) => RebuildFiltered();

    public void AddEntry(MessageLogEntry entry)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            AddEntryInternal(entry);
        }
        else
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => AddEntryInternal(entry));
        }
    }

    private void AddEntryInternal(MessageLogEntry entry)
    {
        Entries.Insert(0, entry);
        TotalCount = Entries.Count;

        // Track new message header
        if (!string.IsNullOrEmpty(entry.Content))
        {
            var header = ExtractHeader(entry.Content);
            if (!string.IsNullOrEmpty(header) && !_headerLookup.ContainsKey(header))
            {
                var filter = new MessageHeaderFilter(header);
                filter.IsEnabled = true;
                // Use a named handler so Clear() can unsubscribe — otherwise the closure
                // keeps the filter alive and re-adding a header risks duplicate subscriptions.
                filter.PropertyChanged += OnHeaderFilterChanged;
                _headerLookup[header] = filter;
                HeaderFilters.Add(filter);
            }
        }

        // Trim old entries
        while (Entries.Count > MaxEntries)
            Entries.RemoveAt(Entries.Count - 1);

        // Insert into filtered list if matches filter
        if (PassesFilter(entry))
        {
            FilteredEntries.Insert(0, entry);
            FilteredCount = FilteredEntries.Count;
        }
    }

    private void OnHeaderFilterChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (HeaderFilterEnabled) RebuildFiltered();
    }

    /// <summary>
    /// Extract message header from content like "S6F11 W" -> "S6F11"
    /// </summary>
    private static string ExtractHeader(string content)
    {
        // Content format: "S1F1", "S1F1 W", "S6F11W", etc.
        var span = content.AsSpan();
        int end = 0;
        for (int i = 0; i < span.Length; i++)
        {
            if (span[i] == ' ' || span[i] == 'W')
            {
                end = i;
                break;
            }
            end = i + 1;
        }
        return end > 0 ? content[..end] : content;
    }

    /// <summary>
    /// Rebuild filtered list from scratch. Called when filter changes.
    /// </summary>
    private void RebuildFiltered()
    {
        FilteredEntries.Clear();

        foreach (var entry in Entries)
        {
            if (PassesFilter(entry))
                FilteredEntries.Add(entry);
        }

        FilteredCount = FilteredEntries.Count;
    }

    private bool PassesFilter(MessageLogEntry entry)
    {
        if (!PassesCategoryFilter(entry)) return false;
        if (HeaderFilterEnabled && !PassesHeaderFilter(entry)) return false;
        if (!string.IsNullOrWhiteSpace(FilterText))
            return PassesTextFilter(entry, FilterText.Trim());
        return true;
    }

    private bool PassesCategoryFilter(MessageLogEntry entry) => entry.Direction switch
    {
        "SYS" => ShowSystem,
        ">>" => ShowSend,
        "<<" => ShowReceive,
        "ERR" => ShowError,
        _ => true,
    };

    private bool PassesHeaderFilter(MessageLogEntry entry)
    {
        if (string.IsNullOrEmpty(entry.Content)) return true;
        var header = ExtractHeader(entry.Content);
        if (_headerLookup.TryGetValue(header, out var filter))
            return filter.IsEnabled;
        return true; // Unknown headers pass through
    }

    private static bool PassesTextFilter(MessageLogEntry entry, string filter)
    {
        return entry.Content.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               entry.Protocol.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               entry.MessageId.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private void Clear()
    {
        // Detach handlers before dropping references — otherwise closures from the old
        // pre-named-handler code path could pile up across Clear/AddEntry cycles.
        foreach (var f in HeaderFilters)
            f.PropertyChanged -= OnHeaderFilterChanged;

        Entries.Clear();
        FilteredEntries.Clear();
        HeaderFilters.Clear();
        _headerLookup.Clear();
        DetailText = string.Empty;
        TotalCount = 0;
        FilteredCount = 0;
    }

    [RelayCommand]
    private void ToggleAllFilters()
    {
        var allOn = ShowSystem && ShowSend && ShowReceive && ShowError;
        ShowSystem = !allOn;
        ShowSend = !allOn;
        ShowReceive = !allOn;
        ShowError = !allOn;
    }

    [RelayCommand]
    private void ToggleAllHeaders()
    {
        var allEnabled = HeaderFilters.All(h => h.IsEnabled);
        foreach (var h in HeaderFilters)
            h.IsEnabled = !allEnabled;
    }

    [RelayCommand]
    private void SelectOnlyHeader(MessageHeaderFilter? target)
    {
        if (target == null) return;
        foreach (var h in HeaderFilters)
            h.IsEnabled = (h == target);
    }
}

public partial class MessageHeaderFilter : ObservableObject
{
    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private string _header = string.Empty;

    public MessageHeaderFilter(string header)
    {
        Header = header;
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
    public string DirectionLabel => Direction switch
    {
        "<<" => "Recv",
        ">>" => "Send",
        "SYS" => "SYS",
        "ERR" => "ERR",
        _ => Direction,
    };
    public string MessageId { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}
