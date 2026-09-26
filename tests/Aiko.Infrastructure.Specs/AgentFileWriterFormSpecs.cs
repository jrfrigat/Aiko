using Aiko.Application.Agents;
using Aiko.Infrastructure.Agents;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// The three JSON shapes the writer knows for an MCP entry: the root <c>mcpServers</c> object, the nested
/// <c>mcp.servers</c> object and the flat root <c>mcp</c> object OpenCode writes. The writer resolves the
/// container from the file kind, so each shape is checked here against the same merge, idempotency and
/// removal rules rather than one shape being proven and the others assumed.
/// </summary>
public sealed class AgentFileWriterFormSpecs
{
    [Fact]
    public async Task The_flat_mcp_form_writes_its_entry_and_keeps_the_rest()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "opencode.json");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "$schema": "https://opencode.ai/config.json",
              "mcp": {
                "other": { "type": "local", "command": ["other"] }
              }
            }
            """);

        var definition = AgentFileDefinition.FlatJsonMcp(
            path,
            "Merge the project's Aiko MCP server.",
            "http://127.0.0.1:24560/mcp/projects/aiko",
            "test-token",
            mcpTransport: "remote");

        var applied = await AgentConfigurationWriter.ApplyAsync(definition, CancellationToken.None);
        Assert.Equal(InstallationFileStatus.Updated, applied.Status);

        var written = await File.ReadAllTextAsync(path);
        // A field the client owns and a server somebody else declared both survive the merge.
        Assert.Contains("\"$schema\"", written, StringComparison.Ordinal);
        Assert.Contains("\"other\"", written, StringComparison.Ordinal);
        Assert.Contains("\"type\": \"local\"", written, StringComparison.Ordinal);
        // Ours lands in the flat mcp object with the transport and the token.
        Assert.Contains("\"aiko\":", written, StringComparison.Ordinal);
        Assert.Contains("\"type\": \"remote\"", written, StringComparison.Ordinal);
        Assert.Contains("Bearer test-token", written, StringComparison.Ordinal);
        Assert.True(AgentConfigurationWriter.IsPresent(definition));

        // A second run changes nothing.
        var again = await AgentConfigurationWriter.ApplyAsync(definition, CancellationToken.None);
        Assert.Equal(InstallationFileStatus.Unchanged, again.Status);

        var removed = await AgentConfigurationWriter.RemoveAsync(definition, CancellationToken.None);
        Assert.Equal(InstallationFileStatus.Updated, removed.Status);

        var afterRemoval = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain("Bearer test-token", afterRemoval, StringComparison.Ordinal);
        Assert.Contains("\"other\"", afterRemoval, StringComparison.Ordinal);
        Assert.False(AgentConfigurationWriter.IsPresent(definition));
    }

    [Fact]
    public async Task The_older_json_forms_keep_their_own_containers()
    {
        using var directory = new TemporaryDirectory();

        var rootPath = Path.Combine(directory.Path, "root.json");
        var root = AgentFileDefinition.JsonMcp(
            rootPath, "root mcpServers", "http://127.0.0.1:24560/mcp/projects/aiko", "test-token");
        await AgentConfigurationWriter.ApplyAsync(root, CancellationToken.None);
        var rootWritten = await File.ReadAllTextAsync(rootPath);
        Assert.Contains("\"mcpServers\"", rootWritten, StringComparison.Ordinal);
        Assert.DoesNotContain("\"mcp\":", rootWritten, StringComparison.Ordinal);

        var nestedPath = Path.Combine(directory.Path, "nested.json");
        var nested = AgentFileDefinition.NestedJsonMcp(
            nestedPath, "nested mcp.servers", "http://127.0.0.1:24560/mcp/projects/aiko", "test-token");
        await AgentConfigurationWriter.ApplyAsync(nested, CancellationToken.None);
        var nestedWritten = await File.ReadAllTextAsync(nestedPath);
        Assert.Contains("\"mcp\"", nestedWritten, StringComparison.Ordinal);
        Assert.Contains("\"servers\"", nestedWritten, StringComparison.Ordinal);
        Assert.DoesNotContain("\"mcpServers\"", nestedWritten, StringComparison.Ordinal);
    }

    /// <summary>A throwaway directory deleted with the spec, so the shapes are checked on real files.</summary>
    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = Directory.CreateTempSubdirectory("aiko-agent-forms-").FullName;

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp directory is not a spec failure.
            }
        }
    }
}
