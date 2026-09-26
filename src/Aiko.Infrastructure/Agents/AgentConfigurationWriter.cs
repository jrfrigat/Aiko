using System.Text.Json;
using System.Text.Json.Nodes;
using Aiko.Application.Agents;

namespace Aiko.Infrastructure.Agents;

/// <summary>
/// Atomic application and removal of the Aiko configuration in agent files
/// according to <see cref="AgentFileDefinition"/> descriptions: merging MCP entries
/// into JSON, managed blocks in TOML/Markdown and files owned by Aiko.
/// </summary>
internal static class AgentConfigurationWriter
{
    /// <summary>
    /// Brings a file to the desired state according to the definition kind,
    /// preserving existing user content; idempotent.
    /// </summary>
    public static async ValueTask<InstallationFileResult> ApplyAsync(
        AgentFileDefinition definition,
        CancellationToken cancellationToken)
    {
        var existed = File.Exists(definition.Path);
        var current = existed
            ? await File.ReadAllTextAsync(definition.Path, cancellationToken)
            : string.Empty;
        var desired = definition.Kind switch
        {
            AgentFileKind.JsonMcp => MergeMcpJson(
                current, definition, AgentFileKind.JsonMcp, definition.ServerKey),
            AgentFileKind.NestedJsonMcp => MergeMcpJson(
                current, definition, AgentFileKind.NestedJsonMcp, definition.ServerKey),
            AgentFileKind.FlatJsonMcp => MergeMcpJson(
                current, definition, AgentFileKind.FlatJsonMcp, definition.ServerKey),
            AgentFileKind.ManagedBlock => UpsertManagedBlock(
                current,
                definition.Content,
                Path.GetExtension(definition.Path),
                definition.ServerKey,
                definition.BlockMarkerName),
            AgentFileKind.OwnedText => CreateOwnedText(current, definition.Content, definition.OwnedMarker),
            _ => throw new InvalidOperationException($"Unsupported agent file kind: {definition.Kind}")
        };

        if (string.Equals(current, desired, StringComparison.Ordinal))
        {
            return new InstallationFileResult(
                definition.Path,
                InstallationFileStatus.Unchanged,
                null);
        }

        await WriteAtomicallyAsync(definition.Path, desired, cancellationToken);
        return new InstallationFileResult(
            definition.Path,
            existed ? InstallationFileStatus.Updated : InstallationFileStatus.Created,
            null);
    }

    /// <summary>
    /// Whether the file carries what Aiko writes into it - the whole owned file, the MCP entry or the managed
    /// block - so that removing it would change something.
    /// </summary>
    public static bool IsPresent(AgentFileDefinition definition)
    {
        if (!File.Exists(definition.Path))
        {
            return false;
        }

        var current = File.ReadAllText(definition.Path);
        return definition.Kind switch
        {
            AgentFileKind.OwnedText => current.Contains(definition.OwnedMarker, StringComparison.Ordinal),
            AgentFileKind.JsonMcp => !string.Equals(
                current, RemoveMcpJson(current, AgentFileKind.JsonMcp, definition.ServerKey), StringComparison.Ordinal),
            AgentFileKind.NestedJsonMcp => !string.Equals(
                current, RemoveMcpJson(current, AgentFileKind.NestedJsonMcp, definition.ServerKey), StringComparison.Ordinal),
            AgentFileKind.FlatJsonMcp => !string.Equals(
                current, RemoveMcpJson(current, AgentFileKind.FlatJsonMcp, definition.ServerKey), StringComparison.Ordinal),
            AgentFileKind.ManagedBlock => !string.Equals(
                current,
                RemoveManagedBlock(current, Path.GetExtension(definition.Path), definition.BlockMarkerName),
                StringComparison.Ordinal),
            _ => true
        };
    }

    /// <summary>
    /// Removes the Aiko-managed content from a file: the whole file for OwnedText
    /// (only when the ownership marker is present) or just the Aiko part otherwise.
    /// </summary>
    public static async ValueTask<InstallationFileResult> RemoveAsync(
        AgentFileDefinition definition,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(definition.Path))
        {
            return new InstallationFileResult(
                definition.Path,
                InstallationFileStatus.Unchanged,
                null);
        }

        var current = await File.ReadAllTextAsync(definition.Path, cancellationToken);
        if (definition.Kind == AgentFileKind.OwnedText)
        {
            if (!current.Contains(definition.OwnedMarker, StringComparison.Ordinal))
            {
                throw new IOException(
                    "The target file is not marked as managed by Aiko and was not removed.");
            }

            File.Delete(definition.Path);
            return new InstallationFileResult(
                definition.Path,
                InstallationFileStatus.Removed,
                null);
        }

        var desired = definition.Kind switch
        {
            AgentFileKind.JsonMcp => RemoveMcpJson(current, AgentFileKind.JsonMcp, definition.ServerKey),
            AgentFileKind.NestedJsonMcp => RemoveMcpJson(current, AgentFileKind.NestedJsonMcp, definition.ServerKey),
            AgentFileKind.FlatJsonMcp => RemoveMcpJson(current, AgentFileKind.FlatJsonMcp, definition.ServerKey),
            AgentFileKind.ManagedBlock => RemoveManagedBlock(
                current,
                Path.GetExtension(definition.Path),
                definition.BlockMarkerName),
            _ => throw new InvalidOperationException($"Unsupported agent file kind: {definition.Kind}")
        };

        if (string.Equals(current, desired, StringComparison.Ordinal))
        {
            return new InstallationFileResult(
                definition.Path,
                InstallationFileStatus.Unchanged,
                null);
        }

        await WriteAtomicallyAsync(definition.Path, desired, cancellationToken);
        return new InstallationFileResult(
            definition.Path,
            InstallationFileStatus.Updated,
            null);
    }

    /// <summary>
    /// Merges the Aiko entry into an MCP servers object, preserving everything else in the file.
    /// </summary>
    /// <remarks>
    /// The entry carries the daemon's access token: the MCP endpoint requires it, and without it the
    /// client gets 401 and never sees Aiko. Clients whose configuration cannot hold a literal header get
    /// the name of the environment variable to read instead - that is the shape the client's own CLI
    /// writes, so Aiko stays inside what the client supports.
    /// </remarks>
    private static string MergeMcpJson(
        string current,
        AgentFileDefinition definition,
        AgentFileKind kind,
        string serverKey)
    {
        var endpoint = definition.Content;
        JsonObject root;
        if (string.IsNullOrWhiteSpace(current))
        {
            root = new JsonObject();
        }
        else
        {
            root = JsonNode.Parse(
                current,
                documentOptions: new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                }) as JsonObject
                ?? throw new InvalidDataException("Agent configuration root must be a JSON object.");
        }

        var (parent, serversProperty) = ResolveServersContainer(root, kind);

        if (parent[serversProperty] is not null and not JsonObject)
        {
            throw new InvalidDataException(
                $"Agent configuration {serversProperty} must be a JSON object.");
        }

        var servers = parent[serversProperty] as JsonObject ?? new JsonObject();
        parent[serversProperty] = servers;
        var server = new JsonObject
        {
            ["url"] = endpoint
        };
        if (!string.IsNullOrWhiteSpace(definition.McpTransport))
        {
            // Clients that tag their own transport expect it - Claude Code writes "http" itself - and
            // staying byte-identical to the client's own output is what keeps this maintainable.
            server["type"] = definition.McpTransport;
        }

        if (!string.IsNullOrWhiteSpace(definition.AccessToken))
        {
            server["headers"] = new JsonObject
            {
                ["Authorization"] = $"Bearer {definition.AccessToken}"
            };
        }

        servers[serverKey] = server;

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) +
            Environment.NewLine;
    }

    private static string RemoveMcpJson(string current, AgentFileKind kind, string serverKey)
    {
        if (string.IsNullOrWhiteSpace(current))
        {
            return current;
        }

        var root = JsonNode.Parse(
            current,
            documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            }) as JsonObject
            ?? throw new InvalidDataException("Agent configuration root must be a JSON object.");
        JsonObject parent;
        string serversProperty;
        if (kind == AgentFileKind.NestedJsonMcp)
        {
            if (root["mcp"] is null)
            {
                return current;
            }

            if (root["mcp"] is not JsonObject mcp)
            {
                throw new InvalidDataException("Agent configuration mcp must be a JSON object.");
            }

            parent = mcp;
            serversProperty = "servers";
        }
        else if (kind == AgentFileKind.FlatJsonMcp)
        {
            parent = root;
            serversProperty = "mcp";
        }
        else
        {
            parent = root;
            serversProperty = "mcpServers";
        }

        if (parent[serversProperty] is null)
        {
            return current;
        }

        if (parent[serversProperty] is not JsonObject servers)
        {
            throw new InvalidDataException(
                $"Agent configuration {serversProperty} must be a JSON object.");
        }

        if (!servers.Remove(serverKey))
        {
            return current;
        }

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) +
            Environment.NewLine;
    }

    /// <summary>
    /// The object that holds the named MCP servers and the property it lives under, one branch per JSON shape
    /// Aiko writes: the root <c>mcpServers</c> object, the nested <c>mcp.servers</c> object, or the flat root
    /// <c>mcp</c> object whose entries are the server definitions themselves.
    /// </summary>
    private static (JsonObject Parent, string ServersProperty) ResolveServersContainer(
        JsonObject root,
        AgentFileKind kind) =>
        kind switch
        {
            AgentFileKind.JsonMcp => (root, "mcpServers"),
            AgentFileKind.FlatJsonMcp => (root, "mcp"),
            AgentFileKind.NestedJsonMcp => (NestedMcp(root), "servers"),
            _ => throw new InvalidOperationException($"Not a JSON MCP file kind: {kind}")
        };

    /// <summary>
    /// The root <c>mcp</c> object, created when the file has none.
    /// </summary>
    private static JsonObject NestedMcp(JsonObject root)
    {
        if (root["mcp"] is not null and not JsonObject)
        {
            throw new InvalidDataException("Agent configuration mcp must be a JSON object.");
        }

        var mcp = root["mcp"] as JsonObject ?? new JsonObject();
        root["mcp"] = mcp;
        return mcp;
    }

    private static string CreateOwnedText(string current, string content, string ownedMarker)
    {
        if (!string.IsNullOrEmpty(current) &&
            !current.Contains(ownedMarker, StringComparison.Ordinal))
        {
            throw new IOException(
                "The target file already exists and is not marked as managed by Aiko.");
        }

        return content.Trim() + Environment.NewLine + Environment.NewLine +
            ownedMarker + Environment.NewLine;
    }

    private static string UpsertManagedBlock(
        string current,
        string content,
        string extension,
        string serverKey,
        string blockMarkerName)
    {
        var commentPrefix = extension.Equals(".toml", StringComparison.OrdinalIgnoreCase)
            ? "#"
            : "<!--";
        var commentSuffix = commentPrefix == "#" ? string.Empty : " -->";
        var startMarker = $"{commentPrefix} {blockMarkerName}:begin{commentSuffix}";
        var endMarker = $"{commentPrefix} {blockMarkerName}:end{commentSuffix}";
        var block =
            startMarker + Environment.NewLine +
            content.Trim() + Environment.NewLine +
            endMarker;
        var start = current.IndexOf(startMarker, StringComparison.Ordinal);
        var end = current.IndexOf(endMarker, StringComparison.Ordinal);

        if ((start >= 0) != (end >= 0) || (start >= 0 && end < start))
        {
            throw new InvalidDataException("The existing Aiko managed block is malformed.");
        }

        if (start >= 0)
        {
            var suffixStart = end + endMarker.Length;
            return current[..start] + block + current[suffixStart..];
        }

        if (extension.Equals(".toml", StringComparison.OrdinalIgnoreCase) &&
            current.Contains($"[mcp_servers.{serverKey}]", StringComparison.Ordinal))
        {
            throw new IOException(
                "The Aiko MCP section already exists outside a managed block.");
        }

        if (string.IsNullOrWhiteSpace(current))
        {
            return block + Environment.NewLine;
        }

        return current.TrimEnd() + Environment.NewLine + Environment.NewLine +
            block + Environment.NewLine;
    }

    private static string RemoveManagedBlock(string current, string extension, string blockMarkerName)
    {
        var commentPrefix = extension.Equals(".toml", StringComparison.OrdinalIgnoreCase)
            ? "#"
            : "<!--";
        var commentSuffix = commentPrefix == "#" ? string.Empty : " -->";
        var startMarker = $"{commentPrefix} {blockMarkerName}:begin{commentSuffix}";
        var endMarker = $"{commentPrefix} {blockMarkerName}:end{commentSuffix}";
        var start = current.IndexOf(startMarker, StringComparison.Ordinal);
        var end = current.IndexOf(endMarker, StringComparison.Ordinal);

        if ((start >= 0) != (end >= 0) || (start >= 0 && end < start))
        {
            throw new InvalidDataException("The existing Aiko managed block is malformed.");
        }

        if (start < 0)
        {
            return current;
        }

        var suffixStart = end + endMarker.Length;
        while (start > 0 && (current[start - 1] == '\r' || current[start - 1] == '\n'))
        {
            start--;
        }

        while (suffixStart < current.Length &&
               (current[suffixStart] == '\r' || current[suffixStart] == '\n'))
        {
            suffixStart++;
        }

        var remaining = current[..start] + current[suffixStart..];
        return string.IsNullOrWhiteSpace(remaining)
            ? string.Empty
            : remaining.TrimEnd() + Environment.NewLine;
    }

    private static async Task WriteAtomicallyAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"Cannot resolve parent directory for {path}.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, cancellationToken);
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
}
