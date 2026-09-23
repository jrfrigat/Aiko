namespace Aiko.Server.Contracts;

/// <summary>
/// A memory document to write: its relative Markdown path under <c>.aiko/memory</c> and its whole content.
/// </summary>
/// <param name="Path">Relative Markdown path, for example <c>decisions/auth.md</c>.</param>
/// <param name="Content">The complete Markdown content.</param>
public sealed record StoreMemoryRequest(string Path, string Content);
