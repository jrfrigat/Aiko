using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Aiko.Application.Contracts;
using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Projects;

/// <summary>
/// Project templates: one file per template below the templates root, plus the base template that ships
/// with Aiko.
/// </summary>
/// <remarks>
/// One document per template rather than a mirror of the <c>.aiko</c> tree. A template is read, edited and
/// copied as a whole - the defaults screen saves it in one write, and an init copies it in one read - so
/// splitting it across files would buy nothing and cost an atomicity problem on every save.
/// <para>
/// <b>The base template is not a file.</b> Every installation has it because it is compiled into the
/// daemon: a fresh install has an empty templates root and still creates projects, and an upgrade can
/// improve the base without touching anything a person edited. Changing it means copying it first
/// (<see cref="CopyAsync"/>); a file with the base id in the templates root replaces the shipped one, which
/// is the escape hatch for an installation that wants to pin its own base by hand.
/// </para>
/// </remarks>
public sealed class FileProjectTemplateStore(AikoDataPaths paths) : IProjectTemplateStore
{
    private const string TemplateFileName = "template.json";

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ProjectTemplateSummary>> ListAsync(
        CancellationToken cancellationToken)
    {
        var summaries = new List<ProjectTemplateSummary>();

        // The shipped base first: it exists for every installation, and stops being the one in use only when
        // a file with its id replaces it.
        var overriding = await ReadDirectoryAsync(
            paths.TemplateDirectory(ProjectTemplate.DefaultId),
            cancellationToken);
        if (overriding is null)
        {
            summaries.Add(Summary(BuiltInProjectTemplate.Create(), isBuiltIn: true));
        }

        if (Directory.Exists(paths.TemplatesRoot))
        {
            foreach (var directory in Directory.EnumerateDirectories(paths.TemplatesRoot))
            {
                var template = await ReadDirectoryAsync(directory, cancellationToken);
                if (template is not null)
                {
                    summaries.Add(Summary(template, isBuiltIn: false));
                }
            }
        }

        return summaries
            .OrderByDescending(summary => summary.IsBuiltIn)
            .ThenBy(summary => summary.Id, StringComparer.Ordinal)
            .ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<ProjectTemplate> ReadAsync(string templateId, CancellationToken cancellationToken)
    {
        FileSystemSafeIdentifiers.Validate(templateId, "template");
        var directory = paths.TemplateDirectory(templateId);
        var stored = await ReadDirectoryAsync(directory, cancellationToken);
        if (stored is not null)
        {
            return stored;
        }

        if (string.Equals(templateId, ProjectTemplate.DefaultId, StringComparison.Ordinal))
        {
            return BuiltInProjectTemplate.Create();
        }

        throw new FileNotFoundException(
            $"No Aiko project template '{templateId}' below {paths.TemplatesRoot}.",
            Path.Combine(directory, TemplateFileName));
    }

    /// <inheritdoc />
    public async ValueTask<ProjectTemplate> EnsureDefaultAsync(CancellationToken cancellationToken)
    {
        var directory = paths.TemplateDirectory(ProjectTemplate.DefaultId);
        var existing = await ReadDirectoryAsync(directory, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        // A fresh installation gets the base as a real file: the install script ships one, and this writes it
        // for a source build that has nothing shipped. Either way the file is the template from then on, so a
        // person can read it, and an installation that edits it keeps its copy.
        var template = BuiltInProjectTemplate.Create();
        await WriteAsync(template, cancellationToken);
        return template;
    }

    /// <inheritdoc />
    public async ValueTask<ProjectTemplate> CopyAsync(
        string sourceId,
        string templateId,
        string? name,
        CancellationToken cancellationToken)
    {
        FileSystemSafeIdentifiers.Validate(templateId, "template");
        if (Directory.Exists(paths.TemplateDirectory(templateId)))
        {
            throw new IOException($"A template '{templateId}' already exists.");
        }

        var source = await ReadAsync(sourceId, cancellationToken);
        var copy = source with
        {
            Id = templateId,
            Name = string.IsNullOrWhiteSpace(name) ? $"{source.Name} copy" : name.Trim(),
            Description = $"Copied from {source.Name}.",
            // A copy is a new template with its own history: it starts at its first version, because the
            // version it was copied from belongs to the template it came from.
            Version = 1,
        };
        await WriteAsync(copy, cancellationToken);
        return copy;
    }

    /// <inheritdoc />
    public async ValueTask<ProjectTemplate> CreateFromProjectAsync(
        string projectRootPath,
        string templateId,
        string? name,
        CancellationToken cancellationToken)
    {
        FileSystemSafeIdentifiers.Validate(templateId, "template");
        if (Directory.Exists(paths.TemplateDirectory(templateId)))
        {
            throw new IOException($"A template '{templateId}' already exists.");
        }

        var dataRoot = AikoProjectPaths.DataRoot(Path.GetFullPath(projectRootPath));
        if (!Directory.Exists(dataRoot))
        {
            throw new DirectoryNotFoundException($"No Aiko project below {projectRootPath}.");
        }

        // The manifest is what the project records about itself: its name for the template's, and the git
        // policy it was created with.
        var manifest = await ReadProjectDocumentAsync(
            Path.Combine(dataRoot, "project.json"),
            ProjectJsonContext.Default.ProjectManifest,
            cancellationToken);
        var settings = await ReadProjectDocumentAsync(
            Path.Combine(dataRoot, "settings.json"),
            ProjectJsonContext.Default.AppSettings,
            cancellationToken);

        var template = new ProjectTemplate(
            templateId,
            string.IsNullOrWhiteSpace(name)
                ? $"{ProjectNameOf(manifest, projectRootPath)} template"
                : name.Trim(),
            $"Copied from project {ProjectNameOf(manifest, projectRootPath)}.",
            1,
            settings,
            await ReadProjectCatalogAsync(
                Path.Combine(dataRoot, "workflows"),
                ProjectJsonContext.Default.WorkflowDefinition,
                cancellationToken),
            await ReadProjectCatalogAsync(
                Path.Combine(dataRoot, "projections"),
                ProjectJsonContext.Default.BoardProjectionDefinition,
                cancellationToken),
            await ReadMemoryAsync(Path.Combine(dataRoot, "memory"), cancellationToken),
            manifest?.GitPolicy ?? ProjectGitPolicy.LocalOnly);

        await WriteAsync(template, cancellationToken);
        return template;
    }

    /// <inheritdoc />
    public async ValueTask ExportAsync(string templateId, string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var template = await ReadAsync(templateId, cancellationToken);
        var target = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await using var output = new FileStream(
            target,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(
            output,
            template,
            ProjectJsonContext.Default.ProjectTemplate,
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<ProjectTemplate> ImportAsync(
        string path,
        string? templateId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var source = Path.GetFullPath(path);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException($"No template file at {path}.", source);
        }

        ProjectTemplate? imported;
        await using (var input = File.OpenRead(source))
        {
            imported = await JsonSerializer.DeserializeAsync(
                input,
                ProjectJsonContext.Default.ProjectTemplate,
                cancellationToken);
        }

        if (imported is null || string.IsNullOrWhiteSpace(imported.Id))
        {
            throw new InvalidDataException($"Invalid Aiko project template: {source}");
        }

        var id = string.IsNullOrWhiteSpace(templateId) ? imported.Id : templateId.Trim();
        FileSystemSafeIdentifiers.Validate(id, "template");
        if (Directory.Exists(paths.TemplateDirectory(id)))
        {
            throw new IOException($"A template '{id}' already exists.");
        }

        // An imported template is a new template in this installation: it keeps its content and its version,
        // and takes the id it is filed under.
        var stored = imported with
        {
            Id = id,
            Name = string.Equals(id, imported.Id, StringComparison.Ordinal)
                ? imported.Name
                : $"{imported.Name} ({id})"
        };
        await WriteAsync(stored, cancellationToken);
        return stored;
    }

    /// <inheritdoc />
    public ValueTask<bool> DeleteAsync(string templateId, CancellationToken cancellationToken)
    {
        FileSystemSafeIdentifiers.Validate(templateId, "template");
        var directory = paths.TemplateDirectory(templateId);
        if (!Directory.Exists(directory))
        {
            return ValueTask.FromResult(false);
        }

        Directory.Delete(directory, recursive: true);
        return ValueTask.FromResult(true);
    }

    /// <summary>The name a template built from this project carries: the project's, or its directory's.</summary>
    private static string ProjectNameOf(ProjectManifest? manifest, string projectRootPath) =>
        manifest?.Name is { Length: > 0 } name
            ? name
            : Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRootPath)));

    /// <summary>
    /// Reads one JSON document of a project, or null when the project does not carry it. A project is read
    /// as it is: a template built from one is a snapshot, and a missing section is a section the project
    /// never had.
    /// </summary>
    private static async ValueTask<T?> ReadProjectDocumentAsync<T>(
        string path,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
        where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using var input = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(input, typeInfo, cancellationToken);
    }

    /// <summary>Reads one directory of a project's JSON documents, in file-name order.</summary>
    private static async ValueTask<IReadOnlyList<T>> ReadProjectCatalogAsync<T>(
        string directory,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
        where T : class
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var documents = new List<T>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json")
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var document = await ReadProjectDocumentAsync(file, typeInfo, cancellationToken);
            if (document is not null)
            {
                documents.Add(document);
            }
        }

        return documents;
    }

    /// <summary>Reads a project's memory directory as a template's starting documents.</summary>
    private static async ValueTask<IReadOnlyList<TemplateDocument>> ReadMemoryAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var documents = new List<TemplateDocument>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.md", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var content = await File.ReadAllTextAsync(file, cancellationToken);
            documents.Add(new TemplateDocument(
                Path.GetRelativePath(directory, file).Replace('\\', '/'),
                content));
        }

        return documents;
    }

    /// <summary>One summary line for a template.</summary>
    private static ProjectTemplateSummary Summary(ProjectTemplate template, bool isBuiltIn) => new(
        template.Id,
        template.Name,
        template.Description,
        template.Version,
        string.Equals(template.Id, ProjectTemplate.DefaultId, StringComparison.Ordinal),
        isBuiltIn);

    /// <summary>
    /// Writes a template over its own file, atomically. The defaults screen saves through here.
    /// </summary>
    /// <param name="template">The template to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask WriteAsync(ProjectTemplate template, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(template);
        FileSystemSafeIdentifiers.Validate(template.Id, "template");

        var directory = paths.TemplateDirectory(template.Id);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, TemplateFileName);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(
                    output,
                    template,
                    ProjectJsonContext.Default.ProjectTemplate,
                    cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    /// <summary>
    /// Reads one template directory, or null when it holds no template file. A file that exists but cannot
    /// be read is a failure rather than a missing template: silently skipping it would hide a broken
    /// template behind "the template is not there".
    /// </summary>
    private static async ValueTask<ProjectTemplate?> ReadDirectoryAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, TemplateFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var input = File.OpenRead(path);
        var template = await JsonSerializer.DeserializeAsync(
            input,
            ProjectJsonContext.Default.ProjectTemplate,
            cancellationToken);

        if (template is null || string.IsNullOrWhiteSpace(template.Id))
        {
            throw new InvalidDataException($"Invalid Aiko project template: {path}");
        }

        return template;
    }
}