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
    /// Root of the models fetched on demand, next to the database.
    /// </summary>
    /// <remarks>
    /// Installation data rather than project data: a model serves every project of this machine, and it is not
    /// part of the installation either - it arrives when someone asks for it, and deleting this directory is a
    /// legitimate way to get the space back.
    /// </remarks>
    public string ModelsRoot => Path.Combine(DataDirectory, "models");

    /// <summary>
    /// Directory holding one model's files.
    /// </summary>
    /// <param name="modelId">Model identifier, also its directory name.</param>
    public string ModelDirectory(string modelId) => Path.Combine(ModelsRoot, modelId);

    /// <summary>
    /// Root of the idempotency records: one file per key a mutating tool call can be answered from
    /// instead of being executed twice.
    /// </summary>
    /// <remarks>
    /// Daemon data rather than project data: the record is a property of the tool call - which caller
    /// repeated which call - so it does not belong in a project's own <c>.aiko</c>.
    /// </remarks>
    public string IdempotencyRoot => Path.Combine(DataDirectory, "idempotency");

    /// <summary>Directory the daemon's own data lives in.</summary>
    private string DataDirectory =>
        Path.GetDirectoryName(DatabasePath)
            ?? throw new InvalidOperationException("The database path must include a directory.");

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
