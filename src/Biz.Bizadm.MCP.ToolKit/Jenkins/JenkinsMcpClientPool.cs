using System.Collections.Concurrent;
using System.Net;
using System.Text;

using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Biz.Bizadm.MCP.ToolKit.Jenkins;

/// <summary>Jenkins 인스턴스별 MCP 연결과 Tool 목록 캐시</summary>
internal sealed class JenkinsMcpClientPool : IAsyncDisposable, IDisposable
{
    private readonly JenkinsInstance[] _ordered;
    private readonly Dictionary<string, JenkinsInstance> _instances;
    private readonly Func<JenkinsInstance, CancellationToken, Task<JenkinsConnection>> _connect;
    private readonly ConcurrentDictionary<string, Lazy<Task<JenkinsConnection>>> _connections = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<Tool>> _tools = new(StringComparer.OrdinalIgnoreCase);

    public JenkinsMcpClientPool(JenkinsOptions options)
        : this(options, ConnectAsync)
    {
    }

    internal JenkinsMcpClientPool(JenkinsOptions options, Func<JenkinsInstance, CancellationToken, Task<JenkinsConnection>> connect)
    {
        _ordered = [.. options.Instances];
        _instances = _ordered.ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase);
        _connect = connect;
    }

    /// <summary>등록 순서대로 인스턴스 목록</summary>
    public IReadOnlyList<JenkinsInstance> Instances => _ordered;

    /// <summary>이름으로 인스턴스를 찾는다. 없으면 등록 이름을 안내하는 <see cref="McpException"/></summary>
    public JenkinsInstance Resolve(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name) && _instances.TryGetValue(name.Trim(), out JenkinsInstance? instance))
            return instance;
        string label = string.IsNullOrWhiteSpace(name) ? "instance is required." : $"Unknown Jenkins instance '{name.Trim()}'.";
        throw new McpException($"{label} Registered instances: {string.Join(", ", _ordered.Select(i => i.Name))}");
    }

    /// <summary>인스턴스 Tool 목록. 캐시가 있고 <paramref name="refresh"/>가 false면 upstream을 부르지 않는다.</summary>
    public async Task<IReadOnlyList<Tool>> ListToolsAsync(JenkinsInstance instance, bool refresh, CancellationToken cancellationToken)
    {
        if (!refresh && _tools.TryGetValue(instance.Name, out IReadOnlyList<Tool>? cached))
            return cached;

        IList<McpClientTool> tools = await InvokeAsync(instance, (c, ct) => c.ListToolsAsync(cancellationToken: ct).AsTask(), cancellationToken).ConfigureAwait(false);
        Tool[] result = [.. tools.Select(t => t.ProtocolTool)];
        _tools[instance.Name] = result;
        return result;
    }

    /// <summary>인스턴스의 Tool을 호출하고 upstream 결과를 그대로 반환한다.</summary>
    public Task<CallToolResult> CallToolAsync(JenkinsInstance instance, string tool, IReadOnlyDictionary<string, object?>? arguments, CancellationToken cancellationToken)
        => InvokeAsync(instance, (c, ct) => c.CallToolAsync(tool, arguments, cancellationToken: ct).AsTask(), cancellationToken);

    private async Task<T> InvokeAsync<T>(JenkinsInstance instance, Func<McpClient, CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        // 연결은 호출자 취소와 분리해 취소된 Task가 캐시되지 않게 한다
        Lazy<Task<JenkinsConnection>> entry = _connections.GetOrAdd(instance.Name,
            _ => new Lazy<Task<JenkinsConnection>>(() => _connect(instance, CancellationToken.None)));
        try
        {
            JenkinsConnection connection = await entry.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            return await call(connection.Client, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // 호출자 취소. 연결 유지
        }
        catch (McpProtocolException ex)
        {
            // upstream이 보낸 오류. 그대로 던지면 SDK 서버가 JSON-RPC 오류로 다시 던지므로 결과(IsError)로 전달되게 감싼다. 연결 유지
            throw new McpException($"Jenkins '{instance.Name}' returned error {(int)ex.ErrorCode}: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            Evict(instance.Name, entry);
            throw new McpException(DescribeFailure(instance.Name, ex), ex);
        }
    }

    internal static string DescribeFailure(string instanceName, Exception ex)
    {
        // 연결 단계 예외는 다른 예외에 감싸여 올 수 있으므로 InnerException 사슬에서 HTTP 상태를 찾는다
        HttpStatusCode? code = null;
        for (Exception? e = ex; e is not null && code is null; e = e.InnerException)
            code = (e as HttpRequestException)?.StatusCode;
        string status = code is { } c ? $" (HTTP {(int)c})" : string.Empty;
        return $"Jenkins '{instanceName}' request failed{status}: {ex.Message}";
    }

    internal static string BasicAuthorization(JenkinsInstance instance)
        => "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{instance.User}:{instance.ApiToken}"));

    internal static HttpClientTransportOptions CreateTransportOptions(JenkinsInstance instance) => new()
    {
        Endpoint = new Uri(instance.Endpoint),
        TransportMode = HttpTransportMode.StreamableHttp,
        Name = $"jenkins-{instance.Name}",
        AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = BasicAuthorization(instance) },
    };

    private static async Task<JenkinsConnection> ConnectAsync(JenkinsInstance instance, CancellationToken cancellationToken)
    {
        HttpClientTransport transport = new(CreateTransportOptions(instance));
        try
        {
            McpClient client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken).ConfigureAwait(false);
            return new JenkinsConnection(client, transport);
        }
        catch
        {
            await transport.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private void Evict(string name, Lazy<Task<JenkinsConnection>> entry)
    {
        if (!_connections.TryRemove(new KeyValuePair<string, Lazy<Task<JenkinsConnection>>>(name, entry)))
            return;
        // 세션 종료 DELETE가 응답 없는 호스트에서 오래 걸릴 수 있으므로 기다리지 않는다
        _ = DisposeQuietlyAsync(entry);
    }

    private static async Task DisposeQuietlyAsync(Lazy<Task<JenkinsConnection>> entry)
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
            if (_connections.TryRemove(name, out Lazy<Task<JenkinsConnection>>? entry))
                disposals.Add(DisposeQuietlyAsync(entry));
        }
        await Task.WhenAll(disposals).ConfigureAwait(false);
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
