namespace Aiko.Pwa.Services;

/// <summary>
/// The environment variables of one configured agent instance as the settings screen edits them. Values
/// arrive masked, so an untouched row keeps the sentinel and the daemon keeps the stored secret.
/// </summary>
public sealed class AgentEnvironmentEditor
{
    private readonly List<AgentEnvironmentEntry> entries = [];

    public IReadOnlyList<AgentEnvironmentEntry> Entries => entries;

    public static AgentEnvironmentEditor Load(IReadOnlyDictionary<string, string>? environment)
    {
        var editor = new AgentEnvironmentEditor();
        if (environment is null)
        {
            return editor;
        }

        foreach (var pair in environment)
        {
            editor.entries.Add(new AgentEnvironmentEntry { Key = pair.Key, Value = pair.Value });
        }

        return editor;
    }

    public AgentEnvironmentEntry Add()
    {
        var entry = new AgentEnvironmentEntry();
        entries.Add(entry);
        return entry;
    }

    public void Remove(AgentEnvironmentEntry entry) => entries.Remove(entry);

    /// <summary>The variables to save: blank keys are dropped, and no rows at all means no section.</summary>
    public IReadOnlyDictionary<string, string>? ToEnvironment()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (!string.IsNullOrWhiteSpace(entry.Key))
            {
                values[entry.Key.Trim()] = entry.Value;
            }
        }

        return values.Count == 0 ? null : values;
    }
}

/// <summary>One key/value row of <see cref="AgentEnvironmentEditor"/>.</summary>
public sealed class AgentEnvironmentEntry
{
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}
