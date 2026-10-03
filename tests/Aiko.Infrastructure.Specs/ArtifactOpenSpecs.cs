using Aiko.Infrastructure.Cards;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// Opening an artifact hands a file to another program, so it resolves the path exactly as the store
/// does: a path that climbs out of the card's folder, is absolute, or is not Markdown never becomes a
/// file an open could reach.
/// </summary>
public sealed class ArtifactOpenSpecs
{
    [Fact]
    public void An_artifact_path_that_leaves_the_card_folder_is_refused()
    {
        var card = Path.Combine(Path.GetTempPath(), $"aiko-card-{Guid.NewGuid():N}");
        Directory.CreateDirectory(card);
        try
        {
            Assert.Equal(
                Path.GetFullPath(Path.Combine(card, "notes", "analysis.md")),
                CardArtifactPaths.ResolveArtifactPath(card, "notes/analysis.md"));
            Assert.Equal(
                Path.GetFullPath(Path.Combine(card, "analysis.md")),
                CardArtifactPaths.ResolveArtifactPath(card, "analysis.md"));

            // A climb, an absolute path and a non-Markdown file are all refused before anything is opened.
            Assert.Throws<ArgumentException>(() => CardArtifactPaths.ResolveArtifactPath(card, "../escape.md"));
            Assert.Throws<ArgumentException>(() => CardArtifactPaths.ResolveArtifactPath(card, "..\\escape.md"));
            Assert.Throws<ArgumentException>(() => CardArtifactPaths.ResolveArtifactPath(card, Path.GetTempPath()));
            Assert.Throws<ArgumentException>(() => CardArtifactPaths.ResolveArtifactPath(card, "notes.txt"));
        }
        finally
        {
            Directory.Delete(card, recursive: true);
        }
    }

    [Fact]
    public void A_relative_path_is_normalised_before_it_is_used()
    {
        Assert.Equal("a/b.md", CardArtifactPaths.NormalizeRelativePath("a\\b.md"));
        // An absolute path is refused however it is written, and so is any step that is not a file name.
        Assert.Throws<ArgumentException>(() => CardArtifactPaths.NormalizeRelativePath("/a/b.md"));
        Assert.Throws<ArgumentException>(() => CardArtifactPaths.NormalizeRelativePath("a/./b.md"));
        Assert.Throws<ArgumentException>(() => CardArtifactPaths.NormalizeRelativePath("a/../b.md"));
    }
}
