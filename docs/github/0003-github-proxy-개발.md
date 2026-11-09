---
status: done
category: github
feature: github-proxy
phase: 개발
---

# github-proxy 개발

선행: `0001-github-proxy-방안.md`, `0002-github-proxy-설계.md`

경로 기준: `src/Biz.Bizadm.MCP.ToolKit/`(src), `tests/Biz.Bizadm.MCP.ToolKit.Tests/`(tests)

## 설계 보정

- `FakeMcpServer`는 이름만 일반화한다. 테스트 연결은 메모리 transport라 HTTP 헤더가 오가지 않으므로 인증 헤더 인자를 받지 않는다. 인증 헤더는 `CreateTransportOptions` 결과의 `AdditionalHeaders`로 검사한다.

## 단위 1. 공통 코어 추출 + Jenkins 이전

Jenkins 풀을 제거하면 Jenkins 코드가 바로 깨지므로 코어 추출과 Jenkins 이전을 한 단위로 한다.

대상:

| 파일 | 변경 |
| --- | --- |
| src `Relay/McpRelayTarget.cs` | 신규 |
| src `Relay/McpRelayConnection.cs` | `Jenkins/JenkinsConnection.cs`를 옮기고 이름 변경 |
| src `Relay/McpRelayPool.cs` | `Jenkins/JenkinsMcpClientPool.cs`를 옮기고 `label`·`McpRelayTarget` 기반으로 변경 |
| src `Relay/McpRelayTools.cs` | `JenkinsTools`의 본문과 `ToArguments`를 옮긴다 |
| src `Relay/McpRelayValidation.cs` | `WithJenkinsTools` 검증 중 공통 부분 |
| src `Tools/Jenkins/JenkinsTools.cs` | keyed 풀 사용, 본문 위임 |
| src `JenkinsMcpServerBuilderExtensions.cs` | 공통 검증 사용, keyed 풀 등록, `ToTarget` |
| tests `Relay/FakeMcpServer.cs` | `Jenkins/FakeJenkinsServer.cs`를 옮기고 이름 변경 |
| tests `Relay/McpRelayPoolTests.cs` | `Jenkins/JenkinsMcpClientPoolTests.cs`를 옮긴다 |
| tests `Jenkins/JenkinsToolsTests.cs`, `JenkinsRegistrationTests.cs` | 내부 타입 참조만 변경 |

코드 초안:

```csharp
// Relay/McpRelayTarget.cs
internal sealed record McpRelayTarget(string Name, string Endpoint, string? Description, IReadOnlyDictionary<string, string> Headers);
```

```csharp
// Relay/McpRelayPool.cs
internal sealed class McpRelayPool : IAsyncDisposable, IDisposable
{
    public McpRelayPool(string label, IEnumerable<McpRelayTarget> targets)
        : this(label, targets, ConnectAsync) { }

    internal McpRelayPool(string label, IEnumerable<McpRelayTarget> targets,
        Func<McpRelayTarget, CancellationToken, Task<McpRelayConnection>> connect)
    {
        Label = label;
        _ordered = [.. targets];
        // ... existing ...
    }

    public string Label { get; }

    public McpRelayTarget Resolve(string? name)
    {
        // ... existing ...
        string label = string.IsNullOrWhiteSpace(name) ? "instance is required." : $"Unknown {Label} instance '{name.Trim()}'.";
        throw new McpException($"{label} Registered instances: {string.Join(", ", _ordered.Select(i => i.Name))}");
    }

    // InvokeAsync: $"{Label} '{target.Name}' returned error {(int)ex.ErrorCode}: {ex.Message}"
    // DescribeFailure(string label, string instanceName, Exception ex): $"{label} '{instanceName}' request failed{status}: {ex.Message}"

    internal static HttpClientTransportOptions CreateTransportOptions(string label, McpRelayTarget target) => new()
    {
        Endpoint = new Uri(target.Endpoint),
        TransportMode = HttpTransportMode.StreamableHttp,
        Name = $"{label.ToLowerInvariant()}-{target.Name}",
        AdditionalHeaders = new Dictionary<string, string>(target.Headers),
    };
}
```

```csharp
// Relay/McpRelayTools.cs
internal static class McpRelayTools
{
    public static string ListInstances(McpRelayPool pool) { /* ... existing JenkinsTools.ListInstances 본문, McpRelayTarget 사용 ... */ }
    public static async Task<string> ListToolsAsync(McpRelayPool pool, string? instance, bool refresh, CancellationToken ct) { /* ... existing ... */ }
    public static Task<CallToolResult> CallToolAsync(McpRelayPool pool, string? instance, string? tool, JsonElement? arguments, CancellationToken ct)
    {
        McpRelayTarget target = pool.Resolve(instance);
        if (string.IsNullOrWhiteSpace(tool))
            throw new McpException("tool is required.");
        return pool.CallToolAsync(target, tool.Trim(), ToArguments(arguments), ct);
    }
    internal static Dictionary<string, object?>? ToArguments(JsonElement? arguments) { /* ... existing ... */ }
}
```

```csharp
// Relay/McpRelayValidation.cs
internal static class McpRelayValidation
{
    /// 목록 비어 있음·null 항목 검사. 메시지는 기존 Jenkins 문자열에 label을 넣은 것
    public static void RequireInstances<T>(string label, IList<T>? instances) where T : class { /* ... */ }

    /// 이름 trim·공백·중복, Endpoint 절대 http/https·UserInfo 검사. (trimmed name, AbsoluteUri) 반환
    public static (string Name, string Endpoint) NameAndEndpoint(string label, HashSet<string> names, string? name, string? endpoint, string credentialHint)
    {
        // ... existing WithJenkinsTools 검사, "Jenkins" → label ...
        // UserInfo 메시지: $"{label} instance '{n}' Endpoint must not contain user info. Use {credentialHint}"
    }
}
```

```csharp
// JenkinsMcpServerBuilderExtensions.cs
builder.Services.AddSingleton(validated);
builder.Services.AddKeyedSingleton(JenkinsTools.PoolKey,
    (sp, _) => new McpRelayPool("Jenkins", sp.GetRequiredService<JenkinsOptions>().Instances.Select(ToTarget)));
builder.WithTools<JenkinsTools>();

internal static McpRelayTarget ToTarget(JenkinsInstance i) => new(i.Name, i.Endpoint, i.Description,
    new Dictionary<string, string> { ["Authorization"] = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{i.User}:{i.ApiToken}")) });
```

```csharp
// Tools/Jenkins/JenkinsTools.cs
internal const string PoolKey = "jenkins";

public JenkinsTools(IServiceProvider services)
    : this(services.GetRequiredKeyedService<McpRelayPool>(PoolKey)) { }

public string ListInstances() => McpRelayTools.ListInstances(_pool);
// ListTools, CallTool: 시그니처·Description 그대로, 본문만 McpRelayTools 위임
```

테스트 변경 예:

```csharp
// JenkinsToolsTests: DI 교체
services.AddKeyedSingleton(JenkinsTools.PoolKey, (sp, _) => new McpRelayPool("Jenkins",
    sp.GetRequiredService<JenkinsOptions>().Instances.Select(JenkinsMcpServerBuilderExtensions.ToTarget),
    (t, ct) => t.Name == "prod" ? fake.ConnectionAsync(ct) : throw new HttpRequestException("Forbidden", null, HttpStatusCode.Forbidden)));

// McpRelayPoolTests: Basic 헤더 검사는 ToTarget + CreateTransportOptions("Jenkins", ...)로
```

검증:

- `dotnet build` 경고·오류 없음
- `dotnet test` 전체 통과. 기존 Jenkins 테스트의 메시지·결과 단언은 바꾸지 않는다(타입 참조만 변경)
- `McpRelayPoolTests`에서 transport `Name`이 `jenkins-prod`, `Authorization`이 기존 Basic 값

선행 단위: 없음

## 단위 2. GitHub 연동

대상:

| 파일 | 변경 |
| --- | --- |
| src `GitHub/GitHubOptions.cs`, `GitHub/GitHubInstance.cs` | 신규 |
| src `GitHubMcpServerBuilderExtensions.cs` | 신규 |
| src `Tools/GitHub/GitHubTools.cs` | 신규 |
| tests `GitHub/GitHubRegistrationTests.cs`, `GitHub/GitHubToolsTests.cs` | 신규 |

코드 초안:

```csharp
// GitHub/GitHubInstance.cs
public sealed class GitHubInstance
{
    public string Name { get; set; } = string.Empty;
    /// <summary>GitHub MCP 주소 전체(예: <c>https://api.githubcopilot.com/mcp/</c>)</summary>
    public string Endpoint { get; set; } = string.Empty;
    /// <summary>Bearer 인증에 쓸 GitHub Personal Access Token</summary>
    public string Token { get; set; } = string.Empty;
    public string? Description { get; set; }
}
```

```csharp
// GitHubMcpServerBuilderExtensions.cs
public static IMcpServerBuilder WithGitHubTools(this IMcpServerBuilder builder, Action<GitHubOptions> configure)
{
    // ... WithJenkinsTools와 같은 흐름 ...
    builder.Services.AddSingleton(validated);
    builder.Services.AddKeyedSingleton(GitHubTools.PoolKey,
        (sp, _) => new McpRelayPool("GitHub", sp.GetRequiredService<GitHubOptions>().Instances.Select(ToTarget)));
    builder.WithTools<GitHubTools>();
    return builder;
}

private static GitHubOptions Validate(GitHubOptions options)
{
    McpRelayValidation.RequireInstances("GitHub", options.Instances);
    HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
    List<GitHubInstance> copies = [];
    foreach (GitHubInstance instance in options.Instances)
    {
        (string name, string endpoint) = McpRelayValidation.NameAndEndpoint("GitHub", names, instance.Name, instance.Endpoint, "Token");
        if (string.IsNullOrWhiteSpace(instance.Token))
            throw new ArgumentException($"GitHub instance '{name}' Token is required", nameof(options));
        copies.Add(new GitHubInstance { Name = name, Endpoint = endpoint, Token = instance.Token.Trim(), Description = /* trim or null */ });
    }
    return new GitHubOptions { Instances = copies };
}

internal static McpRelayTarget ToTarget(GitHubInstance i)
    => new(i.Name, i.Endpoint, i.Description, new Dictionary<string, string> { ["Authorization"] = $"Bearer {i.Token}" });
```

```csharp
// Tools/GitHub/GitHubTools.cs
[McpServerToolType]
public sealed class GitHubTools
{
    internal const string PoolKey = "github";

    [McpServerTool(Name = "github_list_instances", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("등록한 GitHub 인스턴스의 이름, 용도 설명, MCP Endpoint를 조회한다. 다른 github_* Tool의 instance 인자로 이름을 쓴다.")]
    public string ListInstances() => McpRelayTools.ListInstances(_pool);

    // github_list_tools, github_call_tool: JenkinsTools와 같은 주석·인자, 설명의 Jenkins → GitHub
}
```

검증:

- `dotnet build`, `dotnet test` 전체 통과
- 등록: 3개 Tool 이름·주석, 검증 오류(빈 목록, null, 이름 공백·중복, Endpoint 형식·UserInfo, Token 공백) 메시지에 토큰 없음, 검증 복사본 보관
- 헤더: `CreateTransportOptions("GitHub", ToTarget(...))`의 `Authorization`이 `Bearer {token}`, `Name`이 `github-{name}`
- Tool: `github_list_instances`에 Token 없음, 가짜 서버로 `github_list_tools`·`github_call_tool` 결과 전달
- 동시 등록: `WithJenkinsTools` + `WithGitHubTools` + `WithRedmineTools`에서 Tool 이름 충돌 없음, 각 Tool이 자기 풀을 쓴다(GitHub 호출이 Jenkins 가짜 서버로 가지 않음)

선행 단위: 단위 1

## 단위 3. 문서·버전

대상:

- `ReadMe.md`: GitHub 등록 예(`https://api.githubcopilot.com/mcp/`, PAT), 3개 Tool 설명. Jenkins 절과 같은 형식
- src `Biz.Bizadm.MCP.ToolKit.csproj`: `<Version>1.3.0</Version>`, `PackageTags`에 `jenkins;github` 추가

코드 초안:

```xml
<Version>1.3.0</Version>
<PackageTags>mcp;model-context-protocol;mcp-server;tools;redmine;storage;audio;agent-resource;jenkins;github</PackageTags>
```

검증: `dotnet build` 통과, ReadMe 예시의 멤버 이름이 실제 공개 API와 일치

선행 단위: 단위 2

## 순서

1 → 2 → 3. 단위마다 구현 뒤 검증이 통과해야 다음 단위로 간다. 완료 전 독립 검토(가정·회귀·범위 이탈)를 한다.
