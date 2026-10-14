namespace Aiko.Application.Contracts;

/// <summary>Creates the .aiko structure, the default registries, and the catalog entry.</summary>
public interface IProjectInitializer
{
    /// <summary>Idempotently initializes a project at <see cref="InitializeProjectRequest.RootPath"/> and
    /// returns its registration.</summary>
    /// <exception cref="InvalidOperationException">The requested slug already belongs to another project. A
    /// slug Aiko derived itself is made unique instead, so this is a value the caller typed.</exception>
    ValueTask<RegisteredProject> InitializeAsync(
        InitializeProjectRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gives a readable handle to every registered project that has none, and returns how many were assigned -
    /// the upgrade path for installations that predate slugs, run by the daemon at startup and by
    /// <c>aiko repair --fix</c>. A handle already stated in the project's manifest wins.
    /// </summary>
    ValueTask<int> EnsureSlugsAsync(CancellationToken cancellationToken);
}
