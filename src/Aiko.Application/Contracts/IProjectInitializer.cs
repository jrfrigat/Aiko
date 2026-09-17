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
    /// <param name="request">Where the project is and what it starts as.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <exception cref="InvalidOperationException">
    /// The requested slug already belongs to another project. A slug Aiko derived itself is made unique
    /// instead, so this only happens for a value the caller typed.
    /// </exception>
    ValueTask<RegisteredProject> InitializeAsync(
        InitializeProjectRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gives a readable handle to every registered project that has none, and returns how many were assigned.
    /// </summary>
    /// <remarks>
    /// The upgrade path for installations that predate slugs: the daemon runs it at startup and
    /// <c>aiko repair --fix</c> runs it too, so an existing project's address becomes readable without anyone
    /// re-registering it. A handle already stated in the project's manifest wins, so deleting the database
    /// does not move every link to a new address.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    ValueTask<int> EnsureSlugsAsync(CancellationToken cancellationToken);
}
