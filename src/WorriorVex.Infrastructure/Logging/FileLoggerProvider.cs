using System.Globalization;
using Microsoft.Extensions.Logging;

namespace WorriorVex.Infrastructure.Logging;

/// <summary>
/// Appends log lines to one file per day in the logs folder. A desktop app has no console,
/// so this is where startup, migration and unexpected errors can be found afterwards.
/// </summary>
public sealed class FileLoggerProvider(string directory, LogLevel minimumLevel = LogLevel.Information) : ILoggerProvider
{
    private readonly Lock _gate = new();

    private LogLevel MinimumLevel { get; } = minimumLevel;

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Write(string line)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, $"worriorvex-{DateTime.UtcNow:yyyyMMdd}.log");
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch (IOException)
        {
            // Logging must never take the application down.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= provider.MinimumLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
            var line = $"{timestamp} [{logLevel}] {category}: {formatter(state, exception)}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            provider.Write(line);
        }
    }
}
