namespace Aiko.Application.Contracts;

/// <summary>
/// Board projection definition: view (kanban/swimlane), card-kind filter
/// and the grouping rule.
/// </summary>
public sealed record BoardProjectionDefinition(
    int SchemaVersion,
    string Id,
    string Title,
    string View,
    string? CardKind,
    string GroupBy,
    IReadOnlyDictionary<string, string> Filters);
