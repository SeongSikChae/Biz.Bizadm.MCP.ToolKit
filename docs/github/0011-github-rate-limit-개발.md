---
status: done
category: github
feature: github-rate-limit
phase: 개발
---

# github-rate-limit 개발

선행: 없음(사용자 지시 「바로 구현」으로 방안·설계 생략)

경로 기준: `src/Biz.Bizadm.MCP.ToolKit/`(src), `tests/Biz.Bizadm.MCP.ToolKit.Tests/`(tests)

## 목표

등록한 GitHub 인스턴스의 Token으로 `GET {ApiEndpoint}rate_limit`을 호출해 GitHub 응답을 그대로 돌려주는 MCP Tool `github_get_rate_limit`을 추가한다. `curl -H "Authorization: Bearer <토큰>" https://api.github.com/rate_limit`과 같은 결과를 MCP로 얻는 것이 목적이다.

- GitHub MCP Server(`github_call_tool`)에는 rate limit 조회 Tool이 없다.
- `GET /rate_limit` 호출은 GitHub 기본(primary) 한도에 포함되지 않으므로, 한도를 초과한 상태에서도 리셋 시각을 확인할 수 있다.
- GitHub Enterprise Server는 rate limit이 꺼져 있으면 `404`를 돌려줄 수 있다. 이 경우 기존 오류 형식(`... failed (HTTP 404): ...`)으로 알린다.

비범위: 임의 URL·헤더를 호출하는 범용 HTTP Tool, 응답 가공·요약, 버전 올리기·pack.

재사용: `GitHubRestClient.SendAsync`(헤더·Token·오류 형식), `GitHubRecordingHandler`, `GitHubRestClientTests.Options()`, `GitHubRefTools`의 생성자·DI 패턴.

## 단위 1. GitHubRestClient에 rate limit 조회 추가

- **대상**: src `GitHub/GitHubRestClient.cs`(`GetRateLimitAsync`), tests `GitHub/GitHubRestClientTests.cs`
- **코드 초안**:

```csharp
internal const string RateLimitPath = "rate_limit";

/// <summary><c>GET rate_limit</c>으로 Token 사용자의 rate limit 상태를 조회하고 GitHub 응답 본문을 반환한다.</summary>
/// <remarks>이 호출은 GitHub 기본 한도에 포함되지 않는다.</remarks>
public async Task<string> GetRateLimitAsync(GitHubInstance instance, CancellationToken cancellationToken)
    => await SendAsync(instance, HttpMethod.Get, RateLimitPath, null, cancellationToken).ConfigureAwait(false)
        ?? throw new McpException($"GitHub '{instance.Name}' GET {RateLimitPath} returned no content.");
```

- **검증**: `GetRateLimitAsync`가 `GET https://api.github.com/rate_limit`(GHE는 `https://ghe.example/api/v3/rate_limit`)을 본문 없이 보내고 `Authorization: Bearer`·`Accept`·`X-GitHub-Api-Version` 헤더를 싣는지, 응답 본문을 그대로 반환하는지, 빈 본문이면 `returned no content` 오류인지, `404`면 기존 오류 형식이고 Token이 메시지에 없는지
- **선행 단위**: 없음

## 단위 2. github_get_rate_limit Tool과 등록

- **대상**: src `Tools/GitHub/GitHubRateLimitTools.cs`(신규), src `GitHubMcpServerBuilderExtensions.cs`, tests `GitHub/GitHubRateLimitToolsTests.cs`(신규), tests `GitHub/GitHubRegistrationTests.cs`
- **코드 초안**:

```csharp
/// <summary>GitHub REST API로 Token 사용자의 rate limit 상태를 조회하는 Tool. GitHub MCP Server가 노출하지 않는 기능이다.</summary>
[McpServerToolType]
public sealed class GitHubRateLimitTools
{
    private readonly GitHubRestClient _client;

    public GitHubRateLimitTools(IServiceProvider services)
        : this(services.GetRequiredService<GitHubRestClient>()) { }

    internal GitHubRateLimitTools(GitHubRestClient client) => _client = client;

    [McpServerTool(Name = "github_get_rate_limit", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("지정한 GitHub 인스턴스의 Token으로 GET rate_limit을 호출해 resources(core, search, graphql 등)별 limit·used·remaining·reset(UTC epoch 초)을 담은 GitHub 응답을 그대로 반환한다. 이 호출은 GitHub 기본 한도에 포함되지 않는다.")]
    public Task<string> GetRateLimit(
        [Description("GitHub 인스턴스 이름(필수). github_list_instances로 확인")] string? instance = null,
        CancellationToken cancellationToken = default)
        => _client.GetRateLimitAsync(_client.Resolve(instance), cancellationToken);
}
```

```csharp
// GitHubMcpServerBuilderExtensions.WithGitHubTools
builder.WithTools<GitHubReleaseTools>();
builder.WithTools<GitHubRateLimitTools>();
```

- `WithGitHubTools` XML 주석의 Tool 목록에 `REST rate limit Tool(github_get_rate_limit)`을 덧붙인다.
- **검증**: Tool이 이름으로 인스턴스를 찾아 `GET rate_limit` 1회를 보내고 응답을 그대로 반환하는지, `instance` 누락·미등록이면 요청 없이 기존 형식(`instance is required. Registered instances: ...`, `Unknown GitHub instance '...'`) 오류인지. 등록 테스트: Tool 이름 목록에 `github_get_rate_limit` 추가, 힌트 `ReadOnly`·`Idempotent`·`OpenWorld` true, `Destructive` false, 입력 스키마에 `instance`
- **선행 단위**: 1

## 단위 3. README·전체 검증

- **대상**: `README.md`(204행 근처 제공 Tool 표에 한 줄, `GitHub 릴리스 (REST)` 절 뒤에 `GitHub rate limit (REST) — Tools.GitHub.GitHubRateLimitTools` 절)
- **README 절 초안**:

```markdown
### GitHub rate limit (REST) — `Tools.GitHub.GitHubRateLimitTools`

| Tool | 파라미터 | REST | 힌트 | 반환 |
| --- | --- | --- | --- | --- |
| `github_get_rate_limit` | `instance`(필수) | `GET rate_limit` | 읽기 전용 · 멱등 · 외부 접근 | GitHub rate limit 응답 그대로 |

- 인스턴스 Token 사용자의 한도다. `reset`은 UTC epoch 초다. 이 호출은 기본 한도에 포함되지 않는다.
- GitHub Enterprise Server에서 rate limit이 꺼져 있으면 `HTTP 404` 오류가 날 수 있다.
- 오류 형식·Token 비노출·재시도 없음은 태그·ref Tool과 같다.
```

- **검증**: `dotnet build`(솔루션) 종료 코드 0, `dotnet test`(tests 프로젝트) 전체 통과. 실제 GitHub 호출은 배포된 Bizadm MCP 서버에 새 버전이 올라간 뒤 확인할 수 있으므로 이번 범위에서는 미검증으로 보고한다.
- **선행 단위**: 2

## 순서

1 → 2 → 3. 단위별 구현·검증, 단위마다 커밋 여부를 사용자에게 묻는다. 버전 올리기는 별도 요청 시 한다.
