using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Jenkins;

/// <summary>등록한 Jenkins 인스턴스의 User/API Token(Basic)으로 Jenkins REST API를 호출한다.</summary>
/// <remarks>
/// REST 루트는 인스턴스 <see cref="JenkinsInstance.Endpoint"/>에서 <c>/mcp-server/mcp</c>를 뗀 주소다.
/// 자동 리다이렉트는 따라가지 않는다. 오류는 자격 증명 없이 인스턴스 이름·메서드·경로·HTTP 상태를 담은 <see cref="McpException"/>으로 던진다.
/// </remarks>
internal sealed class JenkinsRestClient : IDisposable
{
    internal const string McpPath = "/mcp-server/mcp";
    private const int MaxErrorBodyLength = 200;

    private static readonly string UserAgent = $"Biz.Bizadm.MCP.ToolKit/{typeof(JenkinsRestClient).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"}";
    private static readonly Regex Title = new("<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex Tag = new("<[^>]+>", RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex Spaces = new("\\s+", RegexOptions.CultureInvariant);

    private readonly JenkinsInstance[] _ordered;
    private readonly Dictionary<string, (JenkinsInstance Instance, Uri? Root)> _instances;
    private readonly HttpClient _http;

    /// <param name="options">검증한 Jenkins 옵션</param>
    /// <param name="handler">테스트용 HTTP 처리기. null이면 리다이렉트를 따라가지 않고 연결 수명을 제한한 기본 처리기를 쓴다.</param>
    public JenkinsRestClient(JenkinsOptions options, HttpMessageHandler? handler = null)
    {
        _ordered = [.. options.Instances];
        _instances = _ordered.ToDictionary(i => i.Name, i => (i, RootOf(i.Endpoint)), StringComparer.OrdinalIgnoreCase);
        _http = new HttpClient(handler ?? new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            AllowAutoRedirect = false,
        }, disposeHandler: handler is null);
    }

    /// <summary>이름으로 인스턴스를 찾고 REST 루트가 있는지 확인한다.</summary>
    /// <exception cref="McpException">미등록·공백 이름이거나 Endpoint가 <c>/mcp-server/mcp</c>로 끝나지 않는다.</exception>
    public JenkinsInstance Resolve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || !_instances.TryGetValue(name.Trim(), out (JenkinsInstance Instance, Uri? Root) entry))
        {
            string label = string.IsNullOrWhiteSpace(name) ? "instance is required." : $"Unknown Jenkins instance '{name.Trim()}'.";
            throw new McpException($"{label} Registered instances: {string.Join(", ", _ordered.Select(i => i.Name))}");
        }
        if (entry.Root is null)
            throw new McpException($"Jenkins '{entry.Instance.Name}' Endpoint does not end with {McpPath}; REST tools are unavailable.");
        return entry.Instance;
    }

    /// <summary>Endpoint 경로가 <c>/mcp-server/mcp</c>(끝 <c>/</c> 허용, 대소문자 무시)로 끝나면 그 앞 + <c>/</c>, 아니면 null</summary>
    internal static Uri? RootOf(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri))
            return null;
        string path = uri.AbsolutePath.TrimEnd('/');
        if (!path.EndsWith(McpPath, StringComparison.OrdinalIgnoreCase))
            return null;
        return new Uri(uri.GetLeftPart(UriPartial.Authority) + path[..^McpPath.Length] + "/");
    }

    /// <summary>Jenkins 전체 이름(<c>folder/sub/job</c>)을 정규화한 이름과 REST 경로(<c>job/folder/job/sub/job/job/</c>)로 바꾼다.</summary>
    /// <exception cref="McpException">빈 세그먼트·<c>.</c>·<c>..</c>·<c>\</c>가 있다.</exception>
    internal static (string Name, string Path) JobPath(string job)
    {
        string[] segments = job.Trim().Trim('/').Split('/');
        if (segments.Any(s => s.Length == 0 || s is "." or ".." || s.Contains('\\')))
            throw new McpException("job must be a full name like folder/job without empty, '.', '..' or '\\' segments.");
        return (string.Join('/', segments), string.Concat(segments.Select(s => "job/" + Uri.EscapeDataString(s) + "/")));
    }

    /// <summary>요청을 보내고 상태 판정 없이 상태·Location(절대)·본문을 돌려준다.</summary>
    /// <param name="instance"><see cref="Resolve"/> 결과</param>
    /// <param name="method">HTTP 메서드</param>
    /// <param name="jobPath"><see cref="JobPath"/>의 Path</param>
    /// <param name="action">Job 경로 뒤 동작(예: <c>config.xml</c>, <c>doDelete</c>)</param>
    /// <param name="content">요청 본문. 없으면 null. 요청과 함께 dispose된다.</param>
    /// <param name="cancellationToken">호출자 취소. 취소는 감싸지 않고 그대로 전파한다.</param>
    public async Task<(int Status, Uri? Location, string Body)> SendRawAsync(JenkinsInstance instance, HttpMethod method,
        string jobPath, string action, HttpContent? content, CancellationToken cancellationToken)
    {
        Uri root = _instances[instance.Name].Root!;
        string path = jobPath + action;
        using HttpRequestMessage request = new(method, new Uri(root, path)) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{instance.User}:{instance.ApiToken}")));
        request.Headers.UserAgent.ParseAdd(UserAgent);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // 호출자 취소
        }
        catch (Exception ex)
        {
            throw new McpException($"Jenkins '{instance.Name}' {method} {path} failed: {ex.Message}", ex);
        }

        using (response)
        {
            string text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Uri? location = response.Headers.Location is { } l ? new Uri(request.RequestUri!, l) : null;
            return ((int)response.StatusCode, location, text);
        }
    }

    /// <summary>요청을 보내고 2xx면 본문을 돌려준다. 그 밖은 <see cref="Failure"/>를 던진다.</summary>
    public async Task<string> SendAsync(JenkinsInstance instance, HttpMethod method, string jobPath, string action,
        HttpContent? content, CancellationToken cancellationToken)
    {
        (int code, Uri? location, string body) = await SendRawAsync(instance, method, jobPath, action, content, cancellationToken).ConfigureAwait(false);
        return code is >= 200 and < 300 ? body : throw Failure(instance, method, jobPath + action, code, location, body);
    }

    /// <summary>실패 응답 예외. 3xx면 Location, 아니면 본문 요약(403 crumb이면 API Token 안내)</summary>
    internal static McpException Failure(JenkinsInstance instance, HttpMethod method, string path, int code, Uri? location, string body)
    {
        string message = code is >= 300 and < 400
            ? $"redirected to {location?.ToString() ?? "(none)"}"
            : DescribeError(body, code) + (code == 403 && body.Contains("crumb", StringComparison.OrdinalIgnoreCase)
                ? " (ApiToken must be a Jenkins API token, not a password)" : string.Empty);
        return new McpException($"Jenkins '{instance.Name}' {method} {path} failed (HTTP {code}): {message}");
    }

    /// <summary>오류 본문 요약. HTML이면 title(5xx면 title + 태그 제거 텍스트), title이 없으면 태그 제거 텍스트, 아니면 본문. 공백 축약, 앞 200자</summary>
    internal static string DescribeError(string body, int status)
    {
        string text = body;
        if (body.Contains("<html", StringComparison.OrdinalIgnoreCase) || body.Contains("<title", StringComparison.OrdinalIgnoreCase))
        {
            Match title = Title.Match(body);
            string stripped = Tag.Replace(Title.Replace(body, " "), " ");
            text = !title.Success ? stripped
                : status >= 500 ? title.Groups[1].Value + " - " + stripped
                : title.Groups[1].Value;
            text = WebUtility.HtmlDecode(text);
        }
        text = Spaces.Replace(text, " ").Trim();
        return text.Length <= MaxErrorBodyLength ? text : text[..MaxErrorBodyLength];
    }

    public void Dispose() => _http.Dispose();
}
