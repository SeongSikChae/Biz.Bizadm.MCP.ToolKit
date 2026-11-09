---
status: done
category: github
feature: github-ref-tools
phase: 개발
---

# github-ref-tools 개발

선행: `0005-github-ref-tools-방안.md`, `0006-github-ref-tools-설계.md`

경로 기준: `src/Biz.Bizadm.MCP.ToolKit/`(src), `tests/Biz.Bizadm.MCP.ToolKit.Tests/`(tests)

재사용: HTTP는 `RedmineClient`와 같이 `SocketsHttpHandler { PooledConnectionLifetime = 2분 }`, 테스트는 `V7RecordingHandler`와 같은 기록용 `HttpMessageHandler`. 등록 검증은 `McpRelayValidation`.

## 단위 1. ApiEndpoint 필수화

- **대상**: src `GitHub/GitHubInstance.cs`, `Relay/McpRelayValidation.cs`, `GitHubMcpServerBuilderExtensions.cs`, tests `GitHub/*Tests.cs`(인스턴스에 `ApiEndpoint` 추가)
- **코드 초안**:

```csharp
// GitHubInstance
/// <summary>GitHub REST API 기본 주소(절대 http/https URI, 예: <c>https://api.github.com/</c>, Enterprise <c>https://{host}/api/v3/</c>)</summary>
public string ApiEndpoint { get; set; } = string.Empty;
```

```csharp
// McpRelayValidation: NameAndEndpoint의 URI 검사를 꺼내 재사용
public static string AbsoluteHttpUri(string label, string name, string property, string? value, string credentialHint)
{
    if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https"))
        throw new ArgumentException($"{label} instance '{name}' {property} must be an absolute http or https URI", ParamName);
    if (!string.IsNullOrEmpty(uri.UserInfo))
        throw new ArgumentException($"{label} instance '{name}' {property} must not contain user info. Use {credentialHint}", ParamName);
    return uri.AbsoluteUri;
}
// NameAndEndpoint는 이름 검사 뒤 AbsoluteHttpUri(label, trimmed, "Endpoint", endpoint, credentialHint)를 호출 (메시지 동일)
```

```csharp
// GitHubMcpServerBuilderExtensions.Validate
string apiEndpoint = McpRelayValidation.AbsoluteHttpUri(Label, name, "ApiEndpoint", instance.ApiEndpoint, "Token");
if (!apiEndpoint.EndsWith('/'))
    apiEndpoint += "/";
copies.Add(new GitHubInstance { /* ... existing ... */ ApiEndpoint = apiEndpoint });
```

- **검증**: 기존 테스트 통과(Jenkins 메시지 불변), GitHub 검증 오류 케이스에 `ApiEndpoint` 누락·상대 URI·ftp·사용자 정보 추가(메시지에 토큰 없음), `https://ghe.example/api/v3` → `https://ghe.example/api/v3/` 정규화
- **선행 단위**: 없음

## 단위 2. GitHubRestClient

- **대상**: src `GitHub/GitHubRestClient.cs`(신규), tests `GitHub/GitHubRecordingHandler.cs`, `GitHub/GitHubRestClientTests.cs`(신규)
- **코드 초안**:

```csharp
internal sealed class GitHubRestClient : IDisposable
{
    private static readonly string UserAgent = $"Biz.Bizadm.MCP.ToolKit/{typeof(GitHubRestClient).Assembly.GetName().Version?.ToString(3)}";
    private readonly Dictionary<string, GitHubInstance> _instances;
    private readonly GitHubInstance[] _ordered;
    private readonly HttpClient _http;

    public GitHubRestClient(GitHubOptions options, HttpMessageHandler? handler = null)
    {
        _ordered = [.. options.Instances];
        _instances = _ordered.ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase);
        _http = new HttpClient(handler ?? new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) }, disposeHandler: handler is null);
    }

    public GitHubInstance Resolve(string? name) { /* 게이트웨이와 같은 "instance is required." / "Unknown GitHub instance '{n}'. Registered instances: ..." */ }

    public async Task<string?> SendAsync(GitHubInstance instance, HttpMethod method, string path, JsonObject? body, CancellationToken ct)
    {
        using HttpRequestMessage request = new(method, new Uri(new Uri(instance.ApiEndpoint), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", instance.Token);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.UserAgent.ParseAdd(UserAgent);
        if (body is not null)
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            throw new McpException($"GitHub '{instance.Name}' {method} {path} failed: {ex.Message}", ex);
        }
        using (response)
        {
            string text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new McpException($"GitHub '{instance.Name}' {method} {path} failed (HTTP {(int)response.StatusCode}): {DescribeError(text)}");
            return text.Length == 0 ? null : text;
        }
    }

    internal static string DescribeError(string body) { /* JSON이면 message + errors[].message|code 요약, 아니면 앞 200자 */ }

    internal static string RepoPath(string owner, string repo) => $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/";
    internal static string RefPath(string reference) => string.Join('/', NormalizeRef(reference).Split('/').Select(Uri.EscapeDataString));
    internal static string NormalizeRef(string reference) => reference.StartsWith("refs/", StringComparison.Ordinal) ? reference[5..] : reference;

    public void Dispose() => _http.Dispose();
}
```

- **검증**: 기록 핸들러로 URL 결합(github.com, `https://ghe.example/api/v3/`), 헤더(Bearer·Accept·API 버전·User-Agent), 2xx 본문 반환, 204 → null, 403·422 JSON 메시지, 비JSON 본문 200자, 전송 예외 감싸기, 호출자 취소 전파, 메시지에 토큰 없음, `NormalizeRef`·`RefPath` 인코딩(`tags/v 1` → `tags/v%201`)
- **선행 단위**: 1

## 단위 3. GitHubRefTools + 등록

- **대상**: src `Tools/GitHub/GitHubRefTools.cs`(신규), `GitHubMcpServerBuilderExtensions.cs`, tests `GitHub/GitHubRefToolsTests.cs`(신규), `GitHub/GitHubRegistrationTests.cs`
- **코드 초안**:

```csharp
[McpServerToolType]
public sealed class GitHubRefTools
{
    public GitHubRefTools(IServiceProvider services) : this(services.GetRequiredService<GitHubRestClient>()) { }
    internal GitHubRefTools(GitHubRestClient client) { _client = client; }

    [McpServerTool(Name = "github_create_lightweight_tag", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = true)]
    [Description("지정한 GitHub 인스턴스의 저장소에 커밋을 가리키는 가벼운 태그(refs/tags/{tag})를 만든다. Contents 쓰기 권한이 필요하다.")]
    public async Task<string> CreateLightweightTag(string? instance = null, string? owner = null, string? repo = null,
        string? tag = null, string? sha = null, CancellationToken cancellationToken = default)
    {
        (GitHubInstance target, string repoPath) = Target(instance, owner, repo);
        string t = Required(tag, nameof(tag)), s = Required(sha, nameof(sha));
        return (await _client.SendAsync(target, HttpMethod.Post, repoPath + "git/refs",
            new JsonObject { ["ref"] = $"refs/tags/{t}", ["sha"] = s }, cancellationToken).ConfigureAwait(false))!;
    }

    [McpServerTool(Name = "github_create_annotated_tag", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = true)]
    public async Task<string> CreateAnnotatedTag(/* instance, owner, repo, tag, sha, message, taggerName, taggerEmail */)
    {
        // taggerName/taggerEmail은 둘 다 있거나 둘 다 없어야 한다
        string tagJson = (await _client.SendAsync(target, HttpMethod.Post, repoPath + "git/tags", tagBody, ct))!;
        string objectSha = JsonNode.Parse(tagJson)!["sha"]!.GetValue<string>();
        string refJson;
        try { refJson = (await _client.SendAsync(target, HttpMethod.Post, repoPath + "git/refs", new JsonObject { ["ref"] = $"refs/tags/{t}", ["sha"] = objectSha }, ct))!; }
        catch (McpException ex) { throw new McpException($"{ex.Message} (tag object {objectSha} was created without a ref)", ex); }
        return new JsonObject { ["tag"] = JsonNode.Parse(tagJson), ["ref"] = JsonNode.Parse(refJson) }.ToJsonString();
    }

    [McpServerTool(Name = "github_update_ref", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
    public async Task<string> UpdateRef(/* instance, owner, repo, ref, sha, bool force = false */)
        => (await _client.SendAsync(target, HttpMethod.Patch, repoPath + "git/refs/" + GitHubRestClient.RefPath(r), new JsonObject { ["sha"] = s, ["force"] = force }, ct))!;

    [McpServerTool(Name = "github_delete_ref", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
    public async Task<string> DeleteRef(/* instance, owner, repo, ref */)
    {
        await _client.SendAsync(target, HttpMethod.Delete, repoPath + "git/refs/" + GitHubRestClient.RefPath(r), null, ct).ConfigureAwait(false);
        return new JsonObject { ["deleted"] = "refs/" + GitHubRestClient.NormalizeRef(r) }.ToJsonString();
    }

    private static string Required(string? value, string name)
        => string.IsNullOrWhiteSpace(value) ? throw new McpException($"{name} is required.") : value.Trim();
}
```

```csharp
// WithGitHubTools
builder.Services.AddSingleton(sp => new GitHubRestClient(sp.GetRequiredService<GitHubOptions>()));
builder.WithTools<GitHubTools>();
builder.WithTools<GitHubRefTools>();
```

- **검증**: 등록 Tool 7개·주석(update/delete 파괴적), 동시 등록(Jenkins·Redmine) 이름 충돌 없음, Tool별 메서드·URL·본문(가짜 핸들러), 주석 태그 두 요청 순서·② 실패 시 객체 sha 포함, 삭제 `{"deleted":"refs/tags/x"}`, 입력 누락은 요청 없음, `taggerName`만 주면 오류
- **선행 단위**: 2

## 단위 4. ReadMe·실제 확인

- **대상**: `ReadMe.md`(GitHub 등록 예·옵션 표에 `ApiEndpoint`, 호환성 주의, Tool 명세 4개)
- **검증**:
  - `dotnet build`·`dotnet test` 전체 통과
  - 테스트 도구로 실제 GitHub(`SeongSikChae/Biz.Bizadm.MCP.ToolKit`)에서 가벼운 태그 생성 → ref 이동 → 삭제, 주석 태그 생성 → 삭제. PAT가 Contents Read and write일 때 수행하며, 만든 테스트 태그는 모두 지우고 남지 않았는지 확인한다
- **선행 단위**: 3

## 순서

1 → 2 → 3 → 4. 단위별 구현·검증. 버전은 #2(`github-release-tools`)까지 끝낸 뒤 1.4.0으로 올린다.
