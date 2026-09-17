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
                    Revision = card.Revision + 1
                };
                await cards.SaveAsync(updated, request.ExpectedRevision, cancellationToken);

                return Results.Ok(updated);
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
                if (string.IsNullOrWhiteSpace(request.CardId) ||
                    string.IsNullOrWhiteSpace(request.Title) ||
                    request.OwnPriority < 0)
                {
                    return Results.BadRequest(new ErrorResponse(
                        "Card id and title are required and priority cannot be negative."));
                }

                var reference = new CardReference(projectId, request.CardId);
                if (await cards.FindAsync(reference, cancellationToken) is not null)
                {
                    return Results.Conflict(new ErrorResponse($"Card '{request.CardId}' already exists."));
                }

                var card = new Card(
                    reference,
                    request.Kind,
                    request.Title.Trim(),
                    request.WorkflowId,
                    request.StageId,
                    1,
                    request.OwnPriority,
                    (request.DeclaredScopeFiles ?? [])
                        .Where(path => !string.IsNullOrWhiteSpace(path))
                        .Select(path => path.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    [],
                    new Dictionary<string, string>(StringComparer.Ordinal),
                    null,
                    request.CriterionValues,
                    NormalizeSize(request.Size));

                var stage = await CardStageValidation.FindValidStageAsync(
                    card,
                    request.StageId,
                    definitions,
                    cancellationToken);
                if (stage is null)
                {
                    return Results.BadRequest(new ErrorResponse("Stage is not valid for this card workflow."));
                }

                await cards.SaveAsync(card, 0, cancellationToken);
                return Results.Created($"/api/v1/projects/{projectId}/cards/{request.CardId}", card);
            });
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
