using Aiko.Application.Contracts;
using Aiko.Domain.Execution;
using Aiko.Server.Contracts;

namespace Aiko.Server.Endpoints;

/// <summary>
/// The command queue over HTTP: placing a command a screen asks for, reading the queue, and the transitions
/// an agent reports back.
/// </summary>
/// <remarks>
/// Every path translates an <see cref="ICardCommandStore"/> call, so the queue's rules - what each action
/// needs, and which transitions are allowed - stay in the store and the domain, where the MCP tools and the
/// CLI read them from too. This is the half of the feature Aiko can do itself: the daemon records what a
/// person asked for and never pretends to have run the agent, which is what makes a command survive until
/// the next time an agent starts.
/// </remarks>
internal static class CommandEndpoints
{
    /// <summary>
    /// Maps the command routes.
    /// </summary>
    public static void MapCommandEndpoints(this IEndpointRouteBuilder app)
    {
        // The queue itself. `state` narrows it: `open` (the default) is what is still asked for, `all` is
        // the history too, and a state name is that state alone - which is how a screen shows "what is
        // waiting" without also showing everything that ever happened.
        app.MapGet(
            "/api/v1/projects/{projectId}/commands",
            async (
                string projectId,
                string? state,
                ICardCommandStore commands,
                CancellationToken cancellationToken) =>
            {
                var filter = CommandFilter.Parse(state);
                var entries = await commands.ListAsync(projectId, filter.IncludeClosed, cancellationToken);
                return Results.Ok(filter.Apply(entries));
            });
        app.MapPost(
            "/api/v1/projects/{projectId}/commands",
            async (
                string projectId,
                PlaceCommandRequest request,
                ICardCommandStore commands,
                CancellationToken cancellationToken) =>
                // Placing goes through the same ladder as the transitions: a refusal here - the board is
                // already being worked - is a conflict, and only this route knows which route that answer
                // describes, so it cannot be left to the global mapper (which would call it a 500).
                await RunAsync(
                    async () => await commands.PlaceAsync(projectId, request, cancellationToken),
                    command => Results.Created(
                        $"/api/v1/projects/{projectId}/commands/{command.Id}",
                        command)));
        app.MapGet(
            "/api/v1/projects/{projectId}/commands/{commandId}",
            async (
                string projectId,
                string commandId,
                ICardCommandStore commands,
                CancellationToken cancellationToken) =>
            {
                var command = await commands.FindAsync(projectId, commandId, cancellationToken);
                return command is null
                    ? Results.NotFound()
                    : Results.Ok(command);
            });
        app.MapPost(
            "/api/v1/projects/{projectId}/commands/{commandId}/claim",
            async (
                string projectId,
                string commandId,
                ClaimCommandRequest request,
                ICardCommandStore commands,
                CancellationToken cancellationToken) =>
                await RunAsync(async () => await commands.ClaimAsync(
                    projectId,
                    commandId,
                    request.AgentAdapterId,
                    cancellationToken)));
        app.MapPost(
            "/api/v1/projects/{projectId}/commands/{commandId}/complete",
            async (
                string projectId,
                string commandId,
                CommandReportRequest request,
                ICardCommandStore commands,
                CancellationToken cancellationToken) =>
                await RunAsync(async () => await commands.FinishAsync(
                    projectId,
                    commandId,
                    CardCommandState.Completed,
                    request.Message,
                    cancellationToken)));
        app.MapPost(
            "/api/v1/projects/{projectId}/commands/{commandId}/fail",
            async (
                string projectId,
                string commandId,
                CommandReportRequest request,
                ICardCommandStore commands,
                CancellationToken cancellationToken) =>
                await RunAsync(async () => await commands.FinishAsync(
                    projectId,
                    commandId,
                    CardCommandState.Failed,
                    request.Message,
                    cancellationToken)));
        app.MapPost(
            "/api/v1/projects/{projectId}/commands/{commandId}/cancel",
            async (
                string projectId,
                string commandId,
                CancelCommandRequest request,
                ICardCommandStore commands,
                CancellationToken cancellationToken) =>
                await RunAsync(async () => await commands.CancelAsync(
                    projectId,
                    commandId,
                    request.Reason,
                    cancellationToken)));
    }

    /// <summary>
    /// Runs a store call and turns its outcome into a response: something that is not there is a 404, a
    /// refusal the store names is a 409 carrying that text, bad input is a 400, and success is whatever the
    /// caller asked for - a placed command is a 201 with its address, everything else is the command itself.
    /// </summary>
    private static async Task<IResult> RunAsync(
        Func<Task<CardCommand>> action,
        Func<CardCommand, IResult>? onSuccess = null)
    {
        try
        {
            var command = await action();
            return onSuccess is null
                ? Results.Ok(command)
                : onSuccess(command);
        }
        catch (FileNotFoundException exception)
        {
            return Results.NotFound(new ErrorResponse(exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            // A transition the queue does not allow - a command already taken, or one an agent holds being
            // withdrawn. Its own words are what the caller needs, so they travel untouched.
            return Results.Conflict(new ErrorResponse(exception.Message));
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new ErrorResponse(exception.Message));
        }
    }

    /// <summary>
    /// What a caller asked to see of the queue.
    /// </summary>
    /// <param name="IncludeClosed">True when closed commands are part of the answer.</param>
    /// <param name="State">One state to keep, or null for every state the caller asked for.</param>
    private readonly record struct CommandFilter(bool IncludeClosed, CardCommandState? State)
    {
        /// <summary>
        /// Reads the <c>state</c> query value: <c>open</c>, <c>all</c>, or the name of one state.
        /// </summary>
        public static CommandFilter Parse(string? value) =>
            string.IsNullOrWhiteSpace(value) || StringComparer.OrdinalIgnoreCase.Equals(value.Trim(), "open")
                ? new CommandFilter(false, null)
                : StringComparer.OrdinalIgnoreCase.Equals(value.Trim(), "all")
                    ? new CommandFilter(true, null)
                    : new CommandFilter(true, CardCommands.ParseState(value));

        /// <summary>
        /// Keeps what the caller asked for. The open queue is already filtered by the store, so only a
        /// single-state answer is narrowed here.
        /// </summary>
        public IReadOnlyList<CardCommand> Apply(IReadOnlyList<CardCommand> entries) =>
            State is { } state
                ? [.. entries.Where(entry => entry.State == state)]
                : entries;
    }
}
