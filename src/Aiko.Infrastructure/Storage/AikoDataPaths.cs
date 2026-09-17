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
    /// Root of the project templates: one directory per template, next to the database. Templates are
    /// installation-level material, so they live with the daemon's own data rather than in any project.
    /// </summary>
    public string TemplatesRoot =>
        Path.Combine(
            Path.GetDirectoryName(DatabasePath)
                ?? throw new InvalidOperationException("The database path must include a directory."),
            "templates");

    /// <summary>
    /// Directory of one template.
    /// </summary>
    /// <param name="templateId">Template identifier, also its directory name.</param>
    public string TemplateDirectory(string templateId) => Path.Combine(TemplatesRoot, templateId);

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
