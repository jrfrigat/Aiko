namespace Aiko.Infrastructure.Storage;

/// <summary>
/// Paths of the Aiko daemon's service data: the SQLite database and the settings.json file.
/// </summary>
public sealed record AikoDataPaths(string DatabasePath)
{
    /// <summary>
    /// Full path to settings.json next to the database (daemon endpoint settings).
    /// </summary>
    public string SettingsPath =>
        Path.Combine(
            Path.GetDirectoryName(DatabasePath)
                ?? throw new InvalidOperationException("The database path must include a directory."),
            "settings.json");

    /// <summary>
    /// Full path to app-settings.json next to the database (global application settings).
    /// </summary>
    public string ApplicationSettingsPath =>
        Path.Combine(
            Path.GetDirectoryName(DatabasePath)
                ?? throw new InvalidOperationException("The database path must include a directory."),
            "app-settings.json");

    /// <summary>
    /// Full path to the access token used to authenticate local REST/MCP clients.
    /// </summary>
    public string AccessTokenPath =>
        Path.Combine(
            Path.GetDirectoryName(DatabasePath)
                ?? throw new InvalidOperationException("The database path must include a directory."),
            "access-token");

    /// <summary>
    /// Reads the database path from the AIKO_DATABASE environment variable
    /// or falls back to %LocalAppData%/Aiko/aiko.db.
    /// </summary>
    public static AikoDataPaths FromEnvironment()
    {
        var configuredPath = Environment.GetEnvironmentVariable("AIKO_DATABASE");
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return new AikoDataPaths(Path.GetFullPath(configuredPath));
        }

        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new AikoDataPaths(Path.Combine(localData, "Aiko", "aiko.db"));
    }
}
