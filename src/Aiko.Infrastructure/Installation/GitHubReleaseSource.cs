using Aiko.Application.Installation;

namespace Aiko.Infrastructure.Installation;

/// <summary>
/// Releases published on GitHub: the newest release is found through the <c>releases/latest</c> redirect,
/// and assets are read from the release's own download URLs.
/// </summary>
/// <remarks>
/// The redirect is used instead of the API on purpose, and that behaviour is carried over from
/// <c>scripts/install.ps1</c>: the unauthenticated API allows 60 requests per hour per public address,
/// often shared by an office, a VPN or an ISP, so a machine can be refused a release lookup through no
/// fault of its own. The redirect endpoint is not subject to that limit.
/// </remarks>
public sealed class GitHubReleaseSource : IReleaseSource
{
    private const string ReleasesHost = "github.com";
    private const string LatestPath = "/releases/latest";
    private const string TagMarker = "/releases/tag/";
    private const string DownloadMarker = "/releases/download/";

    private readonly HttpClient http;
    private readonly string owner;
    private readonly string repository;

    /// <summary>Creates a source for one repository.</summary>
    /// <param name="httpClient">Client used for every request; the caller owns it.</param>
    /// <param name="owner">Repository owner, for example <c>jrfrigat</c>.</param>
    /// <param name="repository">Repository name, for example <c>Aiko</c>.</param>
    public GitHubReleaseSource(HttpClient httpClient, string owner, string repository)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);

        this.http = httpClient;
        this.owner = owner;
        this.repository = repository;
    }

    /// <summary>
    /// The release tag a <c>releases/latest</c> redirect names, or null when the location does not.
    /// </summary>
    /// <remarks>
    /// Static and separate because it is the only part of this source that can be checked without a network:
    /// what matters is that a missing header, a foreign host or a truncated address is refused here rather
    /// than turned into an empty tag that later reads as "the release has no name".
    /// </remarks>
    /// <param name="location">Value of the <c>Location</c> header, or the address a redirect ended at.</param>
    public static string? ParseTagFromLocation(string? location)
    {
        if (string.IsNullOrWhiteSpace(location) ||
            !Uri.TryCreate(location, UriKind.Absolute, out var uri))
        {
            return null;
        }

        // Only the releases host is trusted: an address that resolves somewhere else is not the redirect
        // this method claims to understand, and following it would install whatever that host serves.
        if (!string.Equals(uri.Host, ReleasesHost, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var marker = uri.AbsolutePath.IndexOf(TagMarker, StringComparison.Ordinal);
        if (marker < 0)
        {
            return null;
        }

        var tag = uri.AbsolutePath[(marker + TagMarker.Length)..];
        var end = tag.IndexOfAny(['/', '?', '#']);
        if (end >= 0)
        {
            tag = tag[..end];
        }

        tag = Uri.UnescapeDataString(tag);
        return tag.Length == 0 ? null : tag;
    }

    /// <inheritdoc />
    public async ValueTask<string> ResolveTagAsync(string? requestedTag, CancellationToken cancellationToken)
    {
        if (!IsLatest(requestedTag))
        {
            return requestedTag!.Trim();
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Head,
            new Uri($"https://{ReleasesHost}/{owner}/{repository}{LatestPath}"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        // A client that follows redirects leaves the header empty and reports the address it ended at; one
        // that does not leaves the header. Both are read, so the caller's handler decides which happens.
        var location = response.Headers.Location?.ToString() ?? response.RequestMessage?.RequestUri?.ToString();

        return ParseTagFromLocation(location)
            ?? throw new InvalidOperationException(
                $"{ReleasesHost} did not redirect to a release tag. Location: {location ?? "(none)"}");
    }

    /// <inheritdoc />
    public async ValueTask DownloadAssetAsync(
        string tag,
        string assetName,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        await using var source = await http.GetStreamAsync(AssetUrl(tag, assetName), cancellationToken);
        await using var destination = File.Create(destinationPath);
        await source.CopyToAsync(destination, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyDictionary<string, string>> ReadChecksumsAsync(
        string tag,
        CancellationToken cancellationToken)
    {
        var content = await http.GetStringAsync(AssetUrl(tag, ReleaseNaming.ChecksumsAssetName), cancellationToken);
        return ChecksumFile.Parse(content);
    }

    private static bool IsLatest(string? tag) =>
        string.IsNullOrWhiteSpace(tag) || string.Equals(tag.Trim(), "latest", StringComparison.OrdinalIgnoreCase);

    private string AssetUrl(string tag, string assetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetName);

        // Both are escaped as single path segments: a tag or an asset name carrying a separator would
        // otherwise address a different release than the one that was resolved.
        return $"https://{ReleasesHost}/{owner}/{repository}{DownloadMarker}" +
            $"{Uri.EscapeDataString(tag.Trim())}/{Uri.EscapeDataString(assetName)}";
    }
}
