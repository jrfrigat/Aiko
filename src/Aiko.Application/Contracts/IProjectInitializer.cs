namespace Aiko.Application.Contracts;

/// <summary>
/// Project initializer: creates the .aiko structure, default registries
/// and registers the project in the catalog.
/// </summary>
public interface IProjectInitializer
{
    /// <summary>
    /// Idempotently initializes a project at <see cref="InitializeProjectRequest.RootPath"/>
    /// and returns its registration.
    /// </summary>
    ValueTask<RegisteredProject> InitializeAsync(
        InitializeProjectRequest request,
        CancellationToken cancellationToken);
}
