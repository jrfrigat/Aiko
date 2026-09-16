using System.Text;

namespace Aiko.Infrastructure.Execution;

/// <summary>
/// <see cref="StringBuilder"/> extensions for generating handoff Markdown documents.
/// </summary>
internal static class StringBuilderExtensions
{
    /// <summary>
    /// Appends list items with a "-" marker; appends "- None" for an empty list.
    /// </summary>
    public static StringBuilder AppendLines(
        this StringBuilder builder,
        IEnumerable<string> values)
    {
        var hasValues = false;
        foreach (var value in values)
        {
            builder.AppendLine($"- {value}");
            hasValues = true;
        }

        return hasValues ? builder : builder.AppendLine("- None");
    }
}
