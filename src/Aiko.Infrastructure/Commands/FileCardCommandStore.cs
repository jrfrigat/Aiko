using System.Text.Json;
using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Domain.Execution;
using Aiko.Infrastructure.Projects;
using Aiko.Infrastructure.Storage;
using Aiko.Infrastructure.Threading;

namespace Aiko.Infrastructure.Commands;

/// <summary>
/// File-based command queue: <c>.aiko/commands.json</c> as the source of truth.
/// </summary>
/// <remarks>
/// One document for the whole project rather than a file per command, because the queue is read as a whole -
/// an agent asks "what is waiting for me?" and a screen asks "what is waiting for this card?" - and both are
/// a single read here. Aiko keeps no second copy in SQLite: this is not a projection of anything, the way a
/// card's row is, so a rebuild of the database must not be able to lose a command a person placed.
/// <para>
/// Every transition happens under one per-project lock and is written atomically, which is what makes the
/// queue safe for the thing it exists for: two agents reading the same file at the same time and taking
/// different commands.
/// </para>
/// </remarks>
public sealed class FileCardCommandStore(
    IProjectCatalog projects,
    ICardStore cards,
    IAikoEventPublisher? events = null) : ICardCommandStore
{
    private const string FileName = "commands.json";
    private const int CurrentSchemaVersion = 1;

    /// <summary>Prefix of a command identifier, in the style of the card ids a project already knows.</summary>
    private const string IdPrefix = "CMD-";

    /// <summary>Reference-counted per-project locks; idle keys are dropped automatically.</summary>
    private readonly KeyedLockStore locks = new();

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<CardCommand>> ListAsync(
        string projectId,
        bool includeClosed,
        CancellationToken cancellationToken)
    {
        var project = await FindProjectAsync(projectId, cancellationToken);
        var document = await ReadDocumentAsync(project.RootPath, cancellationToken);
        return includeClosed
            ? document.Entries
            : [.. document.Entries.Where(command => command.IsOpen)];
    }

    /// <inheritdoc />
    public async ValueTask<CardCommand?> FindAsync(
        string projectId,
        string commandId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        var project = await FindProjectAsync(projectId, cancellationToken);
        var document = await ReadDocumentAsync(project.RootPath, cancellationToken);
        return Find(document, commandId);
    }

    /// <inheritdoc />
    public async ValueTask<CardCommand> PlaceAsync(
        string projectId,
        PlaceCommandRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var cardId = Trim(request.CardId);
        // The action's own requirements are checked before anything is written: a command that cannot be
        // carried out is not a queued command, it is a trap for whoever takes it.
        CardCommands.Validate(
            request.Action,
            cardId,
            request.StageId,
            request.ExecutionId,
            request.Text);

        var project = await FindProjectAsync(projectId, cancellationToken);
        if (cardId is not null)
        {
            // The card is named by the project's immutable id however the caller addressed the project,
            // because that is the key every card file, relation and execution is stored under.
            var card = new CardReference(project.Id, cardId);
            if (await cards.FindAsync(card, cancellationToken) is null)
            {
                throw new ArgumentException(
                    $"No card '{cardId}' in project '{project.Id}'.",
                    nameof(request));
            }
        }

        using (await locks.LockAsync(project.Id, cancellationToken))
        {
            var document = await ReadDocumentAsync(project.RootPath, cancellationToken);
            // Whether the command may be placed at all is decided against the queue as it stands, so two
            // requests arriving together cannot both be the one open pass over the board.
            if (CardCommands.RefusePlace(request.Action, document.Entries) is { } refusal)
            {
                throw new InvalidOperationException(refusal);
            }

            var command = new CardCommand(
                $"{IdPrefix}{document.NextSequence}",
                cardId,
                request.Action,
                Trim(request.StageId),
                Trim(request.ExecutionId),
                Trim(request.AgentAdapterId),
                Trim(request.Text),
                CardCommandState.Queued,
                Trim(request.RequestedBy) ?? "you",
                DateTimeOffset.UtcNow,
                null,
                null,
                null);

            var updated = document with
            {
                NextSequence = document.NextSequence + 1,
                Entries = [.. document.Entries, command]
            };
            await WriteDocumentAsync(project.RootPath, updated, cancellationToken);
            await PublishAsync(project.Id, command, cancellationToken);
            return command;
        }
    }

    /// <inheritdoc />
    public async ValueTask<CardCommand> ClaimAsync(
        string projectId,
        string commandId,
        string agentAdapterId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentAdapterId);
        var project = await FindProjectAsync(projectId, cancellationToken);
        var agent = agentAdapterId.Trim();

        return await TransitionAsync(
            project,
            commandId,
            command =>
            {
                if (CardCommands.RefuseClaim(command, agent) is { } refusal)
                {
                    throw new InvalidOperationException(refusal);
                }

                return command with
                {
                    State = CardCommandState.Taken,
                    AgentAdapterId = command.AgentAdapterId ?? agent,
                    ClaimedAtUtc = DateTimeOffset.UtcNow
                };
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<CardCommand> FinishAsync(
        string projectId,
        string commandId,
        CardCommandState state,
        string? message,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        if (state is not (CardCommandState.Completed or CardCommandState.Failed))
        {
            throw new ArgumentException(
                $"A command is closed as completed or failed, not as {state}.",
                nameof(state));
        }

        var project = await FindProjectAsync(projectId, cancellationToken);
        var reported = Trim(message);
        return await TransitionAsync(
            project,
            commandId,
            command =>
            {
                if (CardCommands.RefuseFinish(command) is { } refusal)
                {
                    throw new InvalidOperationException(refusal);
                }

                return command with
                {
                    State = state,
                    FinishedAtUtc = DateTimeOffset.UtcNow,
                    Message = reported
                };
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<CardCommand> CancelAsync(
        string projectId,
        string commandId,
        string? reason,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        var project = await FindProjectAsync(projectId, cancellationToken);
        var reported = Trim(reason);
        return await TransitionAsync(
            project,
            commandId,
            command =>
            {
                if (CardCommands.RefuseCancel(command) is { } refusal)
                {
                    throw new InvalidOperationException(refusal);
                }

                return command with
                {
                    State = CardCommandState.Cancelled,
                    FinishedAtUtc = DateTimeOffset.UtcNow,
                    Message = reported ?? "Withdrawn before an agent took it."
                };
            },
            cancellationToken);
    }

    /// <summary>
    /// Rewrites one command under the project lock, writes the document and announces the change.
    /// </summary>
    /// <remarks>
    /// The read, the change and the write are one critical section on purpose: two agents claiming two
    /// different commands at the same moment must not lose each other's write, and two agents claiming the
    /// same command must have one of them refused rather than both succeed.
    /// </remarks>
    private async ValueTask<CardCommand> TransitionAsync(
        RegisteredProject project,
        string commandId,
        Func<CardCommand, CardCommand> transition,
        CancellationToken cancellationToken)
    {
        using (await locks.LockAsync(project.Id, cancellationToken))
        {
            var document = await ReadDocumentAsync(project.RootPath, cancellationToken);
            if (Find(document, commandId) is not { } existing)
            {
                throw new FileNotFoundException(
                    $"No command '{commandId}' in project '{project.Id}'.");
            }

            // The identifier is restored from the stored record, so a transition can never renumber anything.
            var command = transition(existing) with { Id = existing.Id };
            var entries = document.Entries
                .Select(candidate => StringComparer.Ordinal.Equals(candidate.Id, commandId)
                    ? command
                    : candidate)
                .ToArray();

            await WriteDocumentAsync(
                project.RootPath,
                document with { Entries = entries },
                cancellationToken);
            await PublishAsync(project.Id, command, cancellationToken);
            return command;
        }
    }

    private static CardCommand? Find(CommandDocument document, string commandId) =>
        document.Entries.FirstOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.Id, commandId));

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async ValueTask PublishAsync(
        string projectId,
        CardCommand command,
        CancellationToken cancellationToken)
    {
        if (events is not null)
        {
            await events.PublishAsync(
                projectId,
                AikoEventTypes.CommandsUpdated,
                JsonSerializer.Serialize(command, AikoJson.Project),
                cancellationToken);
        }
    }

    private async ValueTask<CommandDocument> ReadDocumentAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(AikoProjectPaths.DataRoot(projectRoot), FileName);
        if (!File.Exists(path))
        {
            return new CommandDocument(CurrentSchemaVersion, 1, []);
        }

        await using var input = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(
                   input,
                   ProjectJsonContext.Default.CommandDocument,
                   cancellationToken)
               ?? throw new InvalidDataException($"Invalid command document: {path}");
    }

    private static async ValueTask WriteDocumentAsync(
        string projectRoot,
        CommandDocument document,
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

    private async ValueTask<RegisteredProject> FindProjectAsync(
        string projectId,
        CancellationToken cancellationToken) =>
        await projects.FindAsync(projectId, cancellationToken)
        ?? throw new FileNotFoundException($"No Aiko project '{projectId}' is registered.");
}
