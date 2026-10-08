using System.Text;

namespace Aiko.Infrastructure.Memory;

/// <summary>
/// Splits a memory document into the chunks the vector index embeds.
/// </summary>
/// <remarks>
/// Sections first, paragraphs as a fallback, and never mid-sentence by character count alone. The chunk is
/// what a person is shown when a semantic hit is reported, so cutting a thought in half would produce a result
/// nobody can use even though the retrieval "worked". Splitting on headings also keeps a chunk about one thing,
/// which is what makes its vector mean something rather than average a whole document.
/// </remarks>
internal static class MemoryMarkdownChunker
{
    /// <summary>
    /// Longest chunk kept whole. Past this a section is broken at a blank line, so a document with no headings
    /// at all still yields useful pieces instead of one vector for everything it says.
    /// </summary>
    public const int MaxChunkCharacters = 1200;

    /// <summary>Breaks a document into chunks. An empty or whitespace document yields nothing.</summary>
    public static IReadOnlyList<string> Chunk(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var chunks = new List<string>();
        var current = new StringBuilder();
        foreach (var line in content.Replace("\r\n", "\n").Split('\n'))
        {
            // A heading starts a new chunk, so a section and the text under it stay together.
            if (current.Length > 0 && line.TrimStart().StartsWith('#'))
            {
                Flush(chunks, current);
            }

            // Normalise to '\n' rather than the platform's newline: a chunk is shown to a reader and hashed for
            // its row, and the same document must produce the same bytes on every machine.
            current.Append(line).Append('\n');

            // A blank line is the only safe place to break a long section: it is a seam the author put there.
            if (current.Length >= MaxChunkCharacters && string.IsNullOrWhiteSpace(line))
            {
                Flush(chunks, current);
            }
        }

        Flush(chunks, current);
        return chunks;
    }

    private static void Flush(List<string> chunks, StringBuilder builder)
    {
        var text = builder.ToString().Trim();
        builder.Clear();
        if (text.Length > 0)
        {
            chunks.Add(text);
        }
    }
}
