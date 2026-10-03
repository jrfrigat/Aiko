using Aiko.Application.Contracts;
using Aiko.Infrastructure.Execution;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// The running-scope read the work queue weighs: it reports what is truly running, with the scope its card
/// declared, so the queue and the start gate weigh the same facts.
/// </summary>
public sealed class RunningScopeSpecs
{
    [Fact]
    public async Task Only_running_executions_are_reported_with_their_declared_scope()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            var card = InfrastructureSpecs.CreateCard(context.Project.Id, "TASK-1", 1) with
            {
                DeclaredScopeFiles = ["src/Feature/**"]
            };
            await context.Cards.SaveAsync(card, 0, CancellationToken.None);

            // Nothing running yet: the read answers with nothing, so no queue entry can be flagged.
            Assert.Empty(await context.Executions.ReadRunningScopesAsync(
                context.Project.Id,
                CancellationToken.None));

            var started = await context.Executions.StartAsync(
                card.Reference,
                "analysis",
                "cline",
                CancellationToken.None);

            var running = await context.Executions.ReadRunningScopesAsync(
                context.Project.Id,
                CancellationToken.None);
            var scope = Assert.Single(running);
            Assert.Equal("TASK-1", scope.CardId);
            Assert.Equal(["src/Feature/**"], scope.DeclaredScopeFiles);

            // A run that stops holding a slot leaves the list: nothing writes files while it waits (TASK-89).
            await context.Executions.PauseAsync(started.Id, "waiting", CancellationToken.None);
            Assert.Empty(await context.Executions.ReadRunningScopesAsync(
                context.Project.Id,
                CancellationToken.None));
        });
    }
}
