using System.Diagnostics;
using System.Text.Json.Nodes;
using Aiko.Infrastructure.Memory;
using Aiko.Infrastructure.Projects;
using Aiko.Infrastructure.Storage;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// Identifiers read from .aiko become paths, and a repository anyone can commit to is not a trusted source: a kind
/// or a workflow id that climbs out, or a junction inside the project, used to carry a write out of the project.
/// </summary>
public sealed class PathConfinementSpecs
{
    [Fact]
    public void A_path_that_climbs_out_or_is_elsewhere_is_refused()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aiko-confine-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            Assert.Equal(Path.Combine(root, "a", "b.md"), PathConfinement.Resolve(root, Path.Combine("a", "b.md")));
            Assert.Throws<ArgumentException>(() => PathConfinement.Resolve(root, Path.Combine("..", "escape.md")));
            Assert.Throws<ArgumentException>(() => PathConfinement.Resolve(root, Path.GetTempPath()));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_workflow_whose_identifiers_are_not_names_is_refused_with_its_file()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            var workflows = Path.Combine(context.StitchRoot, "workflows");
            var workflow = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(workflows, "task.json")))!;
            workflow["id"] = "escape";
            workflow["stages"]![0]!["allowedCardKinds"] = new JsonArray("..\\..\\outside");
            await File.WriteAllTextAsync(Path.Combine(workflows, "escape.json"), workflow.ToJsonString());

            var refused = await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await new FileProjectDefinitionStore(context.Catalog).ReadAsync(context.Project.Id, CancellationToken.None));
            Assert.Contains("escape.json", refused.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task A_card_whose_kind_climbs_out_is_refused_and_writes_nothing_outside()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            var card = InfrastructureSpecs.CreateCard(context.Project.Id, "TASK-OUT", 1) with { Kind = "../../../outside" };

            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await context.Cards.SaveAsync(card, 0, CancellationToken.None));
            Assert.False(Directory.Exists(Path.GetFullPath(Path.Combine(context.StitchRoot, "workflows", "../../../outside"))));
        });
    }

    [Fact]
    public async Task Memory_behind_a_junction_is_refused()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            var outside = Path.Combine(Path.GetTempPath(), $"aiko-outside-{Guid.NewGuid():N}");
            Directory.CreateDirectory(outside);
            var memory = Path.Combine(context.StitchRoot, "memory");
            Directory.CreateDirectory(memory);
            try
            {
                using (var link = Process.Start(new ProcessStartInfo("cmd", $"/c mklink /J \"{Path.Combine(memory, "linked")}\" \"{outside}\"")
                       {
                           UseShellExecute = false,
                           CreateNoWindow = true,
                           RedirectStandardOutput = true
                       })!)
                {
                    await link.WaitForExitAsync();
                    Assert.Equal(0, link.ExitCode);
                }

                await Assert.ThrowsAsync<ArgumentException>(async () =>
                    await context.Memory.StoreAsync(context.Project.Id, "linked/note.md", "# Out", CancellationToken.None));
                Assert.Empty(Directory.EnumerateFiles(outside));
            }
            finally
            {
                // The junction goes first, so deleting the project cannot follow it into the outside directory.
                var junction = Path.Combine(memory, "linked");
                if (Directory.Exists(junction))
                {
                    Directory.Delete(junction);
                }

                Directory.Delete(outside, recursive: true);
            }
        });
    }
}
