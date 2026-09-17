using Microsoft.Extensions.Logging;

namespace Aiko.Server.Diagnostics;

/// <summary>
/// Writes the daemon's log to a file, for a daemon started without a console to write to.
/// </summary>
/// <remarks>
/// <para>
/// The CLI starts a background daemon with no console of its own and points it here through
/// <c>AIKO_LOG_FILE</c>. Without this, a daemon that leaves the terminal behind also leaves every word it
/// said behind, and "it stopped by itself" has no answer anywhere.
/// </para>
/// <para>
/// Every line is appended with its own open/write/close rather than held in a buffered writer. That is
/// slower, and it is the point: a daemon that is killed - which is exactly the case this log exists for -
/// loses nothing that was already logged, because nothing was buffered.
/// </para>
/// </remarks>
internal sealed class FileLoggerProvider(string path) : ILoggerProvider
{
    /// <summary>Size at which the file is rotated to <c>.1</c>, so a long-running daemon cannot fill a disk.</summary>
    private const long MaxBytes = 8L * 1024 * 1024;

    private readonly object _sync = new();

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    /// <inheritdoc />
    public void Dispose()
    {
    }

    /// <summary>Appends one line, rotating the file first when it has grown past its cap.</summary>
    private void Write(string line)
    {
        lock (_sync)
        {
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var file = new FileInfo(path);
                if (file.Exists && file.Length > MaxBytes)
                {
                    File.Move(path, $"{path}.1", overwrite: true);
                }

                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch (Exception)
            {
                // A log that cannot be written must not take the daemon down with it: losing the record of
                // what happened is bad, and losing the process because of it is worse.
            }
        }
    }

    /// <summary>One category's logger, in the shape the console logger prints: time, level, category, text.</summary>
    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        /// <inheritdoc />
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level(logLevel)}] {category}: {formatter(state, exception)}";
            if (exception is not null)
            {
                line = $"{line}{Environment.NewLine}{exception}";
            }

            provider.Write(line);
        }

        private static string Level(LogLevel logLevel) => logLevel switch
        {
            LogLevel.Trace => "trce",
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            LogLevel.Critical => "crit",
            _ => "none"
        };
    }
}
