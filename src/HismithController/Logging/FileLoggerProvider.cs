using System.IO;
using Microsoft.Extensions.Logging;

namespace HismithController.Logging;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private const int RetentionDays = 7;

    private readonly string _logDirectory;
    private readonly object _writeLock = new();
    private StreamWriter? _writer;
    private DateOnly _currentDate;
    private bool _disposed;

    public FileLoggerProvider(string logDirectory)
    {
        _logDirectory = logDirectory;
        Directory.CreateDirectory(_logDirectory);
        PruneOldLogs();
    }

    public static string DefaultLogDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HismithController",
            "logs");

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this);

    internal void Write(string category, LogLevel level, string message, Exception? exception)
    {
        if (_disposed)
            return;

        var now = DateTimeOffset.Now;
        var line = FormatLine(now, level, category, message, exception);

        lock (_writeLock)
        {
            if (_disposed)
                return;

            EnsureWriter(DateOnly.FromDateTime(now.LocalDateTime));
            try
            {
                _writer!.WriteLine(line);
            }
            catch
            {
                // Last resort: never let logging crash the app.
            }
        }
    }

    private void EnsureWriter(DateOnly date)
    {
        if (_writer is not null && date == _currentDate)
            return;

        _writer?.Dispose();
        _currentDate = date;
        var path = Path.Combine(_logDirectory, $"app-{date:yyyy-MM-dd}.txt");
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream) { AutoFlush = true };
    }

    private static string FormatLine(DateTimeOffset timestamp, LogLevel level,
        string category, string message, Exception? exception)
    {
        var levelTag = level switch
        {
            LogLevel.Trace => "TRCE",
            LogLevel.Debug => "DBUG",
            LogLevel.Information => "INFO",
            LogLevel.Warning => "WARN",
            LogLevel.Error => "FAIL",
            LogLevel.Critical => "CRIT",
            _ => "NONE"
        };

        var line = $"{timestamp:yyyy-MM-ddTHH:mm:ss.fffzzz} [{levelTag}] {category}: {message}";
        return exception is null ? line : $"{line}{Environment.NewLine}{exception}";
    }

    private void PruneOldLogs()
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
            foreach (var file in Directory.EnumerateFiles(_logDirectory, "app-*.txt"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                    File.Delete(file);
            }
        }
        catch
        {
            // Pruning failure must never block startup.
        }
    }

    public void Dispose()
    {
        lock (_writeLock)
        {
            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly string _category;
        private readonly FileLoggerProvider _provider;

        public FileLogger(string category, FileLoggerProvider provider)
        {
            _category = category;
            _provider = provider;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;
            _provider.Write(_category, logLevel, formatter(state, exception), exception);
        }
    }
}
