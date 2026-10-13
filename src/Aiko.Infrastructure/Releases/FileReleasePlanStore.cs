using System.Text.Json;
using Aiko.Application.Contracts;
using Aiko.Infrastructure.Storage;
using Aiko.Infrastructure.Threading;

namespace Aiko.Infrastructure.Releases;

/// <summary>
/// File-based release plans: <c>.aiko/release-plan.json</c> as the source of truth.
/// </summary>
/// <remarks>
/// Written the way the release history is, for the same reasons: the document is read as a whole, so it is
/// one file rather than one per version; every change happens under a per-project lock and lands atomically,
/// so two agents planning the same version cannot lose each other's write; and Aiko keeps no second copy in
/// SQLite, because this is not a projection of anything - a rebuild of the database must not be able to lose
/// the answer to "what was v0.2.0 waiting for".
/// <para>
/// The document is not the whole answer, because a version can be recorded without its plan ever being closed.
/// Every plan is read through <see cref="ReleasePlans.Reconcile(ReleasePlan, ReleaseDocument)"/> against the
/// release history, so a plan whose version has been recorded is a released plan for every caller at once -
/// the plan new cards flow into, the plan that refuses a change, and the plan a screen draws. The file is left
/// exactly as the person wrote it: a read is not a moment to change their document, and closing a plan stays
/// the way to make the document itself true.
/// </para>
/// </remarks>
public sealed class FileReleasePlanStore(
    IProjectCatalog projects,
    IReleaseStore releases,
    IAikoEventPublisher? events = null) : IReleasePlanStore
{
    private const string FileName = "release-plan.json";
    private const int CurrentSchemaVersion = 1;

    /// <summary>Reference-counted per-project locks; idle keys are dropped automatically.</summary>
    private readonly KeyedLockStore locks = new();

    /// <inheritdoc />
    public async ValueTask<ReleasePlanDocument> ReadAsync(
        string projectId,
        CancellationToken cancellationToken)
    {
        var project = await FindProjectAsync(projectId, cancellationToken);
        // Read against the release history rather than handed out as the file holds it: a version that was
        // recorded is a fact, and a plan nobody closed must not keep reading as work still ahead.
        return ReleasePlans.Reconcile(
            await ReadDocumentAsync(project.RootPath, cancellationToken),
            await releases.ReadAsync(projectId, cancellationToken));
    }

    /// <inheritdoc />
    public async ValueTask<ReleasePlan> UpdateAsync(
        string projectId,
        ReleasePlanUpdate update,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        var project = await FindProjectAsync(projectId, cancellationToken);

        using (await locks.LockAsync(project.Id, cancellationToken))
        {
            // Reading, deciding and writing all happen inside the lock, which is the whole reason the store
            // takes an intent rather than a document: a caller that read first would write back a plan that
            // never saw the change made in between.
            var document = await ReadDocumentAsync(project.RootPath, cancellationToken);
            var history = await releases.ReadAsync(projectId, cancellationToken);
            // The decision is taken on the plans as the release history has them, the write is applied to the
            // document as it stands. A version that was recorded refuses a change whether or not its plan was
            // ever closed, and no plan is closed behind the person's back by a write about another version.
            var existing = ReleasePlans.Reconcile(document, history).Find(update.Version);
            // A plan is for a version that will be tagged, so it answers to the rule a release answers to -
            // and a plan being created has to say which scheme the version will follow.
            ReleasePlans.Validate(update.Version, update.SchemeId ?? existing?.SchemeId);
            if (ReleasePlans.RefuseUpdate(existing) is { } refusal)
            {
                throw new InvalidOperationException(refusal);
            }

            if (existing is null && ReleasePlans.RefusePlanning(update.Version, history) is { } shipped)
            {
                // A version in the history with no plan was released without one: planning it now would be
                // planning work for a version that has already shipped.
                throw new InvalidOperationException(shipped);
            }

            var version = update.Version.Trim();
            var now = DateTimeOffset.UtcNow;
            var plan = Apply(existing, update, version, now);

            // Exactly one plan is current: making this one current takes the mark off every other plan that
            // still has a version ahead of it, which is what keeps "where does a new card go" answerable.
            var changed = document with
            {
                Plans = [.. document.Plans.Select(candidate =>
                    ReleasePlan.Same(candidate.Version, version)
                        ? plan
                        : plan.IsCurrent && candidate.IsCurrent && !candidate.IsReleased
                            ? candidate with { IsCurrent = false }
                            : candidate)]
            };
            if (existing is null)
            {
                // A plan that did not exist is the most recently created one, which is where the document
                // holds it.
                changed = changed with { Plans = [plan, .. changed.Plans] };
            }

            await WriteDocumentAsync(project.RootPath, changed, cancellationToken);
            await PublishAsync(project.Id, changed, cancellationToken);
            return plan;
        }
    }

    /// <summary>
    /// The plan as the update leaves it, built from the plan as it stands.
    /// </summary>
    private static ReleasePlan Apply(
        ReleasePlan? existing,
        ReleasePlanUpdate update,
        string version,
        DateTimeOffset now)
    {
        var remove = ReleasePlans.NormalizeCards(update.RemoveCards);
        var cards = (existing?.Cards ?? [])
            .Where(entry => !remove.Any(cardId => ReleasePlan.Same(cardId, entry.CardId)))
            .ToList();
        foreach (var cardId in ReleasePlans.NormalizeCards(update.AddCards))
        {
            // A card already in the plan keeps the place and the note it had: planning it again is not a
            // reason to renumber where it stands.
            if (!cards.Any(entry => ReleasePlan.Same(entry.CardId, cardId)))
            {
                cards.Add(new ReleasePlanEntry(cardId, now));
            }
        }

        // The scheme is guaranteed by the validation above: it is either stated by the caller or taken from
        // the plan being changed.
        var schemeId = (update.SchemeId ?? existing?.SchemeId)!.Trim();
        return new ReleasePlan(
            version,
            schemeId,
            cards,
            update.IsCurrent ?? existing?.IsCurrent ?? false,
            existing?.CreatedAt ?? now,
            existing?.ReleasedAt,
            update.Notes is null ? existing?.Notes : Trim(update.Notes));
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <inheritdoc />
    public async ValueTask<ReleasePlan> CloseAsync(
        string projectId,
        string version,
        IReadOnlyList<string> cardsToCarry,
        string? carryIntoVersion,
        CancellationToken cancellationToken)
    {
        var project = await FindProjectAsync(projectId, cancellationToken);

        using (await locks.LockAsync(project.Id, cancellationToken))
        {
            var document = await ReadDocumentAsync(project.RootPath, cancellationToken);
            var existing = document.Find(version)
                ?? throw new InvalidOperationException(
                    $"No release plan for '{version}' exists in this project, so there is nothing to close.");
            if (existing.IsReleased)
            {
                throw new InvalidOperationException(
                    $"release plan '{existing.Version}' is already closed, at {existing.ReleasedAt:u}. One " +
                    "version has one plan, so closing it twice would give the same question two answers.");
            }

            // Closing works on the document as it stands - it is what makes the document itself true - but the
            // moment it records is the release's own moment when the version was already recorded, so that the
            // plan and the history agree about when the version shipped.
            var history = await releases.ReadAsync(projectId, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var releasedAt = history.Find(existing.Version)?.ReleasedAt ?? now;
            var carry = ReleasePlans.NormalizeCards(cardsToCarry)
                .Where(existing.Names)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            // The closed plan keeps what the version shipped and nothing else; the work that outlived the
            // version is decided below, so it is removed here first and put back only when there is nowhere
            // for it to go.
            var closed = existing with
            {
                ReleasedAt = releasedAt,
                IsCurrent = false,
                Cards =
                [
                    .. existing.Cards.Where(entry =>
                        !carry.Any(cardId => ReleasePlan.Same(cardId, entry.CardId)))
                ]
            };

            var target = ResolveCarryTarget(document, existing, version, carryIntoVersion, now, history);
            if (carry.Length > 0 && target is not null)
            {
                target = target with
                {
                    Cards = [.. target.Cards, .. NewEntriesFor(carry, target, existing.Version, now)]
                };
            }
            else if (carry.Length > 0)
            {
                // Nowhere to carry them: they stay on the closed plan, marked with the version they came
                // from, because a card quietly dropping out of every plan is exactly the silent loss the
                // story forbids. Saying which version takes them is the person's to do.
                closed = closed with
                {
                    Cards = [.. closed.Cards, .. NewEntriesFor(carry, closed, existing.Version, now)]
                };
            }

            var plans = document.Plans
                .Select(candidate => ReleasePlan.Same(candidate.Version, version)
                    ? closed
                    : target is not null && ReleasePlan.Same(candidate.Version, target.Version)
                        ? target
                        : candidate)
                .ToList();
            if (target is not null && !document.Plans.Any(candidate => ReleasePlan.Same(candidate.Version, target.Version)))
            {
                plans.Insert(0, target);
            }

            var changed = document with { Plans = plans };
            await WriteDocumentAsync(project.RootPath, changed, cancellationToken);
            await PublishAsync(project.Id, changed, cancellationToken);
            return closed;
        }
    }

    /// <summary>
    /// Where the work that outlived a version goes.
    /// </summary>
    /// <remarks>
    /// A version named by the caller wins; otherwise the next unreleased plan the project already holds.
    /// When the caller names a version nobody planned, its plan is created carrying the closed plan's scheme
    /// - that is a version the person stated, not one Aiko invented. A version already recorded is refused,
    /// because it is a released version whether or not its plan was ever closed, and a released version takes
    /// no work. The plans are read against the release history here for that reason, not as the file holds
    /// them.
    /// </remarks>
    private static ReleasePlan? ResolveCarryTarget(
        ReleasePlanDocument document,
        ReleasePlan closed,
        string version,
        string? carryIntoVersion,
        DateTimeOffset now,
        ReleaseDocument history)
    {
        var plans = ReleasePlans.Reconcile(document, history);
        ReleasePlan? target;
        if (!string.IsNullOrWhiteSpace(carryIntoVersion) &&
            !ReleasePlan.Same(carryIntoVersion, version))
        {
            ReleaseRecords.Validate(carryIntoVersion, closed.SchemeId);
            target = plans.Find(carryIntoVersion!);
            if (target is null && ReleasePlans.RefusePlanning(carryIntoVersion!, history) is { } shipped)
            {
                throw new ArgumentException(shipped, nameof(carryIntoVersion));
            }

            target ??= new ReleasePlan(carryIntoVersion!.Trim(), closed.SchemeId, [], false, now, null, null);
        }
        else
        {
            target = plans.Next(version);
        }

        if (target is { IsReleased: true })
        {
            throw new ArgumentException(
                $"release plan '{target.Version}' is already closed, so work cannot be carried into it.",
                nameof(carryIntoVersion));
        }

        return target;
    }

    /// <summary>
    /// Plan entries for cards that outlived a version, skipping any the target already plans and marking
    /// each with the version it came from.
    /// </summary>
    private static IEnumerable<ReleasePlanEntry> NewEntriesFor(
        IReadOnlyList<string> cards,
        ReleasePlan target,
        string fromVersion,
        DateTimeOffset now) =>
        cards
            .Where(cardId => !target.Names(cardId))
            .Select(cardId => new ReleasePlanEntry(cardId, now, null, fromVersion));

    private async ValueTask PublishAsync(
        string projectId,
        ReleasePlanDocument document,
        CancellationToken cancellationToken)
    {
        if (events is not null)
        {
            await events.PublishAsync(
                projectId,
                AikoEventTypes.ReleasePlanUpdated,
                JsonSerializer.Serialize(document, ReleaseJson.Document),
                cancellationToken);
        }
    }

    private static async ValueTask<ReleasePlanDocument> ReadDocumentAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(AikoProjectPaths.DataRoot(projectRoot), FileName);
        if (!File.Exists(path))
        {
            return new ReleasePlanDocument(CurrentSchemaVersion, []);
        }

        try
        {
            await using var input = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<ReleasePlanDocument>(
                       input,
                       ReleaseJson.Document,
                       cancellationToken)
                   ?? throw new InvalidDataException($"The release plan at {path} holds no document.");
        }
        catch (JsonException exception)
        {
            // A document nobody can read is refused as one: reporting no plans instead would claim the
            // project plans nothing, which is a different statement from "this file is broken".
            throw new InvalidDataException(
                $"The release plan at {path} cannot be read as a release plan document.",
                exception);
        }
    }

    private static async ValueTask WriteDocumentAsync(
        string projectRoot,
        ReleasePlanDocument document,
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
                    ReleaseJson.Document,
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
