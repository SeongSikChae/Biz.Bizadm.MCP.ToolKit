using System.Text.Json;
using System.Threading.Channels;

using Biz.Bizadm.MCP.ToolKit.Jenkins;

using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Biz.Bizadm.MCP.ToolKit.Tests.Jenkins;

/// <summary>메모리 안에서 서로 메시지를 주고받는 ITransport 한 쪽</summary>
internal sealed class ChannelTransport(Channel<JsonRpcMessage> incoming, ChannelWriter<JsonRpcMessage> writer) : ITransport
{
    public string? SessionId => null;

    public ChannelReader<JsonRpcMessage> MessageReader => incoming.Reader;

    public async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        // 실제 전송처럼 직렬화해 양쪽이 같은 메시지 객체(Context 등)를 공유하지 않게 한다
        string json = JsonSerializer.Serialize(message, McpJsonUtilities.DefaultOptions);
        JsonRpcMessage copy = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions)!;
        await writer.WriteAsync(copy, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        // ITransport 계약: dispose 시 자기 MessageReader 채널도 완료한다
        writer.TryComplete();
        incoming.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    public static (ChannelTransport Client, ChannelTransport Server) CreatePair()
    {
        Channel<JsonRpcMessage> toServer = Channel.CreateUnbounded<JsonRpcMessage>();
        Channel<JsonRpcMessage> toClient = Channel.CreateUnbounded<JsonRpcMessage>();
        return (new ChannelTransport(toClient, toServer.Writer), new ChannelTransport(toServer, toClient.Writer));
    }
}

internal sealed class ConnectedClientTransport(ITransport transport) : IClientTransport
{
    public string Name => "in-memory";

    public Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default) => Task.FromResult(transport);
}

/// <summary>dispose 시작·완료를 기록하는 소유 자원. <see cref="Hang"/>이면 dispose가 끝나지 않는다.</summary>
internal sealed class TrackingOwner(bool hang = false) : IAsyncDisposable
{
    private readonly TaskCompletionSource _never = new();

    public bool Hang => hang;

    public bool DisposeStarted { get; private set; }

    public bool Disposed { get; private set; }

    public async ValueTask DisposeAsync()
    {
        DisposeStarted = true;
        if (hang)
            await _never.Task;
        Disposed = true;
    }
}

/// <summary>가짜 Jenkins MCP Server. 호출 횟수와 받은 인자를 기록한다.</summary>
internal sealed class FakeJenkinsServer : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly List<McpServer> _servers = [];

    public FakeJenkinsServer(params string[] toolNames)
    {
        ToolNames = toolNames;
    }

    public string[] ToolNames { get; }

    public int ConnectCount { get; private set; }

    public int ListToolsCount;

    public int CallToolCount;

    public string? LastToolName { get; private set; }

    public IDictionary<string, JsonElement>? LastArguments { get; private set; }

    /// <summary>true면 CallTool에서 JSON-RPC 오류를 보낸다.</summary>
    public bool FailCallWithProtocolError { get; set; }

    public async Task<McpClient> ConnectAsync(CancellationToken cancellationToken = default)
    {
        ConnectCount++;
        (ChannelTransport client, ChannelTransport server) = ChannelTransport.CreatePair();
        McpServerOptions options = new()
        {
            ServerInfo = new Implementation { Name = "fake-jenkins", Version = "1.0" },
            Handlers = new McpServerHandlers
            {
                ListToolsHandler = (_, _) =>
                {
                    Interlocked.Increment(ref ListToolsCount);
                    return ValueTask.FromResult(new ListToolsResult
                    {
                        Tools = [.. ToolNames.Select(n => new Tool
                        {
                            Name = n,
                            Description = $"{n} description",
                            InputSchema = JsonDocument.Parse("""{"type":"object","properties":{"jobName":{"type":"string"}}}""").RootElement,
                        })],
                    });
                },
                CallToolHandler = (request, _) =>
                {
                    Interlocked.Increment(ref CallToolCount);
                    LastToolName = request.Params!.Name;
                    LastArguments = request.Params.Arguments;
                    if (FailCallWithProtocolError)
                        throw new McpProtocolException("upstream failure", McpErrorCode.InvalidParams);
                    return ValueTask.FromResult(new CallToolResult
                    {
                        Content = [new TextContentBlock { Text = $"called {request.Params.Name}" }],
                        IsError = request.Params.Name == "failing_tool",
                    });
                },
            },
        };
        McpServer mcpServer = McpServer.Create(server, options);
        _servers.Add(mcpServer);
        _ = mcpServer.RunAsync(_stop.Token);
        return await McpClient.CreateAsync(new ConnectedClientTransport(client), cancellationToken: cancellationToken);
    }

    /// <summary>pool 생성기용 연결. 소유 자원으로 <see cref="TrackingOwner"/>를 붙인다.</summary>
    public async Task<JenkinsConnection> ConnectionAsync(CancellationToken cancellationToken = default, TrackingOwner? owner = null)
        => new(await ConnectAsync(cancellationToken), owner ?? new TrackingOwner());

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        foreach (McpServer server in _servers)
            await server.DisposeAsync();
        _stop.Dispose();
    }
}
