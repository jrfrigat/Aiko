using Aiko.Domain.Cards;
using Xunit;

namespace Aiko.Infrastructure.Specs;

public sealed class AgentInstanceHistorySpecs
{
    [Fact]
    public async Task Starting_a_stage_records_the_configured_instance()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            var card = await SaveCardAsync(context);

            var execution = await context.Executions.StartAsync(
                card.Reference,
                "implementation",
                "claude-code",
                "claude-code-deepseek",
                CancellationToken.None);

            var attempt = Assert.Single(execution.Attempts);
            Assert.Equal("claude-code", attempt.AgentAdapterId);
            Assert.Equal("claude-code-deepseek", attempt.AgentInstanceId);
        });
    }

    [Fact]
    public async Task Starting_without_an_instance_records_none()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            var card = await SaveCardAsync(context);

            var execution = await context.Executions.StartAsync(
                card.Reference,
                "implementation",
                "claude-code",
                CancellationToken.None);

            var attempt = Assert.Single(execution.Attempts);
            Assert.Equal("claude-code", attempt.AgentAdapterId);
            Assert.Null(attempt.AgentInstanceId);
        });
    }

    [Fact]
    public async Task The_instance_is_read_back_with_the_execution()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            var card = await SaveCardAsync(context);
            var started = await context.Executions.StartAsync(
                card.Reference,
                "implementation",
                "claude-code",
                "claude-code-deepseek",
                CancellationToken.None);

            var found = await context.Executions.FindAsync(started.Id, CancellationToken.None);

            Assert.Equal("claude-code-deepseek", Assert.Single(found!.Attempts).AgentInstanceId);
        });
    }

    private static async Task<Card> SaveCardAsync(TestContext context)
    {
        var card = InfrastructureSpecs.CreateCard(context.Project.Id, "TASK-INSTANCE", 1);
        await context.Cards.SaveAsync(card, 0, CancellationToken.None);
        return card;
    }
}
