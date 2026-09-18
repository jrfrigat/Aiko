using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using ModelContextProtocol.Server;
using Aiko.Application.Cards;
using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Domain.Workflow;
using Aiko.Server.Contracts;
using Aiko.Server.Workflow;

namespace Aiko.Server.Mcp;

/// <summary>
/// MCP tools for cards and their relations: listing, reading, creating,
/// updating, taking and linking.
/// </summary>
[McpServerToolType]
internal sealed class CardTools(
    IHttpContextAccessor httpContextAccessor,
    IProjectCatalog projects,
    ICardStore cards,
    IRelationStore relations,
    IProjectDefinitionStore definitions,
    ICardDiscussionStore discussion,
    IExecutionCoordinator executions) : ProjectToolBase(httpContextAccessor, projects)
{
    [McpServerTool(Name = "aiko_list_cards", Title = "List Aiko cards")]
    [Description("Lists cards in the current project. Get project context before taking action.")]
    public async Task<string> ListCardsAsync(
        [Description("Optional card kind: story, task, or any type the project defines. Pass null for all.")]
        string? kind,
        [Description("Optional stage id. Pass null for all stages.")]
        string? stageId,
        CancellationToken cancellationToken)
    {
        var projectId = GetProjectId();
        var result = await cards.ListAsync(projectId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(kind))
        {
            var parsedKind = ParseCardKind(kind);
            result = result.Where(card =>
                StringComparer.OrdinalIgnoreCase.Equals(card.Kind, parsedKind)).ToArray();
        }

        if (!string.IsNullOrWhiteSpace(stageId))
        {
            result = result
                .Where(card => StringComparer.Ordinal.Equals(card.StageId, stageId))
                .ToArray();
        }

        return JsonSerializer.Serialize(result, ServerJsonContext.Default.IReadOnlyListCard);
    }

    [McpServerTool(Name = "aiko_get_card", Title = "Get Aiko card")]
    [Description("Gets the full card including declared and actual changed files.")]
    public async Task<string> GetCardAsync(
        [Description("Card id, for example TASK-001.")]
        string cardId,
        CancellationToken cancellationToken)
    {
        var card = await cards.FindAsync(
            new CardReference(GetProjectId(), cardId),
            cancellationToken);
        return card is null
            ? $"Card '{cardId}' was not found."
            : JsonSerializer.Serialize(card, ServerJsonContext.Default.Card);
    }

    [McpServerTool(Name = "aiko_create_card", Title = "Create Aiko card")]
    [Description(
        "Creates a card of any type the project defines at revision 1 in the current project. Read "
        + "aiko_get_project_context to see which types exist. The card lands in its workflow's backlog "
        + "stage; Aiko names it, so pass no id unless you are importing a card that already has one. "
        + "Leave size and criterion values out when the person did not set them - run /aiko-estimate "
        + "afterwards, or estimate them yourself, instead of guessing a number here.")]
    public async Task<string> CreateCardAsync(
        [Description("Card kind: story, task, or any type this project defines.")]
        string kind,
        [Description("Human-readable title.")]
        string title,
        [Description("Own priority score, zero or greater.")]
        decimal ownPriority,
        [Description(
            "Stable file-safe card id, only when importing a card that already has one. Leave it out and "
            + "Aiko invents the next free id for the type, for example TASK-3.")]
        [Optional] string? cardId,
        [Description("Workflow id that defines the type, for example story or task. Defaults to the kind's own id.")]
        [Optional] string? workflowId,
        [Description("Declared scope file patterns.")]
        [Optional] string[]? declaredScopeFiles,
        [Description(
            "Size step from the project's size grid (see aiko_get_project_context). Pick the step whose "
            + "description matches the work; a large step is a plan to split rather than something to start. "
            + "Leave it out when the person did not set one.")]
        [Optional] string? size,
        [Description("What the card is asked to do, when the title alone is not enough.")]
        [Optional] string? requirements,
        [Description(
            "What the user asked for, in their own words - verbatim, or as close as you can get without "
            + "tidying it up into a task. It records what was asked and is fixed once the card leaves the "
            + "backlog, while the requirements go on describing the work.")]
        [Optional] string? request,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ownPriority);
        // The MCP route may carry the readable handle; the card is filed under the project's own id.
        var project = await GetProjectAsync(cancellationToken);
        var canonicalKind = ParseCardKind(kind);
        var (workflow, backlog, reason) = await CardCreation.ResolveAsync(
            definitions,
            project.Id,
            canonicalKind,
            workflowId,
            cancellationToken);
        if (workflow is null || backlog is null)
        {
            throw new ArgumentException(reason!, nameof(kind));
        }

        var resolvedId = string.IsNullOrWhiteSpace(cardId)
            ? await CardIdGenerator.NextAsync(cards, project.Id, canonicalKind, cancellationToken)
            : cardId.Trim();
        IReadOnlyDictionary<string, string> metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        metadata = Card.WithText(metadata, Card.RequirementsMetadataKey, requirements);
        metadata = Card.WithText(metadata, Card.RequestMetadataKey, request);

        var card = new Card(
            new CardReference(project.Id, resolvedId),
            canonicalKind,
            title,
            workflow.Id,
            backlog.Id,
            1,
            ownPriority,
            declaredScopeFiles ?? [],
            [],
            metadata,
            Size: string.IsNullOrWhiteSpace(size) ? null : size.Trim());
        await cards.SaveAsync(card, 0, cancellationToken);
        return JsonSerializer.Serialize(card, ServerJsonContext.Default.Card);
    }

    [McpServerTool(Name = "aiko_estimate_card", Title = "Estimate Aiko card")]
    [Description(
        "Records an estimate on a card: the size step from the project's grid and the score for every "
        + "criterion the project defines. Read the card and aiko_get_project_context first - the context "
        + "carries each criterion's range and each size step's description. Only the fields you pass are "
        + "written, so an estimate that only sets the size leaves the scores alone.")]
    public async Task<string> EstimateCardAsync(
        [Description("Card id to estimate.")]
        string cardId,
        [Description("Revision read by the caller. The saved revision becomes expectedRevision + 1.")]
        long expectedRevision,
        [Description("Size step from the project's size grid that matches the work.")]
        [Optional] string? size,
        [Description(
            "Scores to write, one \"criterionId=score\" per entry, for example \"complexity=5\". Pass a score "
            + "for every criterion the project defines; criteria left out keep their stored value.")]
        [Optional] string[]? criterionValues,
        [Description("Own priority the estimate implies, as a number, when the criteria are not the whole story.")]
        [Optional] string? ownPriority,
        CancellationToken cancellationToken)
    {
        var reference = new CardReference(GetProjectId(), cardId);
        var existing = await cards.FindAsync(reference, cancellationToken)
            ?? throw new KeyNotFoundException($"Card '{cardId}' was not found.");
        if (existing.Revision != expectedRevision)
        {
            throw new RevisionConflictException(
                $"card {GetProjectId()}/{cardId}",
                expectedRevision,
                existing.Revision);
        }

        var scores = ParseScores(criterionValues);
        var updated = existing with
        {
            // Absent means "leave it alone", which is why each field is only written when the caller sent it.
            Size = size is null ? existing.Size : string.IsNullOrWhiteSpace(size) ? null : size.Trim(),
            CriterionValues = scores is null ? existing.CriterionValues : scores,
            OwnPriority = ParseOwnPriority(ownPriority) ?? existing.OwnPriority,
            // The moment of the estimate is recorded only when criterion scores were written: the size alone
            // says nothing about how ready the card is, and the gate on a stage's completion reads the moment.
            Metadata = scores is null
                ? existing.Metadata
                : new Dictionary<string, string>(existing.Metadata, StringComparer.Ordinal)
                {
                    [Card.EstimatedAtMetadataKey] =
                        DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                },
            Revision = existing.Revision + 1
        };
        await cards.SaveAsync(updated, expectedRevision, cancellationToken);
        return JsonSerializer.Serialize(updated, ServerJsonContext.Default.Card);
    }

    [McpServerTool(Name = "aiko_update_card", Title = "Update Aiko card")]
    [Description(
        "Updates an existing card using optimistic revision control. Does not change the stage: use aiko_move_card for that.")]
    public async Task<string> UpdateCardAsync(
        [Description("Card id.")]
        string cardId,
        [Description("Revision read by the caller. The saved revision becomes expectedRevision + 1.")]
        long expectedRevision,
        [Description("New title.")]
        string title,
        [Description("New own priority score.")]
        decimal ownPriority,
        [Description("Complete declared scope list.")]
        string[] declaredScopeFiles,
        [Description("Complete actual changed file list.")]
        string[] actualChangedFiles,
        [Description(
            "Size step from the project's size grid. Pass an empty string to clear it, or leave it out to "
            + "keep the card's current size.")]
        [Optional] string? size,
        [Description(
            "What the card is asked to do, or leave it out to keep the stored text. Pass an empty string to "
            + "clear it.")]
        [Optional] string? requirements,
        [Description(
            "The original request. Only a card still in the backlog accepts a change here, clearing included: "
            + "once work has started the request records what was asked and stays as it is.")]
        [Optional] string? request,
        [Description(
            "Why the description changed, so the card's discussion can say what the change came from. Leave it "
            + "out and the note is written without the quote.")]
        [Optional] string? requirementsReason,
        [Description("Who is writing, for example the agent adapter id; left out, the note is signed 'agent'.")]
        [Optional] string? author,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ownPriority);
        var reference = new CardReference(GetProjectId(), cardId);
        var existing = await cards.FindAsync(reference, cancellationToken)
            ?? throw new KeyNotFoundException($"Card '{cardId}' was not found.");

        var metadata = existing.Metadata;
        if (request is { } nextRequest)
        {
            // The request records what was asked for, so only the backlog may still correct it.
            if (Card.RefuseRequestChange(cardId, existing.StageId) is { } refusal)
            {
                throw new InvalidOperationException(refusal);
            }

            metadata = Card.WithText(metadata, Card.RequestMetadataKey, nextRequest);
        }

        var requirementsBefore = existing.Requirements;
        if (requirements is { } nextRequirements)
        {
            metadata = Card.WithText(metadata, Card.RequirementsMetadataKey, nextRequirements);
        }

        var updated = existing with
        {
            Title = title,
            Revision = expectedRevision + 1,
            OwnPriority = ownPriority,
            DeclaredScopeFiles = declaredScopeFiles,
            ActualChangedFiles = actualChangedFiles,
            // Absent means "leave it alone"; an empty string is how a caller clears the size.
            Size = size is null ? existing.Size : string.IsNullOrWhiteSpace(size) ? null : size.Trim(),
            // The card's two texts were already folded in above.
            Metadata = metadata
        };
        await cards.SaveAsync(updated, expectedRevision, cancellationToken);
        await ExplainRequirementsChangeAsync(
            updated,
            requirementsBefore,
            requirementsReason,
            author,
            cancellationToken);
        return JsonSerializer.Serialize(updated, ServerJsonContext.Default.Card);
    }

    /// <summary>
    /// Records in the card's discussion why its requirements changed, when they actually did.
    /// </summary>
    /// <remarks>
    /// Best effort on purpose: the card is already saved when this runs, and failing the tool call over the note
    /// would only invite a retry that applies the same change twice.
    /// </remarks>
    private async ValueTask ExplainRequirementsChangeAsync(
        Card updated,
        string? before,
        string? reason,
        string? author,
        CancellationToken cancellationToken)
    {
        if (Card.RequirementsChangeNote(before, updated.Requirements, reason) is not { } note)
        {
            return;
        }

        try
        {
            await discussion.AppendAsync(
                updated.Reference,
                string.IsNullOrWhiteSpace(author) ? "agent" : author,
                note,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
        }
    }

    [McpServerTool(Name = "aiko_move_card", Title = "Move Aiko card")]
    [Description(
        "Moves a card one stage forward in its workflow, or anywhere backwards, validating the stage against "
        + "the card kind. A forward move is refused while the stage the card is leaving has no execution: the "
        + "card moves on because the stage is done, so run it with aiko_start_stage first.")]
    public async Task<string> MoveCardAsync(
        [Description("Card id.")]
        string cardId,
        [Description("Target stage id.")]
        string stageId,
        [Description("Revision read by the caller. The saved revision becomes expectedRevision + 1.")]
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stageId);
        var reference = new CardReference(GetProjectId(), cardId);
        var card = await cards.FindAsync(reference, cancellationToken)
            ?? throw new KeyNotFoundException($"Card '{cardId}' was not found.");
        if (card.Revision != expectedRevision)
        {
            throw new RevisionConflictException(
                $"card {GetProjectId()}/{cardId}",
                expectedRevision,
                card.Revision);
        }

        var stages = await CardStageValidation.ReadStagesAsync(card, definitions, cancellationToken);
        var stage = CardStageValidation.FindValidStage(stages, card, stageId)
            ?? throw new ArgumentException(
                $"Stage '{stageId}' is not valid for card '{cardId}' workflow.",
                nameof(stageId));

        // The agent's path, and deliberately not the board's: an agent that can move a card from the backlog
        // straight to review can call a card done without running anything, while a person dragging a card
        // knows what they are doing - the REST move endpoint stays free for exactly that reason.
        var cardExecutions = await executions.ListAsync(reference, cancellationToken);
        var refusal = CardProgress.RefuseForwardMove(
            cardId,
            card.StageId,
            stage.Id,
            stages,
            cardExecutions
                .Select(execution => new StageRun(execution.StageId, execution.State))
                .ToArray());
        if (refusal is not null)
        {
            throw new InvalidOperationException(refusal);
        }

        var moved = card with { StageId = stage.Id, Revision = card.Revision + 1 };
        await cards.SaveAsync(moved, expectedRevision, cancellationToken);
        return JsonSerializer.Serialize(moved, ServerJsonContext.Default.Card);
    }

    [McpServerTool(Name = "aiko_link_cards", Title = "Link Aiko cards")]
    [Description(
        "Creates a directed implements, parent-child or blocks edge, or a symmetric relates-to edge.")]
    public async Task<string> LinkCardsAsync(
        [Description("Source card id.")]
        string sourceCardId,
        [Description("Target card id.")]
        string targetCardId,
        [Description("Relation type: implements, parent-child, blocks or relates-to.")]
        string relationType,
        CancellationToken cancellationToken)
    {
        // The relation is built from the project's own id, not from the route value: an agent connects through
        // the readable handle, and a handle written into relations.json is a reference nothing else resolves.
        var project = await GetProjectAsync(cancellationToken);
        var relation = new CardRelation(
            Guid.CreateVersion7().ToString("N"),
            new CardReference(project.Id, sourceCardId),
            new CardReference(project.Id, targetCardId),
            relationType,
            DateTimeOffset.UtcNow);
        await relations.SaveAsync(relation, cancellationToken);
        return JsonSerializer.Serialize(relation, ServerJsonContext.Default.CardRelation);
    }

    [McpServerTool(Name = "aiko_take_card", Title = "Take Aiko card")]
    [Description(
        "Assigns the current card to an agent. Use before starting work when no board assignment exists.")]
    public async Task<string> TakeCardAsync(
        [Description("Card id.")]
        string cardId,
        [Description("Agent adapter id, for example claude-code or codex.")]
        string agentAdapterId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentAdapterId);
        var reference = new CardReference(GetProjectId(), cardId);
        var card = await cards.FindAsync(reference, cancellationToken)
            ?? throw new KeyNotFoundException($"Card '{cardId}' was not found.");
        var metadata = new Dictionary<string, string>(card.Metadata, StringComparer.Ordinal)
        {
            ["assignedAgentAdapterId"] = agentAdapterId,
            ["assignmentUpdatedAt"] = DateTimeOffset.UtcNow.ToString("O")
        };
        var updated = card with
        {
            Revision = card.Revision + 1,
            Metadata = metadata
        };
        await cards.SaveAsync(updated, card.Revision, cancellationToken);
        return JsonSerializer.Serialize(updated, ServerJsonContext.Default.Card);
    }

    [McpServerTool(Name = "aiko_add_comment", Title = "Comment on an Aiko card")]
    [Description(
        "Appends a note to a card's discussion, which is the feed its page shows. Post the outcome of a "
        + "stage here - what was done, what was left - so the card that asked for the work explains what "
        + "came of it instead of leaving the feed empty.")]
    public async Task<string> AddCommentAsync(
        [Description("Card id.")]
        string cardId,
        [Description("The note itself.")]
        string body,
        [Description(
            "Who is writing, for example the agent adapter id; left out, the note is signed 'agent'.")]
        string? author,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        var comment = await discussion.AppendAsync(
            new CardReference(GetProjectId(), cardId),
            string.IsNullOrWhiteSpace(author) ? "agent" : author,
            body,
            cancellationToken);
        return JsonSerializer.Serialize(comment, ServerJsonContext.Default.CardComment);
    }

    [McpServerTool(Name = "aiko_list_comments", Title = "Read an Aiko card discussion")]
    [Description(
        "Reads a card's discussion, oldest first, so an agent can see what was already said about the card "
        + "before adding to it.")]
    public async Task<string> ListCommentsAsync(
        [Description("Card id.")]
        string cardId,
        CancellationToken cancellationToken)
    {
        var comments = await discussion.ListAsync(
            new CardReference(GetProjectId(), cardId),
            cancellationToken);
        return JsonSerializer.Serialize(
            comments,
            ServerJsonContext.Default.IReadOnlyListCardComment);
    }

    /// <summary>
    /// Reads the estimate's <c>criterionId=score</c> entries into the dictionary a card stores, or null when
    /// the caller sent none - which leaves the stored scores untouched.
    /// </summary>
    /// <remarks>
    /// The MCP tool takes text rather than a JSON object because the daemon's MCP serializer is
    /// source-generated and carries no type info for a <see cref="Dictionary{TKey,TValue}"/>; a flat list of
    /// pairs is what it can marshal, and it stays readable in a tool-call transcript.
    /// </remarks>
    private static Dictionary<string, decimal>? ParseScores(string[]? entries)
    {
        if (entries is not { Length: > 0 })
        {
            return null;
        }

        var scores = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var separator = entry.IndexOf('=');
            if (separator <= 0 || separator == entry.Length - 1)
            {
                throw new ArgumentException(
                    $"'{entry}' is not a criterion score; expected \"criterionId=score\".",
                    nameof(entries));
            }

            var id = entry[..separator].Trim();
            if (!decimal.TryParse(
                    entry[(separator + 1)..].Trim(),
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var score))
            {
                throw new ArgumentException($"'{entry}' has no readable score.", nameof(entries));
            }

            scores[id] = score;
        }

        return scores;
    }

    /// <summary>Reads an optional decimal the caller sent as text, or null when it sent none.</summary>
    private static decimal? ParseOwnPriority(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : decimal.TryParse(
                value.Trim(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var priority)
                ? priority
                : throw new ArgumentException($"'{value}' is not a priority.", nameof(value));
}
