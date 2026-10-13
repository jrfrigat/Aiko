namespace Aiko.Infrastructure.Specs;

/// <summary>
/// What moved between two fingerprints of the same tree: the entries added, removed or changed, or
/// <c>nothing</c> when the two are the same.
/// </summary>
/// <remarks>
/// A fingerprint comparison that fails by printing two long blocks leaves the reader to find the difference,
/// and a flake nobody can chase is what two full-solution failures of <c>LocalFeedInstallationSpecs</c> left
/// behind (TASK-341). This is the same comparison, said in one line: <c>nothing</c> means the fingerprints are
/// equal, which is exactly what the assertion used to check.
/// </remarks>
internal static class FingerprintDiff
{
    /// <summary>An entry that was not there before.</summary>
    public const string Nothing = "nothing";

    public static string Between(string before, string after)
    {
        var earlier = Entries(before);
        var later = Entries(after);
        if (earlier.Count == later.Count
            && earlier.All(entry => later.TryGetValue(entry.Key, out var hash) && hash == entry.Value))
        {
            return Nothing;
        }

        var changes = new List<string>();
        foreach (var (key, hash) in later)
        {
            if (!earlier.TryGetValue(key, out var was))
            {
                changes.Add($"added {key}");
            }
            else if (was != hash)
            {
                changes.Add($"changed {key}");
            }
        }

        changes.AddRange(earlier.Keys.Where(key => !later.ContainsKey(key)).Select(key => $"removed {key}"));
        return string.Join("; ", changes);
    }

    private static Dictionary<string, string> Entries(string fingerprint)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in fingerprint.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf('|');
            if (separator > 0)
            {
                entries[line[..separator]] = line[(separator + 1)..];
            }
        }

        return entries;
    }
}
