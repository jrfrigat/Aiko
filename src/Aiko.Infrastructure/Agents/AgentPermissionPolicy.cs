using Capastrum.Acp;

namespace Aiko.Infrastructure.Agents;

/// <summary>
/// Decides an ACP permission request by confining its paths to the project root. The request carries a
/// terminal command rather than a structured path, so the decision is deliberately conservative: a token
/// that looks like a path outside the root is denied.
/// </summary>
public static class AgentPermissionPolicy
{
    /// <summary>Allows work inside the project and denies a path that leaves it.</summary>
    public static ConfirmationResult Decide(string projectRoot, string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return ConfirmationResult.Allowed;
        }

        foreach (var token in command.Split(
                     [' ', '\t', '\r', '\n', '='],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = token.Trim('"', '\'');
            if (candidate.Length == 0 ||
                (!candidate.Contains("..", StringComparison.Ordinal) &&
                 !Path.IsPathRooted(candidate)))
            {
                continue;
            }

            try
            {
                var full = Path.GetFullPath(candidate, projectRoot);
                if (!IsInside(full, projectRoot))
                {
                    return ConfirmationResult.Denied;
                }
            }
            catch
            {
                return ConfirmationResult.Denied;
            }
        }

        return ConfirmationResult.Allowed;
    }

    private static bool IsInside(string path, string root)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return string.Equals(path, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(
                   normalizedRoot + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }
}
