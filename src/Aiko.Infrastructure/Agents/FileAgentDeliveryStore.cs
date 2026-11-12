using System.Text.Json;
using Aiko.Application.Agents;
using Aiko.Application.Contracts;
using Aiko.Domain.Execution;
using Aiko.Infrastructure.Projects;
using Aiko.Infrastructure.Storage;
using Aiko.Infrastructure.Threading;

namespace Aiko.Infrastructure.Agents;

/// <summary>
/// File-based delivery history: <c>.aiko/deliveries.json</c> as the source of truth for the direct attempts
/// of one project.
/// </summary>
public sealed class FileAgentDeliveryStore(
    IProjectCatalog projects) : IAgentDeliveryStore
{
    private const string FileName = "deliveries.json";
    private const int CurrentSchemaVersion = 1;
    private const string IdPrefix = "DLV-";

    private readonly KeyedLockStore locks = new();

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AgentDelivery>> ListAsync(
        string projectId,
        bool includeClosed,
        CancellationToken cancellationToken)
    {
        var project = await FindProjectAsync(projectId, cancellationToken);
        var document = await ReadDocumentAsync(project.RootPath, cancellationToken);
        return includeClosed
            ? document.Entries
            : [.. document.Entries.Where(delivery => delivery.IsOpen)];
    }

    /// <inheritdoc />
    public async ValueTask<AgentDelivery?> FindAsync(
        string projectId,
        string deliveryId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryId);
        var project = await FindProjectAsync(projectId, cancellationToken);
        var document = await ReadDocumentAsync(project.RootPath, cancellationToken);
        return document.Entries.FirstOrDefault(delivery =>
            StringComparer.Ordinal.Equals(delivery.Id, deliveryId));
    }

    /// <inheritdoc />
    public async ValueTask<AgentDelivery?> FindLatestForCommandAsync(
        string projectId,
        string commandId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        var project = await FindProjectAsync(projectId, cancellationToken);
        var document = await ReadDocumentAsync(project.RootPath, cancellationToken);
        return document.Entries.LastOrDefault(delivery =>
            StringComparer.Ordinal.Equals(delivery.CommandId, commandId));
    }

    /// <inheritdoc />
    public async ValueTask<AgentDelivery> CreateAsync(
        string projectId,
        string commandId,
        string agentAdapterId,
        AgentTransport transport,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentAdapterId);
        var project = await FindProjectAsync(projectId, cancellationToken);

        using (await locks.LockAsync(project.Id, cancellationToken))
        {
            var document = await ReadDocumentAsync(project.RootPath, cancellationToken);
            var delivery = new AgentDelivery(
                $"{IdPrefix}{document.NextSequence}",
                commandId.Trim(),
                agentAdapterId.Trim(),
                transport,
                AgentDeliveryState.Starting,
                null,
                null,
                null,
                DateTimeOffset.UtcNow,
                null,
                null);
            await WriteDocumentAsync(
                project.RootPath,
                document with
                {
                    NextSequence = document.NextSequence + 1,
                    Entries = [.. document.Entries, delivery]
                },
                cancellationToken);
            return delivery;
        }
    }

    /// <inheritdoc />
    public async ValueTask<AgentDelivery> UpdateAsync(
        string projectId,
        AgentDelivery delivery,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        var project = await FindProjectAsync(projectId, cancellationToken);

        using (await locks.LockAsync(project.Id, cancellationToken))
        {
            var document = await ReadDocumentAsync(project.RootPath, cancellationToken);
            if (document.Entries.All(entry =>
                    !StringComparer.Ordinal.Equals(entry.Id, delivery.Id)))
            {
                throw new FileNotFoundException(
                    $"No delivery '{delivery.Id}' in project '{project.Id}'.");
            }

            var entries = document.Entries
                .Select(entry => StringComparer.Ordinal.Equals(entry.Id, delivery.Id)
                    ? delivery
                    : entry)
                .ToArray();
            await WriteDocumentAsync(
                project.RootPath,
                document with { Entries = entries },
                cancellationToken);
            return delivery;
        }
    }

    private async ValueTask<RegisteredProject> FindProjectAsync(
        string projectId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        return await projects.FindAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Unknown Aiko project: {projectId}");
    }

    private static async ValueTask<AgentDeliveryDocument> ReadDocumentAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(AikoProjectPaths.DataRoot(projectRoot), FileName);
        if (!File.Exists(path))
        {
            return new AgentDeliveryDocument(CurrentSchemaVersion, 1, []);
        }

        await using var input = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(
                   input,
                   ProjectJsonContext.Default.AgentDeliveryDocument,
                   cancellationToken)
               ?? throw new InvalidDataException($"Invalid delivery document: {path}");
    }

    private static async ValueTask WriteDocumentAsync(
        string projectRoot,
        AgentDeliveryDocument document,
        CancellationToken cancellationToken)
    {
        var directory = AikoProjectPaths.DataRoot(projectRoot);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, FileName);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(
                    output,
                    document,
                    AikoJson.Project,
                    cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
