using Aiko.Application.Contracts;
using Aiko.Infrastructure.Installation;

namespace Aiko.Infrastructure.Release;

/// <summary>
/// Asks GitHub for the latest release of a repository by reading the redirect of
/// <c>https://github.com/&lt;owner&gt;/&lt;repo&gt;/releases/latest</c>.
/// </summary>
/// <remarks>
/// The same address and the same reasoning as the installer (<c>scripts/install.ps1:67-83</c>): the
/// unauthenticated API allows 60 requests per hour per public address, often shared by an office, a VPN or an
/// ISP, so a machine can be refused a lookup through no fault of its own, while the redirect endpoint is not
/// subject to that limit. The address is parsed by
/// <see cref="GitHubReleaseSource.ParseTagFromLocation"/> rather than by a second parser here, so exactly one
/// place decides which host may be followed.
/// <para>
/// This is the daemon's first outbound request, so it never throws: no repository, no network, a 404 and a
/// timeout are all "not known", each with its reason for a screen to show.
/// </para>
/// </remarks>
public sealed class GitHubReleaseProbe(HttpClient http)
{
    /// <summary>
    /// How long the probe may take. Short on purpose: the answer decorates a screen, and a probe hanging on a
    /// black-holed connection must not hold the request that draws it.
    /// </summary>
    internal static TimeSpan Timeout { get; } = TimeSpan.FromSeconds(10);

    /// <summary>The host releases are read from.</summary>
    internal const string ReleasesHost = "github.com";

    /// <summary>
    /// Reads the latest release of the configured repository.
    /// </summary>
    /// <param name="settings">The project's effective release section.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask<LatestRelease> ReadAsync(
        ReleaseSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.Slug is not { } slug)
        {
            return LatestRelease.Unknown("the project states no release repository");
        }

        // The slug comes from a settings file a person edits, and it is pasted into an address. A value that is
        // not exactly `owner/repository` - a name with a `?`, a `#` or a slash of its own - would address
        // something other than the repository it claims, so it is refused here rather than escaped and sent.
        if (!IsRepositorySlug(slug))
        {
            return LatestRelease.Unknown($"'{slug}' is not an owner/repository address");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Head,
                new Uri($"https://{ReleasesHost}/{slug}/releases/latest"));
            using var response = await http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);

            // A client that follows redirects leaves the header empty and reports the address it ended at;
            // one that does not leaves the header. Both are read, so the handler decides which happens.
            var location = response.Headers.Location?.ToString()
                ?? response.RequestMessage?.RequestUri?.ToString();

            if (GitHubReleaseSource.ParseTagFromLocation(location) is { } tag)
            {
                return LatestRelease.Found(
                    tag,
                    $"https://{ReleasesHost}/{slug}/releases/tag/{Uri.EscapeDataString(tag)}");
            }

            return LatestRelease.Unknown(
                response.IsSuccessStatusCode
                    ? $"{ReleasesHost} did not answer with a release address"
                    : $"{ReleasesHost} answered {(int)response.StatusCode}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return LatestRelease.Unknown($"the probe did not answer within {Timeout.TotalSeconds:0} seconds");
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or UriFormatException)
        {
            // Kept beside the slug check as a backstop: whatever the address turns out to be, a probe that
            // cannot be performed is an answer, and the screen never gets a 500 for asking about a release.
            return LatestRelease.Unknown(exception.Message);
        }
    }

    /// <summary>
    /// Whether a value is a plain <c>owner/repository</c> address, as GitHub spells one.
    /// </summary>
    /// <remarks>
    /// GitHub allows letters, digits, <c>-</c>, <c>_</c> and <c>.</c> in a repository name and a stricter set
    /// in an owner. Anything else is not tightened here but refused, because the alternative is escaping a
    /// value that was never an address.
    /// </remarks>
    private static bool IsRepositorySlug(string slug)
    {
        var parts = slug.Split('/');
        return parts.Length == 2 &&
            parts.All(part => part.Length > 0 && part.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'));
    }
}
