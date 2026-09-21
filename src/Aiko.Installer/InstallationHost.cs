using Aiko.Application.Installation;
using Aiko.Infrastructure.Agents;
using Aiko.Infrastructure.Cards;
using Aiko.Infrastructure.Diagnostics;
using Aiko.Infrastructure.Execution;
using Aiko.Infrastructure.Installation;
using Aiko.Infrastructure.Projects;
using Aiko.Infrastructure.Settings;
using Aiko.Infrastructure.Storage;

namespace Aiko.Installer;

/// <summary>
/// The installer's composition root: it assembles the engine out of the pieces that already exist and adds
/// nothing of its own.
/// </summary>
/// <remarks>
/// This is the only place in the executable that knows what an installation is made of, and it is
/// deliberately the same wiring the CLI's repair uses - the database, the catalog, the reindexer, the
/// agent adapters, the doctor - because the engine's repair step performs the same work. If a second
/// assembly of these services appeared here, the installer would be installing something subtly different
/// from what the rest of the product maintains.
/// <para>
/// The release repository is named here because the installer is the program that fetches releases. The
/// bootstrap script names the same repository to find this executable in the first place; those two are the
/// only places that may, and TASK-112 keeps the script's copy.
/// </para>
/// </remarks>
internal static class InstallationHost
{
    /// <summary>Owner of the releases.</summary>
    public const string ReleaseOwner = "jrfrigat";

    /// <summary>Repository the releases are published in.</summary>
    public const string ReleaseRepository = "Aiko";

    /// <summary>
    /// Builds the engine for this machine.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async ValueTask<IInstallationEngine> CreateEngineAsync(CancellationToken cancellationToken)
    {
        var paths = AikoDataPaths.FromEnvironment();
        var database = new AikoDatabase(paths);

        // The repair and the doctor reach the projects through the catalog, which stands on the database:
        // opening it here is what keeps the first read from being the one that creates the schema.
        await database.InitializeAsync(cancellationToken);

        var catalog = new SqliteProjectCatalog(database);
        var definitions = new FileProjectDefinitionStore(catalog);
        var cards = new FileCardStore(catalog, database);
        var adapters = AgentAdapters.CreateBuiltIn();
        var agents = new UnifiedAgentInstaller(adapters, catalog, definitions);

        var repair = new InstallationRepair(catalog, new ProjectReindexer(catalog, database), agents);
        var diagnostics = new WorkshopDoctor(
            paths,
            catalog,
            agents,
            adapters,
            new DaemonEndpointConfiguration(paths),
            new AccessTokenStore(paths),
            cards,
            definitions,
            new SqliteExecutionCoordinator(
                catalog,
                cards,
                database,
                new AppSettingsService(new FileAppSettingsStore(catalog))));

        return new InstallationEngine(
            new GitHubReleaseSource(new HttpClient(), ReleaseOwner, ReleaseRepository),
            new DaemonLifecycle(paths),
            repair,
            diagnostics);
    }
}
