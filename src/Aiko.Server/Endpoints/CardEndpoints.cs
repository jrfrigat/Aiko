using Aiko.Application.Cards;
using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Server.Contracts;
using Aiko.Server.Workflow;

namespace Aiko.Server.Endpoints;

/// <summary>
/// Card endpoints: moving cards between stages, updating cards and listing executions.
/// </summary>
internal static class CardEndpoints
{
    /// <summary>
    /// Maps the stage move, card update and execution listing routes.
    /// </summary>
    public static void MapCardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPut(
            "/api/v1/projects/{projectId}/cards/{cardId}/stage",
            async (
                string projectId,
                string cardId,
                MoveCardRequest request,
                IProjectDefinitionStore definitions,
                ICardStore cards,
                CancellationToken cancellationToken) =>
            {
                var reference = new CardReference(projectId, cardId);
                var card = await cards.FindAsync(reference, cancellationToken);
                if (card is null)
                {
                    return Results.NotFound();
                }
                if (card.Revision != request.ExpectedRevision)
                {
                    return Results.Conflict(new RevisionConflictResponse(
                        request.ExpectedRevision,
                        card.Revision));
                }

                var stage = await CardStageValidation.FindValidStageAsync(
                    card,
                    request.StageId,
                    definitions,
                    cancellationToken);
                if (stage is null)
                {
                    return Results.BadRequest(new ErrorResponse("Stage is not valid for this card workflow."));
                }

                var moved = card with { StageId = stage.Id, Revision = card.Revision + 1 };
                await cards.SaveAsync(moved, request.ExpectedRevision, cancellationToken);

                return Results.Ok(moved);
            });
        app.MapPut(
            "/api/v1/projects/{projectId}/cards/{cardId}",
            async (
                string projectId,
                string cardId,
                UpdateCardRequest request,
                ICardStore cards,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.Title) || request.OwnPriority < 0)
                {
                    return Results.BadRequest(new ErrorResponse(
                        "Card title is required and priority cannot be negative."));
                }

                var reference = new CardReference(projectId, cardId);
                var card = await cards.FindAsync(reference, cancellationToken);
                if (card is null)
                {
                    return Results.NotFound();
                }
                if (card.Revision != request.ExpectedRevision)
                {
                    return Results.Conflict(new RevisionConflictResponse(
                        request.ExpectedRevision,
                        card.Revision));
                }

                var updated = card with
                {
                    Title = request.Title.Trim(),
                    OwnPriority = request.OwnPriority,
                    Size = NormalizeSize(request.Size),
                    DeclaredScopeFiles = (request.DeclaredScopeFiles ?? [])
                        .Where(path => !string.IsNullOrWhiteSpace(path))
                        .Select(path => path.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    // Null keeps what the card already carries: a caller that edits only the title has no
                    // opinion about the criteria, and replacing them with nothing would silently erase a
                    // score an agent set. The form that does edit them sends its whole set.
                    CriterionValues = request.CriterionValues is { } criterionValues
                        ? new Dictionary<string, decimal>(
                            criterionValues.Where(pair => !string.IsNullOrWhiteSpace(pair.Key)),
                            StringComparer.Ordinal)
                        : card.CriterionValues,
                    // Requirements follow the same rule: null leaves the text alone, a blank string clears it.
                    Metadata = request.Requirements is { } requirements
                        ? WithRequirements(card.Metadata, requirements)
                        : card.Metadata,
                    Revision = card.Revision + 1
                };
                await cards.SaveAsync(updated, request.ExpectedRevision, cancellationToken);

                return Results.Ok(updated);
            });
        // The card's discussion: the notes a person leaves, and the reports an agent posts through MCP.
        // Both are authored content and live in the card's own folder.
        app.MapGet(
            "/api/v1/projects/{projectId}/cards/{cardId}/discussion",
            async (
                string projectId,
                string cardId,
                ICardDiscussionStore discussion,
                CancellationToken cancellationToken) =>
                Results.Ok(await discussion.ListAsync(
                    new CardReference(projectId, cardId),
                    cancellationToken)));
        app.MapPost(
            "/api/v1/projects/{projectId}/cards/{cardId}/discussion",
            async (
                string projectId,
                string cardId,
                AddCommentRequest request,
                ICardDiscussionStore discussion,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.Body))
                {
                    return Results.BadRequest(new ErrorResponse("A note needs a body."));
                }

                return Results.Ok(await discussion.AppendAsync(
                    new CardReference(projectId, cardId),
                    request.Author ?? "you",
                    request.Body,
                    cancellationToken));
            });
        app.MapGet(
            "/api/v1/projects/{projectId}/cards/{cardId}/executions",
            async (
                string projectId,
                string cardId,
                IExecutionCoordinator executions,
                CancellationToken cancellationToken) =>
                Results.Ok(await executions.ListAsync(
                    new CardReference(projectId, cardId),
                    cancellationToken)));
        app.MapPost(
            "/api/v1/projects/{projectId}/cards",
            async (
                string projectId,
                CreateCardRequest request,
                ICardStore cards,
                IProjectDefinitionStore definitions,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.Title) || request.OwnPriority < 0)
                {
                    return Results.BadRequest(new ErrorResponse(
                        "Card title is required and priority cannot be negative."));
                }

                if (!CardCreation.IsCreationStage(request.StageId))
                {
                    return Results.BadRequest(new ErrorResponse(
                        "A card is created in the backlog and moves from there; it cannot start in another stage."));
                }

                var kind = CardKind.Canonical(request.Kind);
                var (workflow, backlog, reason) = await CardCreation.ResolveAsync(
                    definitions,
                    projectId,
                    kind,
                    request.WorkflowId,
                    cancellationToken);
                if (workflow is null || backlog is null)
                {
                    return Results.BadRequest(new ErrorResponse(reason!));
                }

                // The id is Aiko's own bookkeeping, so it is invented here unless the caller named one.
                var cardId = string.IsNullOrWhiteSpace(request.CardId)
                    ? await CardIdGenerator.NextAsync(cards, projectId, kind, cancellationToken)
                    : request.CardId.Trim();
                var reference = new CardReference(projectId, cardId);
                if (await cards.FindAsync(reference, cancellationToken) is not null)
                {
                    return Results.Conflict(new ErrorResponse($"Card '{cardId}' already exists."));
                }

                var card = new Card(
                    reference,
                    kind,
                    request.Title.Trim(),
                    workflow.Id,
                    backlog.Id,
                    1,
                    request.OwnPriority,
                    (request.DeclaredScopeFiles ?? [])
                        .Where(path => !string.IsNullOrWhiteSpace(path))
                        .Select(path => path.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    [],
                    BuildMetadata(request.Requirements),
                    null,
                    request.CriterionValues,
                    NormalizeSize(request.Size));

                await cards.SaveAsync(card, 0, cancellationToken);
                return Results.Created($"/api/v1/projects/{projectId}/cards/{cardId}", card);
            });
        // Asking an agent to estimate a card. The daemon cannot run an agent - that is a post-MVP feature -
        // so this records who was asked and leaves the command in the card's discussion; the person runs it
        // in that agent's terminal, and the agent writes the estimate back through aiko_estimate_card.
        app.MapPost(
            "/api/v1/projects/{projectId}/cards/{cardId}/estimate",
            async (
                string projectId,
                string cardId,
                EstimateCardRequest request,
                ICardStore cards,
                ICardDiscussionStore discussion,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.AgentAdapterId))
                {
                    return Results.BadRequest(new ErrorResponse("An estimate needs an agent to run it."));
                }

                var reference = new CardReference(projectId, cardId);
                var card = await cards.FindAsync(reference, cancellationToken);
                if (card is null)
                {
                    return Results.NotFound();
                }

                var agent = request.AgentAdapterId.Trim();
                var metadata = new Dictionary<string, string>(card.Metadata, StringComparer.Ordinal)
                {
                    [EstimateAgentMetadataKey] = agent,
                    [EstimateRequestedAtMetadataKey] = DateTimeOffset.UtcNow.ToString("O")
                };
                var updated = card with { Metadata = metadata, Revision = card.Revision + 1 };
                await cards.SaveAsync(updated, card.Revision, cancellationToken);
                await discussion.AppendAsync(
                    reference,
                    "aiko",
                    $"Estimate requested from {agent}. Run /aiko-estimate {cardId} in its terminal.",
                    cancellationToken);
                return Results.Ok(updated);
            });
    }

    /// <summary>The metadata key naming the agent a person asked to estimate a card.</summary>
    private const string EstimateAgentMetadataKey = "estimateRequestedAgentAdapterId";

    /// <summary>The metadata key recording when the estimate was asked for.</summary>
    private const string EstimateRequestedAtMetadataKey = "estimateRequestedAt";

    /// <summary>
    /// The metadata a new card starts with: its requirements, when the caller wrote any.
    /// </summary>
    /// <remarks>
    /// Requirements are prose, so they live in the card's metadata rather than as a field of their own.
    /// A card created without any holds no key at all, which keeps its document as small as the card is.
    /// </remarks>
    private static Dictionary<string, string> BuildMetadata(string? requirements)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(requirements))
        {
            metadata[Card.RequirementsMetadataKey] = requirements.Trim();
        }

        return metadata;
    }

    /// <summary>
    /// The card's metadata with its requirements replaced: the text is trimmed and stored, and a blank
    /// string removes the key rather than storing an empty one, so "no requirements" has one representation.
    /// </summary>
    private static IReadOnlyDictionary<string, string> WithRequirements(
        IReadOnlyDictionary<string, string> metadata,
        string requirements)
    {
        var updated = new Dictionary<string, string>(metadata, StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(requirements))
        {
            updated.Remove(Card.RequirementsMetadataKey);
        }
        else
        {
            updated[Card.RequirementsMetadataKey] = requirements.Trim();
        }

        return updated;
    }

    /// <summary>
    /// Trims a size step and turns the empty string into null, so "no size" has one representation rather
    /// than two. The value is not checked against the project's grid here: the grid is editable, a card
    /// outliving the step it was sized with must not become unreadable, and the formula treats an unknown
    /// step as neutral anyway.
    /// </summary>
    private static string? NormalizeSize(string? size) =>
        string.IsNullOrWhiteSpace(size) ? null : size.Trim();
}
