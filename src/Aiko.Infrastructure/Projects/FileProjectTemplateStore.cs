using System.Text.Json;
using Aiko.Application.Contracts;
using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Projects;

/// <summary>
/// Project templates on disk: one directory per template below the templates root, each holding a single
/// <c>template.json</c>.
/// </summary>
/// <remarks>
/// One document per template rather than a mirror of the <c>.aiko</c> tree. A template is read, edited and
/// copied as a whole - the defaults screens save it in one write, and an init copies it in one read - so
/// splitting it across files would buy nothing and cost an atomicity problem on every save. The directory
/// stays, because a template will later carry material that is not JSON (exported notes, sample files).
/// <para>
/// Nothing here is written unless the built-in default is missing: the store is read-only for every
/// template that exists, so a hand-edited file is authoritative over anything the code would prefer.
/// </para>
/// </remarks>
public sealed class FileProjectTemplateStore(AikoDataPaths paths) : IProjectTemplateStore
{
    private const string TemplateFileName = "template.json";

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ProjectTemplateSummary>> ListAsync(
        CancellationToken cancellationToken)
    {
        var root = paths.TemplatesRoot;
        if (!Directory.Exists(root))
        {
            return [];
        }

        var summaries = new List<ProjectTemplateSummary>();
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var template = await ReadDirectoryAsync(directory, cancellationToken);
            if (template is not null)
            {
                summaries.Add(new ProjectTemplateSummary(
                    template.Id,
                    template.Name,
                    template.Description,
                    template.Version,
                    string.Equals(template.Id, ProjectTemplate.DefaultId, StringComparison.Ordinal)));
            }
        }

        return summaries
            .OrderByDescending(summary => summary.IsDefault)
            .ThenBy(summary => summary.Id, StringComparer.Ordinal)
            .ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<ProjectTemplate> ReadAsync(string templateId, CancellationToken cancellationToken)
    {
        FileSystemSafeIdentifiers.Validate(templateId, "template");
        var directory = paths.TemplateDirectory(templateId);
        return await ReadDirectoryAsync(directory, cancellationToken)
            ?? throw new FileNotFoundException(
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

        var template = BuiltInProjectTemplate.Create();
        await WriteAsync(template, cancellationToken);
        return template;
    }

    /// <summary>
    /// Writes a template over its own file, atomically. The defaults screens save through here.
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
