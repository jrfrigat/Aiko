using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using ModelContextProtocol.Server;
using Aiko.Application.Cards;
using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
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
    IProjectDefinitionStore definitions) : ProjectToolBase(httpContextAccessor, projects)
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
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ownPriority);
        var canonicalKind = ParseCardKind(kind);
        var (workflow, backlog, reason) = await CardCreation.ResolveAsync(
            definitions,
            GetProjectId(),
            canonicalKind,
            workflowId,
            cancellationToken);
        if (workflow is null || backlog is null)
        {
            throw new ArgumentException(reason!, nameof(kind));
        }

        var resolvedId = string.IsNullOrWhiteSpace(cardId)
            ? await CardIdGenerator.NextAsync(cards, GetProjectId(), canonicalKind, cancellationToken)
            : cardId.Trim();
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(requirements))
        {
            metadata[Card.RequirementsMetadataKey] = requirements.Trim();
        }

        var card = new Card(
            new CardReference(GetProjectId(), resolvedId),
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
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ownPriority);
        var reference = new CardReference(GetProjectId(), cardId);
        var existing = await cards.FindAsync(reference, cancellationToken)
            ?? throw new KeyNotFoundException($"Card '{cardId}' was not found.");
        var updated = existing with
        {
            Title = title,
            Revision = expectedRevision + 1,
            OwnPriority = ownPriority,
            DeclaredScopeFiles = declaredScopeFiles,
            ActualChangedFiles = actualChangedFiles,
            // Absent means "leave it alone"; an empty string is how a caller clears the size.
            Size = size is null ? existing.Size : string.IsNullOrWhiteSpace(size) ? null : size.Trim()
        };
        await cards.SaveAsync(updated, expectedRevision, cancellationToken);
        return JsonSerializer.Serialize(updated, ServerJsonContext.Default.Card);
    }

    [McpServerTool(Name = "aiko_move_card", Title = "Move Aiko card")]
    [Description(
        "Moves a card to another stage of its workflow, validating the stage against the card kind.")]
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

        var stage = await CardStageValidation.FindValidStageAsync(card, stageId, definitions, cancellationToken)
            ?? throw new ArgumentException(
                $"Stage '{stageId}' is not valid for card '{cardId}' workflow.",
                nameof(stageId));

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
        var projectId = GetProjectId();
        var relation = new CardRelation(
            Guid.CreateVersion7().ToString("N"),
            new CardReference(projectId, sourceCardId),
            new CardReference(projectId, targetCardId),
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
