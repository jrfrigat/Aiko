using Aiko.Application.Prioritization;
using Aiko.Domain.Cards;
using Aiko.Domain.Prioritization;
using Aiko.Domain.Workflow;
using Aiko.Infrastructure.Cards;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// Guards the rule the project owner set: a card type is project data, so the engine's own behaviour never
/// keys off a type's name.
/// </summary>
/// <remarks>
/// These specs are deliberately written with names the engine never shipped (<c>widget</c>, <c>gadget</c>): a
/// name the template happens to use would prove nothing about the engine not knowing it.
/// </remarks>
public sealed class CardTypeDataSpecs
{
    [Theory]
    [InlineData("story", "stories")]
    [InlineData("epic", "epics")]
    [InlineData("task", "tasks")]
    [InlineData("bug", "bugs")]
    [InlineData("category", "categories")]
    [InlineData("box", "boxes")]
    [InlineData("day", "days")]
    [InlineData("branch", "branches")]
    public void A_type_is_filed_under_the_plural_of_its_own_id(string kind, string expected) =>
        Assert.Equal(expected, FileCardStore.CollectionFor(kind));

    [Fact]
    public void A_card_blends_with_its_parent_only_when_its_type_says_so()
    {
        const string projectId = "project";
        var parent = MakeCard(projectId, "PARENT", "Widget", "widget", 10m);
        var blendingChild = MakeCard(projectId, "CHILD-A", "Widget", "widget", 4m);
        var keepingChild = MakeCard(projectId, "CHILD-B", "Gadget", "gadget", 4m);
        var relations = new CardRelation[]
        {
            new("r1", parent.Reference, blendingChild.Reference, RelationTypes.ParentChild, DateTimeOffset.UtcNow),
            new("r2", parent.Reference, keepingChild.Reference, RelationTypes.ParentChild, DateTimeOffset.UtcNow)
        };

        var priorities = CardPriorityProjector.Project(
            [parent, blendingChild, keepingChild],
            relations,
            PrioritySettings.SafeDefault,
            [
                new WorkflowDefinition("widget", "Widgets", [], 1, BlendsWithParent: true),
                new WorkflowDefinition("gadget", "Gadgets", [], 1)
            ]);

        // The type whose workflow declares the flag rolls its score up into the parent...
        var blended = priorities.Single(priority => priority.CardId == "CHILD-A").Snapshot;
        Assert.Equal(10m, blended.MaximumParentPriority);

        // ...and the type that does not keeps its own score, whatever it is called.
        var kept = priorities.Single(priority => priority.CardId == "CHILD-B").Snapshot;
        Assert.Null(kept.MaximumParentPriority);
        Assert.Equal(4m, kept.EffectivePriority);
    }

    private static Card MakeCard(
        string projectId,
        string cardId,
        string kind,
        string workflowId,
        decimal ownPriority) =>
        new(
            new CardReference(projectId, cardId),
            kind,
            cardId,
            workflowId,
            WorkflowDefinition.BacklogStageId,
            1,
            ownPriority,
            [],
            [],
            new Dictionary<string, string>());
}
