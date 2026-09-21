namespace Aiko.Application.Installation;

/// <summary>
/// How a release tag, its version and its asset are named.
/// </summary>
/// <remarks>
/// The rules come from <c>scripts/install.ps1</c>, which is today the only installation path: the tag is
/// resolved through the <c>releases/latest</c> redirect, the version is the tag without its leading
/// <c>v</c> and without build metadata, and the asset is <c>aiko-&lt;version&gt;-win-x64.zip</c>. They live
/// here rather than in the source that downloads them because the name is also what a person types into a
/// URL, and one definition is what keeps the two from drifting.
/// </remarks>
public static class ReleaseNaming
{
    /// <summary>File a release publishes with the checksums of its assets.</summary>
    public const string ChecksumsAssetName = "SHA256SUMS";

    private const string AssetSuffix = "-win-x64.zip";

    /// <summary>
    /// The version a tag names: the tag without its leading <c>v</c> and without <c>+build</c> metadata.
    /// </summary>
    /// <param name="tag">Release tag, for example <c>v0.9.2</c>.</param>
    /// <exception cref="FormatException">The tag does not name a version.</exception>
    public static string VersionFromTag(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        var trimmed = tag.Trim();
        var withoutPrefix = trimmed.StartsWith('v') || trimmed.StartsWith('V') ? trimmed[1..] : trimmed;
        var version = withoutPrefix.Split('+')[0];

        // The version ends up in a URL and in a file name, so a tag that carries a separator or a space is
        // refused here rather than turned into a request for something unexpected.
        if (version.Length == 0 ||
            version.Any(char.IsWhiteSpace) ||
            version.Contains('/') ||
            version.Contains('\\'))
        {
            throw new FormatException($"'{tag}' is not a release tag: it does not name a version.");
        }

        return version;
    }

    /// <summary>The asset a tag publishes, for example <c>aiko-0.9.2-win-x64.zip</c>.</summary>
    /// <param name="tag">Release tag, for example <c>v0.9.2</c>.</param>
    public static string AssetNameForTag(string tag) => $"aiko-{VersionFromTag(tag)}{AssetSuffix}";
}
