---
status: done
category: redmine
feature: redmine-tools
phase: 개발
---

# redmine-tools 개발

선행: `0001-redmine-tools-방안.md`, `0002-redmine-tools-설계.md`
원본: `D:\Git\Server\Redmine.MCP\src\Redmine.MCP.Server`, `D:\Git\Server\Redmine.MCP\tests\Redmine.MCP.Server.Tests`

## 공통 규칙

- 원본 파일의 로직·Description 문구·예외 메시지(영문)를 그대로 옮긴다. 바꾸는 것은 네임스페이스, 접근자, XML 주석, annotation, 예외 타입뿐이다.
- 파일 범위 네임스페이스(`namespace X;`)를 쓴다(ToolKit 관례).
- public 멤버는 한국어 XML 주석을 단다. `dotnet build ... --no-incremental` 경고 0.
- 입력 오류 `ArgumentException`/`InvalidOperationException` → `McpException`. 단 `RedmineOptions` 등록 검증은 AgentResource처럼 `ArgumentException`.
- 테스트는 `tests/Biz.Bizadm.MCP.ToolKit.Tests/Redmine/`, 네임스페이스 `Biz.Bizadm.MCP.ToolKit.Tests.Redmine`. 원본의 `RecordingHandler`/`ScriptedHandler`는 원본처럼 테스트 파일마다 private 클래스로 둔다(구현 중 정정: 공용 파일로 모으지 않고 원본과의 차이를 줄임). 원본에서 `ArgumentException`을 기대하던 단언은 `McpException`으로 바꾼다.

## 단위 1. 기반 코드

**대상**
- `src/Biz.Bizadm.MCP.ToolKit/Biz.Bizadm.MCP.ToolKit.csproj` (`InternalsVisibleTo`)
- `src/Biz.Bizadm.MCP.ToolKit/Redmine/RedmineOptions.cs`, `RedmineClient.cs`, `RedmineApiException.cs`, `UploadReference.cs`, `RedmineJson.cs`, `AttachmentDownload.cs`
- 테스트: `RedmineClientTests.cs`, `RedmineJsonTests.cs`, `AttachmentDownloadTests.cs`

**코드 초안**

```xml
<ItemGroup>
  <InternalsVisibleTo Include="Biz.Bizadm.MCP.ToolKit.Tests" />
</ItemGroup>
```

```csharp
/// <summary>Redmine REST API 요청이 실패했을 때 던지는 예외</summary>
public sealed class RedmineApiException(int statusCode, string responseBody)
    : McpException($"Redmine API request failed with status {statusCode}. {responseBody}")
{
    public int StatusCode { get; } = statusCode;
    public string ResponseBody { get; } = responseBody;
}
```

```csharp
public sealed class RedmineClient
{
    internal const string ApiKeyHeaderName = "X-Redmine-API-Key";
    private readonly HttpClient _httpClient;

    /// <summary>옵션으로 HttpClient를 만들어 Redmine에 연결한다.</summary>
    public RedmineClient(RedmineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _httpClient = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) });
        Configure(_httpClient, options);
    }

    /// <summary>구성된 HttpClient로 Redmine에 연결한다.</summary>
    public RedmineClient(HttpClient httpClient) { ArgumentNullException.ThrowIfNull(httpClient); _httpClient = httpClient; }

    internal static void Configure(HttpClient client, RedmineOptions options)
    {
        client.BaseAddress = CreateBaseAddress(options.ApiEndpoint);   // 끝 '/' 보정
        client.DefaultRequestHeaders.Remove(ApiKeyHeaderName);
        client.DefaultRequestHeaders.TryAddWithoutValidation(ApiKeyHeaderName, options.ApiKey);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    internal static void EnsureSameOrigin(Uri baseAddress, Uri candidate)
    {
        // ... 원본과 동일 비교 ...
            throw new McpException($"Attachment URL origin must match the Redmine API endpoint ({baseAddress.GetLeftPart(UriPartial.Authority)}).");
    }
    // ... GetAsync / GetPagedAsync / PostAsync / PutAsync / PutBodyAsync / DeleteAsync / PostOctetStreamAsync / GetBytesAsync / BuildRelativeUri: 원본 그대로 ...
}
```

`RedmineJson`, `AttachmentDownload`는 `internal static`으로 원본 그대로 옮긴다. `UploadReference`는 원본 record에 XML 주석만 추가한다.

**검증**
- `dotnet build Biz.Bizadm.MCP.ToolKit.slnx --no-incremental` 경고 0
- `dotnet test --filter "FullyQualifiedName~Tests.Redmine"`: 원본 `RedmineClientTests`(BaseAddress 하위 경로, 페이징 쿼리·API 키 헤더, wrapper Body, GetBytes 상대/절대·다른 출처·HTTP 오류, PutBody, Delete, octet-stream), `RedmineJsonTests`, `AttachmentDownloadTests` 통과. 다른 출처는 `McpException`, HTTP 오류는 `RedmineApiException`(McpException 상속 확인 포함)

**선행 단위**: 없음

## 단위 2. Issue·Relation Tool

**대상**
- `src/.../Tools/Redmine/RedmineToolDescriptions.cs`, `IssueTools.cs`, `RelationTools.cs`
- 테스트: `IssueToolsTests.cs`(MCP 라운드트립 포함), `RelationToolsTests.cs`

**코드 초안**

```csharp
namespace Biz.Bizadm.MCP.ToolKit.Tools.Redmine;

/// <summary>Redmine 일감 관계 조회·등록·삭제 Tool</summary>
[McpServerToolType]
public sealed class RelationTools(RedmineClient redmine)
{
    internal static readonly string[] RelationTypes = ["relates", "duplicates", "duplicated", "blocks", "blocked", "precedes", "follows"];

    /// <summary>일감의 관계 목록을 조회한다.</summary>
    [McpServerTool(Name = "list_issue_relations", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("Redmine 일감의 연결된 일감(관계) 목록을 조회한다.")]
    public Task<string> ListIssueRelations([Description("일감 ID")] int issueId, CancellationToken cancellationToken = default)
    {
        EnsurePositive(issueId, nameof(issueId));
        return redmine.GetAsync($"issues/{issueId.ToString(CultureInfo.InvariantCulture)}/relations.json", cancellationToken);
    }
    // ... create_issue_relation(Idempotent=false), delete_issue_relation(Idempotent=true, Destructive=true): 원본 그대로 ...

    private static void EnsurePositive(int value, string name)
    {
        if (value < 1)
            throw new McpException($"{name} must be positive.");
    }
}
```

`IssueTools`도 같은 방식이다. `BuildIssue`, 부모 일감 프로젝트 조회(`create_subtask`), "no fields" 검사는 원본 그대로, 예외만 `McpException`.

**검증**
- 빌드 경고 0
- 원본 `IssueToolsTests`(하위 일감 프로젝트 상속·부모 조회 생략·오류, create/update uploads, 필드 없음 오류, `McpRoundTrip`의 camelCase `uploads` 스키마·snake_case 전송), `RelationToolsTests`(목록, 생성 delay 유무, 잘못된 종류 DataRow, relates의 delay, 0 이하 ID, 삭제) 통과

**선행 단위**: 1

## 단위 3. TimeEntry·Wiki·Project·User·Metadata Tool

**대상**
- `src/.../Tools/Redmine/TimeEntryTools.cs`, `WikiTools.cs`, `ProjectTools.cs`, `UserTools.cs`, `MetadataTools.cs`
- 테스트: `WikiToolsTests.cs`, `Redmine22ContractTests.cs`(AttachmentTools Description도 검사하므로 단위 4에서 함께 실행 — 구현 중 정정)

**코드 초안**

```csharp
[McpServerTool(Name = "put_wiki_page", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
[Description(/* 원본 문구 */ "... " + RedmineToolDescriptions.Mutating)]
public Task<string> PutWikiPage(string projectId, string title, string text, string? comments = null,
    IReadOnlyList<UploadReference>? uploads = null, CancellationToken cancellationToken = default)
{
    // ... 원본 그대로: {wiki_page:{text,comments}, attachments:[...]} 를 PutBodyAsync로 전송 ...
}
```

`list_time_entries`는 `user_id` 필터 없음, Metadata 4개는 매개변수 없음 — 원본 그대로.

**검증**
- 빌드 경고 0
- `WikiToolsTests`(top-level `attachments`, uploads 없음), `Redmine22ContractTests`(time entry 필터에 `user_id` 없음, Description의 2.2 표기 reflection 확인 — 대상 어셈블리를 ToolKit으로, `copied_to` 거부) 통과

**선행 단위**: 1

## 단위 4. Attachment·TextGuide Tool

**대상**
- `src/.../Tools/Redmine/AttachmentTools.cs`, `TextGuideTools.cs`
- 테스트: `AttachmentToolsTests.cs`, `TextGuideToolsTests.cs`

**코드 초안**

```csharp
[McpServerTool(Name = "download_attachment", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
public async Task<string> DownloadAttachment(int? attachmentId = null, int? issueId = null, string? filename = null,
    string? contentUrl = null, string? outputPath = null, CancellationToken cancellationToken = default)
{
    // ... 원본 그대로: id 결정 → attachments/{id}.json → GetBytesAsync → AttachmentDownload 응답 ...
}

[McpServerTool(Name = "get_text_guide", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
public string GetTextGuide([Description("...")] string target) { /* 원본 const 문자열 그대로 */ }
```

`TextGuideTools`는 생성자 매개변수 없는 `public sealed` 인스턴스 클래스(원본과 같음).

**검증**
- 빌드 경고 0
- `AttachmentToolsTests`(id·일감+파일명 다운로드, 중복 파일명, 다른 출처, 디스크 저장, upload 3가지 입력과 잘못된 입력), `TextGuideToolsTests` 통과

**선행 단위**: 1

## 단위 5. 등록 확장

**대상**
- `src/Biz.Bizadm.MCP.ToolKit/RedmineMcpServerBuilderExtensions.cs`
- 테스트: `RedmineRegistrationTests.cs`

**코드 초안**

```csharp
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Redmine Tool을 MCP Server에 등록하는 확장 메서드</summary>
public static class RedmineMcpServerBuilderExtensions
{
    /// <summary>Redmine Tool(9개 클래스, 27개)을 접속 옵션과 함께 등록한다.</summary>
    public static IMcpServerBuilder WithRedmineTools(this IMcpServerBuilder builder, Action<RedmineOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        RedmineOptions options = new();
        configure(options);
        if (!Uri.TryCreate(options.ApiEndpoint?.Trim(), UriKind.Absolute, out Uri? endpoint)
            || endpoint.Scheme is not ("http" or "https"))
            throw new ArgumentException("ApiEndpoint must be an absolute http or https URI", nameof(configure));
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new ArgumentException("ApiKey is required", nameof(configure));

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(sp => new RedmineClient(sp.GetRequiredService<RedmineOptions>()));
        builder.WithTools<IssueTools>();
        // ... RelationTools, TimeEntryTools, WikiTools, ProjectTools, UserTools, MetadataTools, AttachmentTools, TextGuideTools ...
        return builder;
    }
}
```

**검증**
- 빌드 경고 0
- `RedmineRegistrationTests`: 27개 Tool 이름 일치, 설계 annotation 표와 일치, Tool 인스턴스 해석 성공(`RedmineClient` 싱글턴), null 인자·잘못된 ApiEndpoint(상대 경로, ftp)·빈 ApiKey `ArgumentException`

**선행 단위**: 2, 3, 4

## 단위 6. ReadMe

**대상**: `ReadMe.md`

**코드 초안**

```markdown
| 그룹 | 등록 | Tool |
| --- | --- | --- |
| Redmine | `.WithRedmineTools(o => { o.ApiEndpoint = "..."; o.ApiKey = "..."; })` | list_issues, get_issue, ... (27개) |
```

폴더 구조 블록에 `docs/redmine/` 추가. 기존 그룹 행 채우기는 범위 밖(Redmine 행만 추가).

**검증**: 전체 `dotnet test` 통과(기존 테스트 회귀 없음), ReadMe 표의 Tool 이름이 등록 테스트 목록과 같음

**선행 단위**: 5

## 순서

1 → 2 → 3 → 4 → 5 → 6. 단위마다 구현 후 해당 테스트가 통과해야 다음으로 간다.
