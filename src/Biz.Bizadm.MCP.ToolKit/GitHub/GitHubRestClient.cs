using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.GitHub;

/// <summary>등록한 GitHub 인스턴스의 Token으로 GitHub REST API를 호출한다.</summary>
/// <remarks>오류는 Token 없이 인스턴스 이름·메서드·경로·HTTP 상태를 담은 <see cref="McpException"/>으로 던져 SDK가 <c>isError</c> 결과로 돌려주게 한다.</remarks>
internal sealed class GitHubRestClient : IDisposable
{
    internal const string ApiVersion = "2022-11-28";
    private const int MaxErrorBodyLength = 200;

    private static readonly string UserAgent = $"Biz.Bizadm.MCP.ToolKit/{typeof(GitHubRestClient).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"}";

    private readonly GitHubInstance[] _ordered;
    private readonly Dictionary<string, GitHubInstance> _instances;
    private readonly HttpClient _http;

    /// <param name="options">검증한 GitHub 옵션</param>
    /// <param name="handler">테스트용 HTTP 처리기. null이면 연결 수명을 제한한 기본 처리기를 쓴다.</param>
    public GitHubRestClient(GitHubOptions options, HttpMessageHandler? handler = null)
    {
        _ordered = [.. options.Instances];
        _instances = _ordered.ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase);
        _http = new HttpClient(handler ?? new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) }, disposeHandler: handler is null);
    }

    /// <summary>이름으로 인스턴스를 찾는다. 없으면 등록 이름을 안내하는 <see cref="McpException"/>(게이트웨이와 같은 형식)</summary>
    public GitHubInstance Resolve(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name) && _instances.TryGetValue(name.Trim(), out GitHubInstance? instance))
            return instance;
        string label = string.IsNullOrWhiteSpace(name) ? "instance is required." : $"Unknown GitHub instance '{name.Trim()}'.";
        throw new McpException($"{label} Registered instances: {string.Join(", ", _ordered.Select(i => i.Name))}");
    }

    /// <summary>REST 요청을 보내고 응답 본문을 반환한다. 본문이 없으면(204 등) null이다.</summary>
    /// <param name="instance">호출할 인스턴스(<see cref="Resolve"/> 결과)</param>
    /// <param name="method">HTTP 메서드</param>
    /// <param name="path"><see cref="GitHubInstance.ApiEndpoint"/> 기준 상대 경로(예: <c>repos/o/r/git/refs</c>)</param>
    /// <param name="body">JSON 본문. 없으면 null</param>
    /// <param name="cancellationToken">호출자 취소. 취소는 감싸지 않고 그대로 전파한다.</param>
    public Task<string?> SendAsync(GitHubInstance instance, HttpMethod method, string path, JsonObject? body, CancellationToken cancellationToken)
        => SendAsync(instance, method, new Uri(new Uri(instance.ApiEndpoint), path), path,
            body is null ? null : new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"), cancellationToken);

    /// <summary>절대 URI로 요청을 보내고 응답 본문을 반환한다. 본문이 없으면(204 등) null이다.</summary>
    /// <param name="instance">호출할 인스턴스(<see cref="Resolve"/> 결과)</param>
    /// <param name="method">HTTP 메서드</param>
    /// <param name="uri">요청 URI. 업로드처럼 <see cref="GitHubInstance.ApiEndpoint"/> 밖이면 호출자가 호스트를 검사해야 한다(<see cref="ResolveUploadUri"/>).</param>
    /// <param name="path">오류 메시지에 쓰는 경로</param>
    /// <param name="content">요청 본문. 없으면 null. 요청과 함께 dispose된다.</param>
    /// <param name="cancellationToken">호출자 취소. 취소는 감싸지 않고 그대로 전파한다.</param>
    public async Task<string?> SendAsync(GitHubInstance instance, HttpMethod method, Uri uri, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(method, uri) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", instance.Token);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
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
            throw new McpException($"GitHub '{instance.Name}' {method} {path} failed: {ex.Message}", ex);
        }

        using (response)
        {
            string text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new McpException($"GitHub '{instance.Name}' {method} {path} failed (HTTP {(int)response.StatusCode}): {DescribeError(text)}");
            return text.Length == 0 ? null : text;
        }
    }

    /// <summary>오류 응답 요약. JSON이면 <c>message</c>와 <c>errors</c>, 아니면 본문 앞부분</summary>
    internal static string DescribeError(string body)
    {
        try
        {
            if (JsonNode.Parse(body) is JsonObject json && json["message"] is JsonValue message)
            {
                string text = message.ToString();
                if (json["errors"] is JsonArray errors && errors.Count > 0)
                {
                    IEnumerable<string> details = errors.Select(e => e is JsonObject o
                        ? (o["message"] ?? o["code"])?.ToString() ?? o.ToJsonString()
                        : e?.ToString() ?? string.Empty);
                    text += $" ({string.Join("; ", details.Where(d => d.Length > 0))})";
                }
                return text;
            }
        }
        catch (JsonException)
        {
            // JSON이 아니면 본문을 그대로 쓴다
        }
        string trimmed = body.Trim();
        return trimmed.Length <= MaxErrorBodyLength ? trimmed : trimmed[..MaxErrorBodyLength];
    }

    /// <summary><c>repos/{owner}/{repo}/</c> (각 세그먼트 URL 인코딩)</summary>
    internal static string RepoPath(string owner, string repo) => $"repos/{Segment(owner, "owner")}/{Segment(repo, "repo")}/";

    /// <summary>앞의 <c>refs/</c>를 뗀 ref(예: <c>tags/v1.0</c>)</summary>
    internal static string NormalizeRef(string reference) => reference.StartsWith("refs/", StringComparison.Ordinal) ? reference[5..] : reference;

    /// <summary>URL 경로용 ref. 세그먼트마다 인코딩하고 <c>/</c>는 구분자로 둔다.</summary>
    internal static string RefPath(string reference) => string.Join('/', NormalizeRef(reference).Split('/').Select(s => Segment(s, "ref")));

    /// <summary>경로 세그먼트 하나(예: 릴리스 tag)를 인코딩한다. 빈 값·<c>.</c>·<c>..</c>는 거부한다.</summary>
    internal static string PathSegment(string value, string name) => Segment(value, name);

    /// <summary>릴리스 <c>upload_url</c> 템플릿에서 업로드 URI를 만든다.</summary>
    /// <remarks>Token을 실어 보내므로 scheme·port가 <see cref="GitHubInstance.ApiEndpoint"/>와 같고 host도 같아야 한다. 예외는 <c>api.github.com</c> → <c>uploads.github.com</c> 하나다.</remarks>
    internal static Uri ResolveUploadUri(GitHubInstance instance, string? uploadUrlTemplate, string name, string? label)
    {
        string template = uploadUrlTemplate ?? string.Empty;
        int brace = template.IndexOf('{');
        string raw = brace >= 0 ? template[..brace] : template;
        if (!Uri.TryCreate(raw, UriKind.Absolute, out Uri? upload) || upload.Scheme is not ("http" or "https"))
            throw new McpException($"GitHub '{instance.Name}' release has no valid upload_url.");

        Uri api = new(instance.ApiEndpoint);
        bool sameHost = string.Equals(upload.Host, api.Host, StringComparison.OrdinalIgnoreCase)
            || (api.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) && upload.Host.Equals("uploads.github.com", StringComparison.OrdinalIgnoreCase));
        if (!sameHost || upload.Scheme != api.Scheme || upload.Port != api.Port)
            throw new McpException($"GitHub '{instance.Name}' upload_url host '{upload.Host}' does not match ApiEndpoint.");

        string query = "?name=" + Uri.EscapeDataString(name) + (string.IsNullOrEmpty(label) ? string.Empty : "&label=" + Uri.EscapeDataString(label));
        return new Uri(upload.GetLeftPart(UriPartial.Path) + query);
    }

    /// <summary>경로 세그먼트 하나를 인코딩한다. 빈 값·<c>.</c>·<c>..</c>는 주소 결합에서 경로를 벗어나므로 거부한다.</summary>
    private static string Segment(string value, string name)
        => value.Length == 0 || value is "." or ".."
            ? throw new McpException($"{name} must not contain empty, '.' or '..' path segments.")
            : Uri.EscapeDataString(value);

    public void Dispose() => _http.Dispose();
}
