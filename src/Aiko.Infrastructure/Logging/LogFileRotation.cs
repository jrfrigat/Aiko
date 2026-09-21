namespace Aiko.Infrastructure.Logging;

/// <summary>
/// Appends to a log file and rotates it into numbered siblings when it reaches the configured size.
/// </summary>
/// <remarks>
/// <para>
/// Rotation that dropped a file without saying so would be indistinguishable from a bug that lost it, so
/// the file taking the place of the old one states what went: that a rotation happened, and which file the
/// bound removed. The note is written first, before the record the daemon is about to add, because a
/// rotation nobody can see is the failure mode this class exists to avoid.
/// </para>
/// <para>
/// Every write opens, appends and closes rather than holding a buffered writer. That is slower, and it is
/// the point: a daemon that is killed - which is the case the log exists for - loses nothing that was
/// already logged, because nothing was buffered.
/// </para>
/// </remarks>
public sealed class LogFileRotation
{
    private readonly object _sync = new();
    private readonly LogRetentionSettings _settings;

    /// <summary>
    /// Creates the rotation for one log file.
    /// </summary>
    /// <param name="path">File currently written; rotated files are this path with a generation suffix.</param>
    /// <param name="settings">Bounds the rotation obeys.</param>
    public LogFileRotation(string path, LogRetentionSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        settings.Validate();
        Path = path;
        _settings = settings;
    }

    /// <summary>Path of the file currently being written.</summary>
    public string Path { get; }

    /// <summary>Path of a rotated generation: 1 is the file closed most recently.</summary>
    /// <param name="generation">Generation number, starting at 1.</param>
    public string RotatedPath(int generation) => $"{Path}.{generation}";

    /// <summary>
    /// Appends one line, rotating first when the current file has reached the configured size.
    /// </summary>
    /// <param name="line">Line to append without its terminator.</param>
    public void Append(string line)
    {
        lock (_sync)
        {
            try
            {
                var directory = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                RotateIfFull();
                File.AppendAllText(Path, line + Environment.NewLine);
            }
            catch (Exception)
            {
                // A log that cannot be written must not take the daemon down with it: losing the record of
                // what happened is bad, and losing the process because of it is worse.
            }
        }
    }

    /// <summary>
    /// Rotates when the current file is full, dropping the oldest kept generation and naming it.
    /// </summary>
    private void RotateIfFull()
    {
        var file = new FileInfo(Path);
        if (!file.Exists || file.Length < _settings.MaxFileBytes)
        {
            return;
        }

        // Generation MaxFiles - 1 is the oldest one the bound keeps; the next rotation pushes it out.
        var oldest = _settings.MaxFiles - 1;
        string? dropped = null;
        if (oldest >= 1 && File.Exists(RotatedPath(oldest)))
        {
            var oldestFile = new FileInfo(RotatedPath(oldest));
            dropped =
                $"{oldestFile.Name} ({oldestFile.Length} bytes, written {oldestFile.LastWriteTime:yyyy-MM-dd HH:mm})";
            File.Delete(RotatedPath(oldest));
        }

        for (var generation = oldest - 1; generation >= 1; generation--)
        {
            var older = RotatedPath(generation);
            if (File.Exists(older))
            {
                File.Move(older, RotatedPath(generation + 1), overwrite: true);
            }
        }

        File.Move(Path, RotatedPath(1), overwrite: true);

        var note = dropped is null
            ? $"log rotated at {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}; keeping {_settings.MaxFiles} files, none dropped"
            : $"log rotated at {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}; keeping {_settings.MaxFiles} files, dropped {dropped}";
        File.AppendAllText(Path, note + Environment.NewLine);
    }
}
