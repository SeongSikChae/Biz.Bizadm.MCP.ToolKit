using System.Collections.Concurrent;
using System.Net;

using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Biz.Bizadm.MCP.ToolKit.Relay;

/// <summary>중계 대상 인스턴스별 MCP 연결과 Tool 목록 캐시</summary>
internal sealed class McpRelayPool : IAsyncDisposable, IDisposable
{
    private readonly McpRelayTarget[] _ordered;
    private readonly Dictionary<string, McpRelayTarget> _instances;
    private readonly Func<McpRelayTarget, CancellationToken, Task<McpRelayConnection>> _connect;
    private readonly ConcurrentDictionary<string, Lazy<Task<McpRelayConnection>>> _connections = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<Tool>> _tools = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="label">메시지·transport 이름에 쓰는 서비스 이름(예: Jenkins, GitHub)</param>
    /// <param name="targets">등록 순서대로 중계 대상</param>
    public McpRelayPool(string label, IEnumerable<McpRelayTarget> targets)
        : this(label, targets, null)
    {
    }

    internal McpRelayPool(string label, IEnumerable<McpRelayTarget> targets, Func<McpRelayTarget, CancellationToken, Task<McpRelayConnection>>? connect)
    {
        Label = label;
        _ordered = [.. targets];
        _instances = _ordered.ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase);
        _connect = connect ?? ConnectAsync;
    }

    /// <summary>서비스 이름</summary>
    public string Label { get; }

    /// <summary>등록 순서대로 인스턴스 목록</summary>
    public IReadOnlyList<McpRelayTarget> Instances => _ordered;

    /// <summary>이름으로 인스턴스를 찾는다. 없으면 등록 이름을 안내하는 <see cref="McpException"/></summary>
    public McpRelayTarget Resolve(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name) && _instances.TryGetValue(name.Trim(), out McpRelayTarget? instance))
            return instance;
        string label = string.IsNullOrWhiteSpace(name) ? "instance is required." : $"Unknown {Label} instance '{name.Trim()}'.";
        throw new McpException($"{label} Registered instances: {string.Join(", ", _ordered.Select(i => i.Name))}");
    }

    /// <summary>인스턴스 Tool 목록. 캐시가 있고 <paramref name="refresh"/>가 false면 upstream을 부르지 않는다.</summary>
    public async Task<IReadOnlyList<Tool>> ListToolsAsync(McpRelayTarget instance, bool refresh, CancellationToken cancellationToken)
    {
        if (!refresh && _tools.TryGetValue(instance.Name, out IReadOnlyList<Tool>? cached))
            return cached;

        IList<McpClientTool> tools = await InvokeAsync(instance, (c, ct) => c.ListToolsAsync(cancellationToken: ct).AsTask(), cancellationToken).ConfigureAwait(false);
        Tool[] result = [.. tools.Select(t => t.ProtocolTool)];
        _tools[instance.Name] = result;
        return result;
    }

    /// <summary>인스턴스의 Tool을 호출하고 upstream 결과를 그대로 반환한다.</summary>
    public Task<CallToolResult> CallToolAsync(McpRelayTarget instance, string tool, IReadOnlyDictionary<string, object?>? arguments, CancellationToken cancellationToken)
        => InvokeAsync(instance, (c, ct) => c.CallToolAsync(tool, arguments, cancellationToken: ct).AsTask(), cancellationToken);

    private async Task<T> InvokeAsync<T>(McpRelayTarget instance, Func<McpClient, CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        // 연결은 호출자 취소와 분리해 취소된 Task가 캐시되지 않게 한다
        Lazy<Task<McpRelayConnection>> entry = _connections.GetOrAdd(instance.Name,
            _ => new Lazy<Task<McpRelayConnection>>(() => _connect(instance, CancellationToken.None)));
        try
        {
            McpRelayConnection connection = await entry.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            return await call(connection.Client, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // 호출자 취소. 연결 유지
        }
        catch (McpProtocolException ex)
        {
            // upstream이 보낸 오류. 그대로 던지면 SDK 서버가 JSON-RPC 오류로 다시 던지므로 결과(IsError)로 전달되게 감싼다. 연결 유지
            throw new McpException($"{Label} '{instance.Name}' returned error {(int)ex.ErrorCode}: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            Evict(instance.Name, entry);
            throw new McpException(DescribeFailure(Label, instance.Name, ex), ex);
        }
    }

    internal static string DescribeFailure(string label, string instanceName, Exception ex)
    {
        // 연결 단계 예외는 다른 예외에 감싸여 올 수 있으므로 InnerException 사슬에서 HTTP 상태를 찾는다
        HttpStatusCode? code = null;
        for (Exception? e = ex; e is not null && code is null; e = e.InnerException)
            code = (e as HttpRequestException)?.StatusCode;
        string status = code is { } c ? $" (HTTP {(int)c})" : string.Empty;
        return $"{label} '{instanceName}' request failed{status}: {ex.Message}";
    }

    internal static HttpClientTransportOptions CreateTransportOptions(string label, McpRelayTarget instance) => new()
    {
        Endpoint = new Uri(instance.Endpoint),
        TransportMode = HttpTransportMode.StreamableHttp,
        Name = $"{label.ToLowerInvariant()}-{instance.Name}",
        AdditionalHeaders = new Dictionary<string, string>(instance.Headers),
    };

    private async Task<McpRelayConnection> ConnectAsync(McpRelayTarget instance, CancellationToken cancellationToken)
    {
        HttpClientTransport transport = new(CreateTransportOptions(Label, instance));
        try
        {
            McpClient client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken).ConfigureAwait(false);
            return new McpRelayConnection(client, transport);
        }
        catch
        {
            await transport.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private void Evict(string name, Lazy<Task<McpRelayConnection>> entry)
    {
        if (!_connections.TryRemove(new KeyValuePair<string, Lazy<Task<McpRelayConnection>>>(name, entry)))
            return;
        // 세션 종료 DELETE가 응답 없는 호스트에서 오래 걸릴 수 있으므로 기다리지 않는다
        _ = DisposeQuietlyAsync(entry);
    }

    private static async Task DisposeQuietlyAsync(Lazy<Task<McpRelayConnection>> entry)
    {
        if (!entry.IsValueCreated || !entry.Value.IsCompletedSuccessfully)
            return;
        try
        {
            await entry.Value.Result.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 이미 끊긴 연결의 정리 실패는 무시한다
        }
    }

    public async ValueTask DisposeAsync()
    {
        List<Task> disposals = [];
        foreach (string name in _connections.Keys.ToArray())
        {
            if (_connections.TryRemove(name, out Lazy<Task<McpRelayConnection>>? entry))
                disposals.Add(DisposeQuietlyAsync(entry));
        }
        await Task.WhenAll(disposals).ConfigureAwait(false);
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
