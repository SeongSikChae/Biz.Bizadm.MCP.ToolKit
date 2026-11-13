using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.NuGet;

/// <summary>등록한 NuGet 피드의 NuGet Server API v3를 호출한다.</summary>
internal sealed class NuGetFeedClient : IDisposable
{
    private readonly NuGetInstance[] _ordered;
    private readonly Dictionary<string, NuGetInstance> _instances;
    private readonly HttpClient _http;

    /// <param name="options">검증한 NuGet 옵션</param>
    /// <param name="handler">테스트용 HTTP 처리기. null이면 기본 처리기를 쓴다.</param>
    public NuGetFeedClient(NuGetOptions options, HttpMessageHandler? handler = null)
    {
        _ordered = [.. options.Instances];
        _instances = _ordered.ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase);
        _http = new HttpClient(handler ?? new SocketsHttpHandler(), disposeHandler: handler is null);
    }

    /// <summary>등록 순서의 인스턴스 목록</summary>
    public IReadOnlyList<NuGetInstance> Instances => _ordered;

    /// <summary>이름으로 인스턴스를 찾는다.</summary>
    /// <exception cref="McpException">미등록·공백 이름</exception>
    public NuGetInstance Resolve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || !_instances.TryGetValue(name.Trim(), out NuGetInstance? instance))
        {
            string label = string.IsNullOrWhiteSpace(name) ? "instance is required." : $"Unknown NuGet instance '{name.Trim()}'.";
            throw new McpException($"{label} Registered instances: {string.Join(", ", _ordered.Select(i => i.Name))}");
        }
        return instance;
    }

    public void Dispose() => _http.Dispose();
}
