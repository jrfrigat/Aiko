using System.Text;
using System.Text.Json;
using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Domain.Execution;
using Aiko.Domain.Workflow;
using Aiko.Infrastructure.Cards;
using Aiko.Infrastructure.Projects;
using Aiko.Infrastructure.Settings;
using Aiko.Infrastructure.Storage;
using Aiko.Infrastructure.Threading;

namespace Aiko.Infrastructure.Execution;

/// <summary>
/// SQLite implementation of <see cref="IExecutionCoordinator"/>: the execution journal
/// in SQLite, per-card/per-execution in-process locks and handoff Markdown documents
/// in the card directory. New starts honor the effective execution settings, including
/// the concurrent-run limit of the project.
/// </summary>
public sealed class SqliteExecutionCoordinator(
    IProjectCatalog projects,
    ICardStore cards,
    AikoDatabase database,
    IAppSettingsService? settings = null,
    IAikoEventPublisher? events = null) : IExecutionCoordinator
{
    /// <summary>
    /// Reference-counted per-card and per-execution locks bound to this instance's
    /// lifetime; idle keys are dropped automatically.
    /// </summary>
    private readonly KeyedLockStore locks = new();

    /// <summary>
    /// The one state a run holds a concurrency slot in. Only a working agent occupies a slot, so
    /// <see cref="StageExecutionState.Running"/> is what <c>maxConcurrentRuns</c> counts and what the scope-overlap
    /// check weighs: a run that waits for the user, is paused or needs attention frees its slot instead of holding
    /// it (TASK-89).
    /// </summary>
    private const string RunningState = nameof(StageExecutionState.Running);

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<StageExecution>> ListAsync(
        CardReference card,
        CancellationToken cancellationToken)
    {
        // Executions are keyed by the project's immutable id, while a caller may address the project by its
        // readable handle - an MCP route and the UI's URLs do. Resolving here is what keeps a card's "runs"
        // tab from answering with an empty list for a project that plainly has runs.
        card = await ResolveAsync(card, cancellationToken);
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT document_json
            FROM executions
            WHERE project_id = $projectId AND card_id = $cardId
            ORDER BY created_utc;
            """;
        command.Parameters.AddWithValue("$projectId", card.ProjectId);
        command.Parameters.AddWithValue("$cardId", card.CardId);

        var executions = new List<StageExecution>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            executions.Add(Deserialize(reader.GetString(0)));
        }

        return executions;
    }

    /// <inheritdoc />
    public async ValueTask<StageExecution?> FindAsync(
        string executionId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT document_json FROM executions WHERE id = $id;";
        command.Parameters.AddWithValue("$id", executionId);
        var json = await command.ExecuteScalarAsync(cancellationToken) as string;
        return json is null ? null : Deserialize(json);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<StageRunSummary>> ReadStageRunsAsync(
        string projectId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        var project = await FindProjectAsync(projectId, cancellationToken);

        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // SQLite returns the row a MAX() belongs to, so this is one row per pair: the latest run of that stage
        // and nothing about the ones before it - which is all a badge needs.
        command.CommandText =
            """
            SELECT card_id, stage_id, state, MAX(updated_utc)
            FROM executions
            WHERE project_id = $projectId
            GROUP BY card_id, stage_id;
            """;
        command.Parameters.AddWithValue("$projectId", project.Id);

        var runs = new List<StageRunSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            runs.Add(new StageRunSummary(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2)));
        }

        return runs;
    }

    /// <inheritdoc />
    public async ValueTask<StageExecution> StartAsync(
        CardReference card,
        string stageId,
        string agentAdapterId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentAdapterId);

        // The call may arrive through the project's readable handle: the MCP endpoint carries one, and both
        // the executions table and the event journal declare a foreign key against projects(id). An
        // unresolved handle would reach SQLite as "SQLite Error 19: 'FOREIGN KEY constraint failed'", which
        // is what stopped every start an agent made. Resolving before the locks also means the card, the
        // per-project lock and the run limit all speak about the same project.
        card = await ResolveAsync(card, cancellationToken);

        // Serialize per card (no two starts of one card) and per project (the
        // concurrent-run limit is checked atomically against the whole project).
        using (await locks.LockAsync($"card:{card.ProjectId}/{card.CardId}", cancellationToken))
        using (await locks.LockAsync($"project:{card.ProjectId}", cancellationToken))
        {
            var project = await FindProjectAsync(card.ProjectId, cancellationToken);
            var sourceCard = await cards.FindAsync(card, cancellationToken)
                ?? throw new KeyNotFoundException($"Unknown card: {card.ProjectId}/{card.CardId}");
            var existing = await ListAsync(card, cancellationToken);
            var unfinished = existing.FirstOrDefault(execution => !IsTerminal(execution.State));
            if (unfinished is not null)
            {
                // A repeated start of the stage that is still open continues it: that is what "run the card
                // again" means while its stage is unfinished, and a second execution for the same stage would
                // only split the history of one attempt in two.
                if (!StringComparer.Ordinal.Equals(unfinished.StageId, stageId))
                {
                    throw new InvalidOperationException(
                        $"stage '{unfinished.StageId}' of card '{card.CardId}' is not finished: its state is "
                        + $"{unfinished.State}. Continue it with aiko_start_stage '{unfinished.StageId}', or "
                        + $"complete it with aiko_complete_stage, before starting '{stageId}'.");
                }

                return await ContinueAsync(
                    unfinished,
                    sourceCard,
                    stageId,
                    agentAdapterId,
                    cancellationToken);
            }

            var effectiveSettings = settings is null
                ? ExecutionSettings.SafeDefault
                : await settings.GetEffectiveExecutionAsync(card.ProjectId, cancellationToken);
            var runningInProject = await CountRunningExecutionsAsync(card.ProjectId, cancellationToken);
            if (runningInProject >= effectiveSettings.MaxConcurrentRuns)
            {
                // Only a running execution occupies a slot, so the way out is to finish, pause or cancel one -
                // pausing frees the slot now (TASK-89).
                throw new InvalidOperationException(
                    $"The project allows at most {effectiveSettings.MaxConcurrentRuns} concurrent " +
                    "running stage execution(s). Finish, pause or cancel one of them, or raise " +
                    "maxConcurrentRuns in the project settings.");
            }

            var runningExecutions = await ListRunningExecutionsAsync(card.ProjectId, cancellationToken);
            var scopeConflicts = FindScopeConflicts(sourceCard, runningExecutions);
            string? scopeWarning = null;
            if (scopeConflicts.Count > 0)
            {
                var conflictList = string.Join(", ", scopeConflicts);
                var policy = effectiveSettings.ScopeOverlapPolicy;
                if (policy == ActionPolicy.Allow)
                {
                    scopeWarning =
                        $"Declared scope overlaps active execution(s) {conflictList}; " +
                        "started because scopeOverlapPolicy is Allow.";
                }
                else if (policy == ActionPolicy.Ask)
                {
                    throw new InvalidOperationException(
                        $"Declared scope overlaps active execution(s) {conflictList}; " +
                        "scopeOverlapPolicy is Ask, so resolve or approve the overlap with the user first.");
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Declared scope overlaps active execution(s) {conflictList}; " +
                        "scopeOverlapPolicy is Deny, so overlapping work cannot start.");
                }
            }

            if (!StringComparer.Ordinal.Equals(sourceCard.StageId, stageId))
            {
                sourceCard = sourceCard with
                {
                    StageId = stageId,
                    Revision = sourceCard.Revision + 1
                };
                await cards.SaveAsync(sourceCard, sourceCard.Revision - 1, cancellationToken);
            }

            var now = DateTimeOffset.UtcNow;
            var execution = new StageExecution(
                Guid.CreateVersion7().ToString("N"),
                card,
                stageId,
                WorkspaceMode.Shared,
                project.RootPath,
                sourceCard.DeclaredScopeFiles,
                sourceCard.ActualChangedFiles,
                [
                    new AgentAttempt(
                        Guid.CreateVersion7().ToString("N"),
                        agentAdapterId,
                        null,
                        AgentAttemptState.Running,
                        now,
                        null,
                        null)
                ],
                now,
                StageExecutionState.Running,
                scopeWarning,
                [],
                [],
                [],
                [],
                [],
                now);
            await SaveAsync(execution, true, cancellationToken);
            return execution;
        }
    }

    /// <summary>
    /// Continues an execution whose stage was left unfinished: the same execution is put back to running with a
    /// new agent attempt, so the history of that stage stays in one place.
    /// </summary>
    /// <remarks>
    /// The run limit is deliberately not consulted. Continuing is not a second run of the project, it is the
    /// same one picked up again - and refusing it would leave a card that nobody can move on (leaving an
    /// unfinished stage is refused) and nobody can resume, which is a dead end rather than a guard rail.
    /// </remarks>
    private async ValueTask<StageExecution> ContinueAsync(
        StageExecution execution,
        Card sourceCard,
        string stageId,
        string agentAdapterId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var attempts = execution.Attempts.ToList();
        if (attempts.Count > 0 &&
            attempts[^1].State is AgentAttemptState.Queued or
                AgentAttemptState.Running or
                AgentAttemptState.WaitingForUser or
                AgentAttemptState.Paused)
        {
            attempts[^1] = attempts[^1] with
            {
                State = AgentAttemptState.Superseded,
                FinishedAt = now,
                ExitReason = "Continued by a new attempt on the same stage."
            };
        }

        attempts.Add(new AgentAttempt(
            Guid.CreateVersion7().ToString("N"),
            agentAdapterId,
            null,
            AgentAttemptState.Running,
            now,
            null,
            null));

        if (!StringComparer.Ordinal.Equals(sourceCard.StageId, stageId))
        {
            // The card was pulled back while its stage stayed open: put it back where the work is.
            await cards.SaveAsync(
                sourceCard with { StageId = stageId, Revision = sourceCard.Revision + 1 },
                sourceCard.Revision,
                cancellationToken);
        }

        var continued = execution with
        {
            Attempts = attempts,
            State = StageExecutionState.Running,
            UpdatedAt = now
        };
        await SaveAsync(continued, create: false, cancellationToken);
        return continued;
    }

    /// <inheritdoc />
    public ValueTask<StageExecution> HandoffAsync(
        string executionId,
        string targetAgentAdapterId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetAgentAdapterId);
        return MutateAsync(
            executionId,
            async execution =>
            {
                EnsureNotTerminal(execution);
                await WriteHandoffAsync(execution, cancellationToken);
                var now = DateTimeOffset.UtcNow;
                var attempts = execution.Attempts.ToList();
                if (attempts.Count > 0 &&
                    attempts[^1].State is AgentAttemptState.Queued or
                        AgentAttemptState.Running or
                        AgentAttemptState.WaitingForUser or
                        AgentAttemptState.Paused)
                {
                    attempts = CloseCurrentAttempt(
                        attempts,
                        AgentAttemptState.Superseded,
                        "Handed off to another agent.",
                        now);
                }
                attempts.Add(new AgentAttempt(
                    Guid.CreateVersion7().ToString("N"),
                    targetAgentAdapterId,
                    null,
                    AgentAttemptState.Running,
                    now,
                    null,
                    null));
                return execution with
                {
                    Attempts = attempts,
                    State = StageExecutionState.Running,
                    UpdatedAt = now
                };
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<StageExecution> ReportProgressAsync(
        string executionId,
        string summary,
        IReadOnlyList<string> completedSteps,
        IReadOnlyList<string> remainingSteps,
        IReadOnlyList<string> actualChangedFiles,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        return MutateAsync(
            executionId,
            execution =>
            {
                EnsureNotTerminal(execution);
                return ValueTask.FromResult(execution with
                {
                    ProgressSummary = summary,
                    CompletedSteps = Distinct(completedSteps),
                    RemainingSteps = Distinct(remainingSteps),
                    ActualChangedFiles = Distinct(actualChangedFiles),
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<StageExecution> RequestScopeExpansionAsync(
        string executionId,
        IReadOnlyList<string> requestedScopeFiles,
        string reason,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (requestedScopeFiles.Count == 0)
        {
            throw new ArgumentException("At least one scope file must be requested.");
        }

        return MutateAsync(
            executionId,
            async execution =>
            {
                EnsureNotTerminal(execution);
                var effectiveSettings = settings is null
                    ? ExecutionSettings.SafeDefault
                    : await settings.GetEffectiveExecutionAsync(execution.Card.ProjectId, cancellationToken);
                var policy = effectiveSettings.ScopeExpansionPolicy;
                var now = DateTimeOffset.UtcNow;

                if (policy == ActionPolicy.Deny)
                {
                    // Nothing is written and the run is left exactly as it was: the agent has to stop, and the
                    // refusal is what it reads as the answer.
                    throw new InvalidOperationException(
                        "This project's scopeExpansionPolicy is Deny, so a card cannot be widened beyond its " +
                        "declared scope. Stop and tell the user which files are needed: "
                        + string.Join(", ", requestedScopeFiles));
                }

                if (policy == ActionPolicy.Allow)
                {
                    // Granted on the spot: the files join the card's declared scope and the run keeps going -
                    // it is deliberately not moved to WaitingForUser, because nobody is going to answer. What
                    // was granted stays on the run as well, or "allowed" would look like "nothing happened".
                    var card = await cards.FindAsync(execution.Card, cancellationToken)
                        ?? throw new KeyNotFoundException(
                            $"Unknown card: {execution.Card.ProjectId}/{execution.Card.CardId}");
                    var declared = Distinct(card.DeclaredScopeFiles.Concat(requestedScopeFiles));
                    await cards.SaveAsync(
                        card with
                        {
                            Revision = card.Revision + 1,
                            DeclaredScopeFiles = declared
                        },
                        card.Revision,
                        cancellationToken);
                    return execution with
                    {
                        // The run's own snapshot follows the grant, and it is the run's list that is added to
                        // rather than the card's: a card can be edited while a run is in flight, so taking its
                        // list would let a run's allowance shrink and turn files it had already changed into
                        // violations. A grant only ever adds. Without this the run kept its start-time snapshot
                        // and reported the very file the policy had just granted as out-of-scope (TASK-142).
                        DeclaredScopeFiles = Distinct(
                            execution.DeclaredScopeFiles.Concat(requestedScopeFiles)),
                        ProgressSummary =
                            $"Scope expanded because scopeExpansionPolicy is Allow; the card now declares "
                            + $"{string.Join(", ", declared)}. Reason: {reason}",
                        RequestedScopeFiles = Distinct(
                            execution.RequestedScopeFiles.Concat(requestedScopeFiles)),
                        UpdatedAt = now
                    };
                }

                var attempts = UpdateCurrentAttempt(
                    execution.Attempts,
                    AgentAttemptState.WaitingForUser,
                    reason,
                    null);
                return execution with
                {
                    Attempts = attempts,
                    State = StageExecutionState.WaitingForUser,
                    ProgressSummary = reason,
                    RequestedScopeFiles = Distinct(
                        execution.RequestedScopeFiles.Concat(requestedScopeFiles)),
                    UpdatedAt = now
                };
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<StageExecution> CompleteAsync(
        string executionId,
        IReadOnlyList<string> actualChangedFiles,
        IReadOnlyList<string> artifacts,
        CancellationToken cancellationToken) =>
        MutateAsync(
            executionId,
            async execution =>
            {
                EnsureNotTerminal(execution);
                var now = DateTimeOffset.UtcNow;
                var attempts = CloseCurrentAttempt(
                    execution.Attempts,
                    AgentAttemptState.Completed,
                    null,
                    now);
                var completed = execution with
                {
                    Attempts = attempts,
                    State = StageExecutionState.Completed,
                    ActualChangedFiles = Distinct(actualChangedFiles),
                    Artifacts = Distinct(artifacts),
                    RemainingSteps = [],
                    UpdatedAt = now
                };

                var card = await cards.FindAsync(execution.Card, cancellationToken)
                    ?? throw new KeyNotFoundException(
                        $"Unknown card: {execution.Card.ProjectId}/{execution.Card.CardId}");
                await EnsureEstimateAsync(execution, card, cancellationToken);
                await cards.SaveAsync(
                    card with
                    {
                        Revision = card.Revision + 1,
                        ActualChangedFiles = completed.ActualChangedFiles
                    },
                    card.Revision,
                    cancellationToken);
                return completed;
            },
            cancellationToken);

    /// <summary>
    /// Refuses to complete a stage whose card was not estimated during that run.
    /// </summary>
    /// <remarks>
    /// The readiness criterion is the one number that says how much of the promised outcome exists, and it is
    /// the agent's job to keep it describing the card as it is after each change. Nothing used to check that,
    /// so a card could walk the whole pipeline carrying the score it was created with. The project's criteria
    /// are what make the check possible: a project without them, or a caller with no settings service, has
    /// nothing to estimate and completes freely.
    /// </remarks>
    private async ValueTask EnsureEstimateAsync(
        StageExecution execution,
        Card card,
        CancellationToken cancellationToken)
    {
        if (settings is null)
        {
            return;
        }

        var priority = await settings.GetEffectivePriorityAsync(
            execution.Card.ProjectId,
            cancellationToken);
        if (priority.Criteria.Count == 0)
        {
            return;
        }

        if (card.EstimatedAt is { } estimatedAt && estimatedAt >= execution.CreatedAt)
        {
            return;
        }

        throw new InvalidOperationException(
            $"card '{card.Reference.CardId}' was not re-estimated during this run of stage "
            + $"'{execution.StageId}': its readiness still describes the card as it was before the work. "
            + "Call aiko_estimate_card with the criterion that says how ready the card is - readiness, or "
            + "whatever this project named it - then complete the stage.");
    }

    /// <inheritdoc />
    public ValueTask<StageExecution> PauseAsync(
        string executionId,
        string reason,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return MutateAsync(
            executionId,
            execution =>
            {
                EnsureNotTerminal(execution);
                var now = DateTimeOffset.UtcNow;
                return ValueTask.FromResult(execution with
                {
                    Attempts = CloseCurrentAttempt(
                        execution.Attempts,
                        AgentAttemptState.Paused,
                        reason,
                        now),
                    State = StageExecutionState.Paused,
                    ProgressSummary = reason,
                    UpdatedAt = now
                });
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<StageExecution> ResumeAsync(
        string executionId,
        string agentAdapterId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentAdapterId);
        return MutateAsync(
            executionId,
            execution =>
            {
                EnsureNotTerminal(execution);
                if (execution.State is not (
                    StageExecutionState.Paused or
                    StageExecutionState.WaitingForUser or
                    StageExecutionState.NeedsAttention))
                {
                    throw new InvalidOperationException(
                        "Only paused, waiting or needs-attention executions can be resumed.");
                }

                var now = DateTimeOffset.UtcNow;
                var attempts = execution.Attempts.ToList();
                attempts.Add(new AgentAttempt(
                    Guid.CreateVersion7().ToString("N"),
                    agentAdapterId,
                    null,
                    AgentAttemptState.Running,
                    now,
                    null,
                    null));
                return ValueTask.FromResult(execution with
                {
                    Attempts = attempts,
                    State = StageExecutionState.Running,
                    UpdatedAt = now
                });
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<StageExecution> ReportAgentStateAsync(
        string executionId,
        AgentAttemptState state,
        string? exitReason,
        CancellationToken cancellationToken) =>
        MutateAsync(
            executionId,
            execution =>
            {
                EnsureNotTerminal(execution);
                var now = DateTimeOffset.UtcNow;
                var isAttemptTerminal = state is
                    AgentAttemptState.RateLimited or
                    AgentAttemptState.Failed or
                    AgentAttemptState.Cancelled or
                    AgentAttemptState.Superseded or
                    AgentAttemptState.Completed;
                var attempts = UpdateCurrentAttempt(
                    execution.Attempts,
                    state,
                    exitReason,
                    isAttemptTerminal ? now : null);
                var executionState = state switch
                {
                    AgentAttemptState.WaitingForUser => StageExecutionState.WaitingForUser,
                    AgentAttemptState.Paused => StageExecutionState.Paused,
                    AgentAttemptState.RateLimited or AgentAttemptState.Failed =>
                        StageExecutionState.NeedsAttention,
                    AgentAttemptState.Cancelled => StageExecutionState.Cancelled,
                    _ => StageExecutionState.Running
                };
                return ValueTask.FromResult(execution with
                {
                    Attempts = attempts,
                    State = executionState,
                    UpdatedAt = now
                });
            },
            cancellationToken);

    /// <inheritdoc />
    public ValueTask<StageExecution> ReportCommitAsync(
        string executionId,
        string? commitSha,
        string message,
        IReadOnlyList<string> files,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return MutateAsync(
            executionId,
            async execution =>
            {
                EnsureNotTerminal(execution);
                var effectiveSettings = settings is null
                    ? ExecutionSettings.SafeDefault
                    : await settings.GetEffectiveExecutionAsync(execution.Card.ProjectId, cancellationToken);
                var policy = effectiveSettings.SharedCheckoutCommitPolicy;
                if (policy == ActionPolicy.Deny)
                {
                    throw new InvalidOperationException(
                        "This project's commit policy is Deny: commits are made by the user.");
                }

                var now = DateTimeOffset.UtcNow;
                var state = policy == ActionPolicy.Ask
                    ? CommitState.PendingApproval
                    : CommitState.Recorded;
                var commit = new Commit(commitSha, message, Distinct(files), state, now);
                var attempts = policy == ActionPolicy.Ask
                    ? UpdateCurrentAttempt(execution.Attempts, AgentAttemptState.WaitingForUser, "Awaiting commit approval.", null)
                    : execution.Attempts;

                return execution with
                {
                    Attempts = attempts,
                    Commits = execution.Commits.Append(commit).ToArray(),
                    State = policy == ActionPolicy.Ask ? StageExecutionState.WaitingForUser : execution.State,
                    UpdatedAt = now
                };
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<StageExecution> ApproveCommitAsync(
        string executionId,
        bool approved,
        CancellationToken cancellationToken) =>
        MutateAsync(
            executionId,
            execution =>
            {
                EnsureNotTerminal(execution);
                var commits = execution.Commits.ToList();
                var pendingIndex = commits.FindLastIndex(commit => commit.State == CommitState.PendingApproval);
                if (pendingIndex < 0)
                {
                    throw new InvalidOperationException("The execution has no pending commit request.");
                }

                var now = DateTimeOffset.UtcNow;
                commits[pendingIndex] = commits[pendingIndex] with
                {
                    State = approved ? CommitState.Recorded : CommitState.Rejected,
                    RecordedAtUtc = now
                };
                var attempts = UpdateCurrentAttempt(execution.Attempts, AgentAttemptState.Running, null, null);
                return ValueTask.FromResult(execution with
                {
                    Attempts = attempts,
                    Commits = commits,
                    State = StageExecutionState.Running,
                    UpdatedAt = now
                });
            },
            cancellationToken);

    private async ValueTask<StageExecution> MutateAsync(
        string executionId,
        Func<StageExecution, ValueTask<StageExecution>> mutation,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);

        using (await locks.LockAsync(executionId, cancellationToken))
        {
            var execution = await FindAsync(executionId, cancellationToken)
                ?? throw new KeyNotFoundException($"Unknown stage execution: {executionId}");
            var updated = await mutation(execution);
            await SaveAsync(updated, false, cancellationToken);
            return updated;
        }
    }

    private async ValueTask SaveAsync(
        StageExecution execution,
        bool create,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(
            execution,
            ProjectJsonContext.Default.StageExecution);
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = create
            ? """
              INSERT INTO executions(
                  id, project_id, card_id, stage_id, state, document_json, created_utc, updated_utc)
              VALUES (
                  $id, $projectId, $cardId, $stageId, $state, $documentJson, $createdUtc, $updatedUtc);
              """
            : """
              UPDATE executions
              SET state = $state, document_json = $documentJson, updated_utc = $updatedUtc
              WHERE id = $id;
              """;
        command.Parameters.AddWithValue("$id", execution.Id);
        command.Parameters.AddWithValue("$projectId", execution.Card.ProjectId);
        command.Parameters.AddWithValue("$cardId", execution.Card.CardId);
        command.Parameters.AddWithValue("$stageId", execution.StageId);
        command.Parameters.AddWithValue("$state", execution.State.ToString());
        command.Parameters.AddWithValue("$documentJson", json);
        command.Parameters.AddWithValue("$createdUtc", execution.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", execution.UpdatedAt.ToString("O"));
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (!create && affected != 1)
        {
            throw new InvalidOperationException(
                $"Execution '{execution.Id}' disappeared while it was being updated.");
        }

        if (events is not null)
        {
            await events.PublishAsync(
                execution.Card.ProjectId,
                AikoEventTypes.ExecutionUpdated,
                json,
                cancellationToken);
        }
    }

    private async ValueTask<RegisteredProject> FindProjectAsync(
        string projectId,
        CancellationToken cancellationToken) =>
        await projects.FindAsync(projectId, cancellationToken)
        ?? throw new KeyNotFoundException($"Unknown Aiko project: {projectId}");

    /// <summary>
    /// Rewrites a card reference so it carries the project's immutable id, whatever handle the caller used.
    /// </summary>
    /// <remarks>
    /// Every projection of this store - executions, the event journal - is keyed by the id and declares a
    /// foreign key against it, so a reference that still holds the readable handle cannot be stored. The
    /// callers that pass a handle are the ones a person types into an agent's configuration, and the failure
    /// they used to get back was an opaque constraint violation.
    /// </remarks>
    private async ValueTask<CardReference> ResolveAsync(
        CardReference card,
        CancellationToken cancellationToken)
    {
        var project = await FindProjectAsync(card.ProjectId, cancellationToken);
        return StringComparer.Ordinal.Equals(project.Id, card.ProjectId)
            ? card
            : new CardReference(project.Id, card.CardId);
    }

    /// <summary>
    /// How many runs are actually running in the project: what <c>maxConcurrentRuns</c> limits. The state is bound
    /// from the enum rather than written into the SQL, so the query cannot drift from the model while the count
    /// keeps meaning "an agent is working right now" (TASK-89).
    /// </summary>
    private async ValueTask<long> CountRunningExecutionsAsync(
        string projectId,
        CancellationToken cancellationToken)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM executions
            WHERE project_id = $projectId
              AND state = $running;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$running", RunningState);
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    /// <summary>
    /// The runs actually running in the project, for the scope-overlap check: a parked run is not writing files
    /// while it waits, so it does not conflict with a start (TASK-89).
    /// </summary>
    private async ValueTask<IReadOnlyList<StageExecution>> ListRunningExecutionsAsync(
        string projectId,
        CancellationToken cancellationToken)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT document_json
            FROM executions
            WHERE project_id = $projectId
              AND state = $running;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$running", RunningState);

        var executions = new List<StageExecution>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            executions.Add(Deserialize(reader.GetString(0)));
        }

        return executions;
    }

    private static IReadOnlyList<string> FindScopeConflicts(
        Card sourceCard,
        IReadOnlyList<StageExecution> activeExecutions)
    {
        var conflicts = new List<string>();
        foreach (var active in activeExecutions)
        {
            if (StringComparer.Ordinal.Equals(active.Card.CardId, sourceCard.Reference.CardId))
            {
                continue;
            }

            if (sourceCard.DeclaredScopeFiles.Any(newPattern =>
                    active.DeclaredScopeFiles.Any(existingPattern =>
                        ScopeMatcher.Overlaps(newPattern, existingPattern))))
            {
                conflicts.Add(active.Card.CardId);
            }
        }

        return conflicts;
    }

    private async ValueTask WriteHandoffAsync(
        StageExecution execution,
        CancellationToken cancellationToken)
    {
        var project = await FindProjectAsync(execution.Card.ProjectId, cancellationToken);
        var card = await cards.FindAsync(execution.Card, cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Unknown card: {execution.Card.ProjectId}/{execution.Card.CardId}");
        // The handoff is filed beside the card where it actually is; see FileCardStore.FindCardDirectory.
        var handoffDirectory = Path.Combine(
            FileCardStore.FindCardDirectory(project.RootPath, card.Reference.CardId)
                ?? throw new KeyNotFoundException(
                    $"Unknown card: {execution.Card.ProjectId}/{execution.Card.CardId}"),
            "handoffs");
        Directory.CreateDirectory(handoffDirectory);
        var previousAttempt = execution.Attempts.LastOrDefault();
        var content = new StringBuilder()
            .AppendLine($"# Handoff {execution.Id}")
            .AppendLine()
            .AppendLine($"- Stage: {execution.StageId}")
            .AppendLine($"- Workspace: {execution.WorkspacePath}")
            .AppendLine($"- Previous agent: {previousAttempt?.AgentAdapterId ?? "unknown"}")
            .AppendLine($"- Previous state: {previousAttempt?.State.ToString() ?? "unknown"}")
            .AppendLine()
            .AppendLine("## Progress")
            .AppendLine()
            .AppendLine(execution.ProgressSummary ?? "No progress summary was reported.")
            .AppendLine()
            .AppendLine("## Completed steps")
            .AppendLines(execution.CompletedSteps)
            .AppendLine()
            .AppendLine("## Remaining steps")
            .AppendLines(execution.RemainingSteps)
            .AppendLine()
            .AppendLine("## Declared scope")
            .AppendLines(execution.DeclaredScopeFiles)
            .AppendLine()
            .AppendLine("## Actual changed files")
            .AppendLines(execution.ActualChangedFiles)
            .AppendLine()
            .AppendLine("## Artifacts")
            .AppendLines(execution.Artifacts)
            .ToString();
        var path = Path.Combine(
            handoffDirectory,
            $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{previousAttempt?.Id ?? "attempt"}.md");
        await File.WriteAllTextAsync(path, content, cancellationToken);
    }

    private static List<AgentAttempt> CloseCurrentAttempt(
        IReadOnlyList<AgentAttempt> attempts,
        AgentAttemptState state,
        string? reason,
        DateTimeOffset finishedAt) =>
        UpdateCurrentAttempt(attempts, state, reason, finishedAt);

    private static List<AgentAttempt> UpdateCurrentAttempt(
        IReadOnlyList<AgentAttempt> attempts,
        AgentAttemptState state,
        string? reason,
        DateTimeOffset? finishedAt)
    {
        if (attempts.Count == 0)
        {
            throw new InvalidOperationException("Execution has no agent attempt.");
        }

        var updated = attempts.ToList();
        updated[^1] = updated[^1] with
        {
            State = state,
            FinishedAt = finishedAt,
            ExitReason = reason
        };
        return updated;
    }

    private static IReadOnlyList<string> Distinct(IEnumerable<string> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static StageExecution Deserialize(string json) =>
        JsonSerializer.Deserialize(json, ProjectJsonContext.Default.StageExecution)
        ?? throw new InvalidDataException("Invalid execution document in SQLite.");

    private static bool IsTerminal(StageExecutionState state) =>
        state is StageExecutionState.Completed or StageExecutionState.Cancelled;

    private static void EnsureNotTerminal(StageExecution execution)
    {
        if (IsTerminal(execution.State))
        {
            throw new InvalidOperationException(
                $"Execution '{execution.Id}' is already {execution.State}.");
        }
    }
}
