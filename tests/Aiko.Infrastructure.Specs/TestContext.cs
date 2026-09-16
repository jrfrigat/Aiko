using Aiko.Application.Contracts;
using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// Infrastructure spec context: an initialized project and all stores
/// wired to an isolated test database, including the event journal.
/// </summary>
internal sealed record TestContext(
    string ProjectRoot,
    string StitchRoot,
    RegisteredProject Project,
    IProjectCatalog Catalog,
    IProjectInitializer Initializer,
    ICardStore Cards,
    IRelationStore Relations,
    IMemoryStore Memory,
    IProjectReindexer Reindexer,
    AikoDatabase Database,
    IExecutionCoordinator Executions,
    IAikoEventPublisher Events,
    IAikoEventStore EventJournal);
