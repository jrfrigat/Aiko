namespace Aiko.Application.Installation;

/// <summary>
/// The <c>SHA256SUMS</c> file a release publishes: one line per asset, <c>&lt;sha256&gt;  &lt;name&gt;</c>.
/// </summary>
/// <remarks>
/// A malformed line is refused rather than skipped. Skipping would quietly drop the entry of the asset the
/// caller is about to verify, and "no checksum for this file" reads exactly like "the release publishes no
/// checksums" - two different facts, and the second one is a decision only a person may take.
/// </remarks>
public static class ChecksumFile
{
    /// <summary>Reads the file into asset name to lowercase SHA-256.</summary>
    /// <param name="content">The whole <c>SHA256SUMS</c> file.</param>
    /// <exception cref="FormatException">A line is not <c>&lt;sha256&gt;  &lt;name&gt;</c>.</exception>
    public static IReadOnlyDictionary<string, string> Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var checksums = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            var separator = trimmed.IndexOfAny([' ', '\t']);
            if (separator <= 0)
            {
                throw new FormatException($"The checksum file has a line without a file name: '{trimmed}'.");
            }

            var hash = trimmed[..separator];
            // `sha256sum` writes a binary marker before the name; coreutils and the GitHub release tooling
            // both produce it, so it is stripped rather than treated as part of the name.
            var name = trimmed[(separator + 1)..].TrimStart('*').Trim();
            if (!IsSha256(hash) || name.Length == 0)
            {
                throw new FormatException($"The checksum file has a line that is not '<sha256>  <name>': '{trimmed}'.");
            }

            checksums[name] = hash.ToLowerInvariant();
        }

        return checksums;
    }

    private static bool IsSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
}
