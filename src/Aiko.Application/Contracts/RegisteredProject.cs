namespace Aiko.Application.Contracts;

/// <summary>
/// A project registered in the Aiko catalog.
/// </summary>
public sealed record RegisteredProject(string Id, string Name, string RootPath);
