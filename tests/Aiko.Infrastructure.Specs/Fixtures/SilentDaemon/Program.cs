// A daemon that is not one: it starts, says it is alive by writing its own process id where the spec asked for
// it, and then does nothing at all - it opens no port and answers no /health. `ServeTimeoutSpecs` lays this
// assembly out next to the CLI as `Aiko.Server.dll`, so that the timeout path of `aiko serve -d` has a real
// process to give up on and end, and so that "it is gone" is a fact about this process rather than about a
// server that had already stopped by itself (TASK-343).
var pidFile = Environment.GetEnvironmentVariable("AIKO_SILENT_DAEMON_PID_FILE");
if (!string.IsNullOrWhiteSpace(pidFile))
{
    File.WriteAllText(
        pidFile,
        Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
}

// Waiting without an end, and only waiting: this process never ends until something ends it.
await Task.Delay(Timeout.Infinite);
