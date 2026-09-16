namespace Aiko.Infrastructure.Storage;

/// <summary>
/// Validation of identifiers that become file or directory names,
/// safe on every supported platform.
/// </summary>
internal static class FileSystemSafeIdentifiers
{
    private static readonly string[] ReservedWindowsNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    ];

    /// <summary>
    /// Throws <see cref="ArgumentException"/> unless the value consists of ASCII letters,
    /// digits, '-', '_' and '.', is not a reserved Windows device name and is safe as a
    /// file name (no leading or trailing dot, not "." or "..").
    /// </summary>
    /// <param name="value">Identifier to validate.</param>
    /// <param name="entityName">Entity name used in the error message, for example "card".</param>
    public static void Validate(string value, string entityName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var stem = value.Split('.')[0];
        if (value.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')) ||
            value is "." or ".." || value.StartsWith('.') || value.EndsWith('.') ||
            ReservedWindowsNames.Contains(stem, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"The {entityName} id is not file-system safe: {value}",
                entityName);
        }
    }
}
