namespace Aiko.Domain.Execution;

/// <summary>
/// Matches file paths against declared-scope glob patterns.
/// Supports <c>*</c> and <c>?</c> inside a path segment and <c>**</c> spanning any number of segments.
/// </summary>
public static class ScopeMatcher
{
    /// <summary>
    /// Checks whether path <paramref name="path"/> matches pattern <paramref name="pattern"/>.
    /// Segment separators are normalized; comparison is case-insensitive on Windows.
    /// </summary>
    public static bool Matches(string pattern, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var patternSegments = Normalize(pattern);
        var pathSegments = Normalize(path);
        var memo = new Dictionary<(int Pattern, int Path), bool>();
        return Match(0, 0);

        bool Match(int patternIndex, int pathIndex)
        {
            if (memo.TryGetValue((patternIndex, pathIndex), out var known))
            {
                return known;
            }

            bool result;
            if (patternIndex == patternSegments.Length)
            {
                result = pathIndex == pathSegments.Length;
            }
            else if (patternSegments[patternIndex] == "**")
            {
                result = Match(patternIndex + 1, pathIndex) ||
                    (pathIndex < pathSegments.Length && Match(patternIndex, pathIndex + 1));
            }
            else
            {
                result = pathIndex < pathSegments.Length &&
                    System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(
                        patternSegments[patternIndex],
                        pathSegments[pathIndex],
                        OperatingSystem.IsWindows()) &&
                    Match(patternIndex + 1, pathIndex + 1);
            }

            memo[(patternIndex, pathIndex)] = result;
            return result;
        }
    }

    /// <summary>
    /// Checks whether two glob patterns can match the same path. Conservative: returns true
    /// unless a literal segment differs at the same position, which guarantees disjointness.
    /// </summary>
    public static bool Overlaps(string patternA, string patternB)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(patternA);
        ArgumentException.ThrowIfNullOrWhiteSpace(patternB);
        var a = Normalize(patternA);
        var b = Normalize(patternB);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        for (var index = 0; index < Math.Min(a.Length, b.Length); index++)
        {
            var segmentA = a[index];
            var segmentB = b[index];
            if (segmentA == "**" || segmentB == "**")
            {
                return true;
            }

            if (IsLiteral(segmentA) && IsLiteral(segmentB) &&
                !string.Equals(segmentA, segmentB, comparison))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLiteral(string segment) =>
        segment.IndexOf('*') < 0 && segment.IndexOf('?') < 0;

    private static string[] Normalize(string value) =>
        value
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
