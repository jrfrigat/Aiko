using Capastrum.Acp;
using Capastrum.Acp.Protocol;

namespace Aiko.Infrastructure.Agents;

/// <summary>The real ACP connection, built on <see cref="AcpAgentClient"/>.</summary>
public sealed class CapastrumAcpClientFactory : IAcpClientFactory
{
    /// <inheritdoc />
    public async Task<IAcpClientSession> StartAsync(
        AcpLaunchOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var client = await AcpAgentClient.StartAsync(
            new AcpClientOptions
            {
                Executable = options.Executable,
                Arguments = options.Arguments,
                WorkspaceRoot = options.WorkspaceRoot,
                ClientInfo = new AcpImplementation { Name = "Aiko", Version = "1.0.0" },
                OnActionConfirm = options.OnActionConfirm,
                OnSessionUpdate = notification =>
                {
                    if (notification.Text is { Length: > 0 } text)
                    {
                        options.OnText(text);
                    }
                },
                ReadTextFileAsync = async (request, token) =>
                {
                    var content = await options.ReadTextFile(request.Path, token);
                    return content is null
                        ? throw new FileNotFoundException(request.Path)
                        : new AcpReadTextFileResponse { Content = content };
                },
                WriteTextFileAsync = async (request, token) =>
                {
                    await options.WriteTextFile(request.Path, request.Content, token);
                    return new AcpWriteTextFileResponse();
                }
            },
            cancellationToken);
        return new Session(client, options.WorkspaceRoot);
    }

    private sealed class Session(AcpAgentClient client, string workspaceRoot) : IAcpClientSession
    {
        public async ValueTask<string> NewSessionAsync(
            IReadOnlyList<AcpMcpServer> mcpServers,
            CancellationToken cancellationToken)
        {
            var session = await client.NewSessionAsync(
                workspaceRoot,
                mcpServers,
                cancellationToken: cancellationToken);
            return session.SessionId;
        }

        public async ValueTask<AcpPromptResponse> SendPromptAsync(
            string sessionId,
            string prompt,
            CancellationToken cancellationToken) =>
            await client.SendPromptAsync(
                sessionId,
                [new AcpTextContent { Text = prompt }],
                cancellationToken);

        public async ValueTask CloseSessionAsync(
            string sessionId,
            CancellationToken cancellationToken) =>
            await client.CloseSessionAsync(sessionId, cancellationToken);

        public ValueTask DisposeAsync() => client.DisposeAsync();
    }
}
