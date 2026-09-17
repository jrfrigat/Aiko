using System.Text.Json;
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