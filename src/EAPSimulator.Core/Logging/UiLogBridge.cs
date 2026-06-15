using Microsoft.Extensions.Logging;

namespace EAPSimulator.Core.Logging;

/// <summary>
/// Bridge between ILogger (Serilog) and UI MessageLog panel.
/// Implements ILoggerProvider and ILogger to forward core layer logs to UI.
/// </summary>
public class UiLogBridge : ILoggerProvider
{
    private readonly Action<LogEntry> _logAction;
    private readonly List<string> _categoryFilters;
    private bool _disposed;

    /// <summary>
    /// Create a new UiLogBridge.
    /// </summary>
    /// <param name="logAction">Action to invoke when a log entry is created (e.g., add to UI).</param>
    /// /// <param name="categoryFilters">Category prefixes to include (empty = all).</param>
    public UiLogBridge(Action<LogEntry> logAction, params string[] categoryFilters)
    {
        _logAction = logAction;
        _categoryFilters = categoryFilters.Length > 0
            ? [.. categoryFilters]
            : [];
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new UiLogger(categoryName, _logAction, _categoryFilters);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    private class UiLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly Action<LogEntry> _logAction;
        private readonly List<string> _categoryFilters;

        public UiLogger(string categoryName, Action<LogEntry> logAction, List<string> categoryFilters)
        {
            _categoryName = categoryName;
            _logAction = logAction;
            _categoryFilters = categoryFilters;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            // Apply category filter
            if (_categoryFilters.Count > 0 &&
                !_categoryFilters.Any(f => _categoryName.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
                return;

            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = logLevel,
                Category = _categoryName,
                Message = formatter(state, exception),
                Exception = exception,
            };

            try
            {
                _logAction(entry);
            }
            catch
            {
                // Swallow to avoid recursive logging failures
            }
        }
    }
}

/// <summary>
/// A log entry from the core layer.
/// </summary>
public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public LogLevel Level { get; set; }
    public string Category { get; set; } = "";
    public string Message { get; set; } = "";
    public Exception? Exception { get; set; }

    /// <summary>
    /// Convert to a UI-friendly direction string.
    /// </summary>
    public string ToDirection()
    {
        return Level switch
        {
            LogLevel.Error or LogLevel.Critical => "ERR",
            LogLevel.Warning => "SYS",
            _ => "SYS",
        };
    }

    /// <summary>
    /// Format for display.
    /// </summary>
    public string ToDisplayString()
    {
        var level = Level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "FTL",
            _ => "???",
        };
        var msg = $"[{level}] {Message}";
        if (Exception != null)
            msg += $" | {Exception.GetType().Name}: {Exception.Message}";
        return msg;
    }
}

/// <summary>
/// Extension methods for registering UiLogBridge.
/// </summary>
public static class UiLogBridgeExtensions
{
    /// <summary>
    /// Create a UiLogBridge instance.
    /// </summary>
    public static UiLogBridge CreateUiBridge(Action<LogEntry> logAction,
        params string[] categoryFilters)
    {
        return new UiLogBridge(logAction, categoryFilters);
    }
}
