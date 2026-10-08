using Aiko.Application.Contracts;

namespace Aiko.Infrastructure.Models;

/// <summary>
/// Fetches a model's files from the pinned address the registry records for them.
/// </summary>
/// <remarks>
/// This is the path that works when the release channel does not carry the model - or cannot be reached at
/// all. It is not a fallback in the sense of "try anything": the address is one the registry states, and the
/// file is verified against the registry's checksum afterwards, so a host that serves something else is
/// refused rather than installed.
/// </remarks>
public sealed class ExternalModelDownloader : IEmbeddingModelDownloader
{
    private readonly HttpClient http;

    /// <summary>Creates the downloader.</summary>
    /// <param name="httpClient">Client used for the fetch; the caller owns it.</param>
    public ExternalModelDownloader(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        http = httpClient;
    }

    /// <inheritdoc />
    public EmbeddingModelSource Source => EmbeddingModelSource.External;

    /// <inheritdoc />
    public async ValueTask DownloadAsync(
        EmbeddingModel model,
        EmbeddingModelFile file,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(file);

        using var response = await http.GetAsync(
            file.ExternalUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous);
        await source.CopyToAsync(destination, cancellationToken);
    }
}
