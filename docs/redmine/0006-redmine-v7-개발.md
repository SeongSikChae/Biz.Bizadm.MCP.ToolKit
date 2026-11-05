---
status: done
category: redmine
feature: redmine-v7
phase: 개발
---

# redmine-v7 개발

선행: `0004-redmine-v7-방안.md`, `0005-redmine-v7-설계.md`

공통 검증 명령:
- 빌드: `dotnet build Biz.Bizadm.MCP.ToolKit.slnx`
- 테스트: `dotnet test tests/Biz.Bizadm.MCP.ToolKit.Tests`

각 단위는 필터 테스트가 통과한 뒤 전체 테스트를 실행한다. 기존 Redmine 테스트 파일은 수정하지 않는다.

## U1. 옵션·enum·등록 검증

- **대상**
  - `Redmine/RedmineVersion.cs`, `Redmine/RedmineTextFormatting.cs`(신규)
  - `RedmineOptions.cs`
  - `RedmineMcpServerBuilderExtensions.WithRedmineTools`
- **코드 초안**

```csharp
/// <summary>대상 Redmine 서버 버전</summary>
public enum RedmineVersion { V2_2, V7_0 }

/// <summary>Redmine 서버 텍스트 서식(관리 → 설정 → 일반 → 텍스트 서식)</summary>
public enum RedmineTextFormatting { Textile, CommonMark }

// RedmineOptions
public RedmineVersion Version { get; set; } = RedmineVersion.V2_2;
public RedmineTextFormatting TextFormatting { get; set; } = RedmineTextFormatting.Textile;

// WithRedmineTools: 기존 ApiKey 검증 뒤
if (!Enum.IsDefined(options.Version))
    throw new ArgumentException("Version is not a supported Redmine version", nameof(configure));
if (!Enum.IsDefined(options.TextFormatting))
    throw new ArgumentException("TextFormatting is not supported", nameof(configure));
if (options.Version == RedmineVersion.V2_2 && options.TextFormatting == RedmineTextFormatting.CommonMark)
    throw new ArgumentException("Redmine 2.2 supports only Textile", nameof(configure));
// ... 공통 5개 WithTools
if (options.Version == RedmineVersion.V7_0) { /* U10에서 V7 세트 */ }
else { builder.WithTools<IssueTools>(); /* RelationTools, TimeEntryTools, TextGuideTools */ }
```

- **검증**: 신규 `RedmineV7RegistrationTests`
  - 기본값이 `V2_2`/`Textile`이다.
  - `V2_2`와 `CommonMark` 조합, 정의되지 않은 enum 값은 `ArgumentException`이다.
  - 기존 `RedmineRegistrationTests`가 그대로 통과한다(27개).
- **선행 단위**: 없음

## U2. RedmineClient.PatchAsync·PostBodyAsync·RedmineJson helper·RedminePaths·CustomFieldValue

- **대상**
  - `RedmineClient.PatchAsync`, `RedmineClient.PostBodyAsync`(최상위 Body POST, `PutBodyAsync`와 같은 형태)
  - `Redmine/RedminePaths.cs`(internal, 신규) — `Project(projectId, suffix)`, `EnsurePositive(value, name)`. 코드는 U7 초안 참고
  - `RedmineJson.Set(bool?)`, `SetIdArray`, `SetCustomFields`
  - `Redmine/CustomFieldValue.cs`(신규)
- **코드 초안**

```csharp
public Task<string> PatchAsync(string path, string wrapper, JsonObject payload, CancellationToken cancellationToken)
{
    return SendWrappedAsync(HttpMethod.Patch, path, wrapper, payload, cancellationToken);
}

/// <summary>Redmine 사용자 정의 필드 값. Value(단일)나 Values(다중) 중 하나만 지정한다.</summary>
public sealed record CustomFieldValue(
    [property: Description("사용자 정의 필드 ID")] int Id,
    [property: Description("단일 값")] string? Value = null,
    [property: Description("다중 값")] IReadOnlyList<string>? Values = null);

public static void Set(JsonObject target, string name, bool? value)
{
    if (value is not null)
        target[name] = value.Value;
}

public static void SetIdArray(JsonObject target, string name, IReadOnlyList<int>? ids)
{
    if (ids is null || ids.Count == 0)
        return;
    target[name] = new JsonArray(ids.Select(id => (JsonNode)id).ToArray());
}

public static void SetCustomFields(JsonObject target, IReadOnlyList<CustomFieldValue>? fields)
{
    // null·빈 목록은 생략. 항목 null, Id<1, Value·Values 둘 다/둘 다 없음 → McpException
    // {"id":n,"value":"x"} 또는 {"id":n,"value":["a","b"]}
}
```

- **검증**: 신규 `RedmineV7ClientJsonTests`
  - PATCH 메서드와 `{"attachment":{...}}` Body를 확인한다.
  - `PostBodyAsync`가 POST 메서드와 최상위 Body 그대로인지 확인한다.
  - `RedminePaths.Project`가 trim·이스케이프하고, 빈 값과 0 이하 ID는 `McpException`인지 확인한다.
  - bool `false`도 Body에 들어간다.
  - ID 배열, custom_fields 단일·다중 값의 Body를 확인한다.
  - custom_fields 잘못된 입력은 `McpException`이다.
- **선행 단위**: 없음

## U3. 공용 Body helper 추출

- **대상**
  - `Redmine/IssueBody.cs`, `Redmine/TimeEntryBody.cs`(internal, 신규)
  - `IssueTools.BuildIssue`·`GetParentProjectIdAsync`, `TimeEntryTools.BuildTimeEntry` → 새 helper 호출로 바꾼다
- **코드 초안**

```csharp
internal static class IssueBody
{
    public static JsonObject Build(string? projectId, string? subject, string? description, int? trackerId,
        int? statusId, int? priorityId, int? assignedToId, int? parentIssueId, string? startDate,
        string? dueDate, double? estimatedHours, int? doneRatio, string? notes,
        IReadOnlyList<UploadReference>? uploads)
    {
        JsonObject issue = new();
        RedmineJson.SetId(issue, "project_id", projectId);
        // ... 기존 IssueTools.BuildIssue 순서 그대로
        RedmineJson.SetUploads(issue, uploads);
        return issue;
    }

    public static async Task<string> GetParentProjectIdAsync(RedmineClient redmine, int parentIssueId, CancellationToken ct)
    { /* 기존 IssueTools 구현 이동 */ }
}
```

- **검증**: 기존 `IssueToolsTests`, `Redmine22ContractTests` 등 Redmine 테스트 전체가 수정 없이 통과한다.
- **선행 단위**: 없음

## U4. IssueToolsV7·RelationToolsV7

- **대상**: `Tools/Redmine/V7/IssueToolsV7.cs`, `RelationToolsV7.cs`
- **코드 초안**

```csharp
namespace Biz.Bizadm.MCP.ToolKit.Tools.Redmine.V7;

[McpServerToolType]
public sealed class IssueToolsV7
{
    [McpServerTool(Name = "list_issues", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("Redmine 이슈 목록을 조회한다.")]
    public Task<string> ListIssues(
        string? projectId = null, string? statusId = null, string? assignedToId = null, int? trackerId = null,
        [Description("하위 프로젝트 ID 또는 !*(하위 제외)")] string? subprojectId = null,
        [Description("부모 일감 ID")] int? parentId = null,
        [Description("일감 ID 쉼표 목록. 예: 1,2,3")] string? issueIds = null,
        string? authorId = null, int? categoryId = null, string? fixedVersionId = null,
        [Description("생성일 조건. 예: >=2026-01-01, ><2026-01-01|2026-01-31")] string? createdOn = null,
        string? updatedOn = null, int? queryId = null,
        [Description("정렬. 예: updated_on:desc,id")] string? sort = null,
        [Description("attachments, relations 쉼표 목록")] string? include = null,
        int? limit = null, int? offset = null, CancellationToken cancellationToken = default)
        => _redmine.GetPagedAsync("issues.json", limit, offset, cancellationToken,
            ("project_id", projectId), ("status_id", statusId), /* ... */ ("issue_id", issueIds),
            ("created_on", createdOn), ("query_id", queryId?.ToString(CultureInfo.InvariantCulture)),
            ("sort", sort), ("include", include));

    // create_issue/create_subtask/update_issue: IssueBody.Build(...) 결과에 7.0 필드 추가
    private static void ApplyV7(JsonObject issue, bool? isPrivate, int? categoryId, int? fixedVersionId,
        IReadOnlyList<CustomFieldValue>? customFields, IReadOnlyList<int>? watcherUserIds, bool? privateNotes)
    {
        RedmineJson.Set(issue, "is_private", isPrivate);
        RedmineJson.Set(issue, "category_id", categoryId);
        RedmineJson.Set(issue, "fixed_version_id", fixedVersionId);
        RedmineJson.SetCustomFields(issue, customFields);
        RedmineJson.SetIdArray(issue, "watcher_user_ids", watcherUserIds);
        RedmineJson.Set(issue, "private_notes", privateNotes);
    }

    [McpServerTool(Name = "add_issue_watcher", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = true)]
    public Task<string> AddIssueWatcher(int issueId, int userId, CancellationToken cancellationToken = default)
        => _redmine.PostBodyAsync($"issues/{issueId}/watchers.json", new JsonObject { ["user_id"] = userId }, cancellationToken);
    // delete_issue: DELETE issues/{id}.json, remove_issue_watcher: DELETE issues/{id}/watchers/{userId}.json
}
```

- `add_issue_watcher`는 최상위 Body라서 U2의 `RedmineClient.PostBodyAsync`를 쓴다. ID 검사는 `RedminePaths.EnsurePositive`를 쓴다.
- `RelationToolsV7.RelationTypes`는 기존 7개에 `copied_to`, `copied_from`을 더한다. delay 규칙은 기존과 같다.
- **검증**: 신규 `IssueToolsV7Tests`, `RelationToolsV7Tests`
  - `list_issues` 쿼리 키를 확인한다.
  - 생성·수정 Body의 7.0 필드를 확인한다.
  - 하위 일감의 부모 프로젝트 상속을 확인한다.
  - watcher·delete의 메서드·경로를 확인한다.
  - `copied_to`를 허용한다.
  - ID가 0 이하이면 요청을 보내지 않는다.
- **선행 단위**: U2, U3

## U5. TimeEntryToolsV7·JournalTools

- **대상**: `V7/TimeEntryToolsV7.cs`, `V7/JournalTools.cs`
- **코드 초안**

```csharp
// list_time_entries: 기존 필터 + ("user_id", userId)  — userId는 ID 또는 me
// create_time_entry: TimeEntryBody.Build(...) 후 RedmineJson.Set(entry, "user_id", userId)
[McpServerTool(Name = "delete_time_entry", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
public Task<string> DeleteTimeEntry(int id, CancellationToken cancellationToken = default)
    => _redmine.DeleteAsync($"time_entries/{id}.json", cancellationToken);

[McpServerTool(Name = "update_journal", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
[Description("Redmine 일감 댓글(journal)을 수정한다. 댓글 ID는 get_issue(include=journals)의 journals[].id이다. 본인 댓글은 edit_own_issue_notes, 남의 댓글은 edit_issue_notes 권한이 필요하다. 성공 시 본문이 비어 있다. " + RedmineToolDescriptions.Mutating)]
public Task<string> UpdateJournal(int id, string? notes = null, bool? privateNotes = null, CancellationToken cancellationToken = default)
{
    JsonObject journal = new();
    if (notes is not null) journal["notes"] = notes;   // 빈 문자열 허용
    RedmineJson.Set(journal, "private_notes", privateNotes);
    // Count==0 → McpException
    return _redmine.PutAsync($"journals/{id}.json", "journal", journal, cancellationToken);
}
```

- **검증**: 신규 `TimeEntryToolsV7Tests`, `JournalToolsTests`
  - `user_id` 쿼리·Body를 확인한다.
  - delete 경로를 확인한다.
  - journal Body와 빈 notes 전송을 확인한다.
  - 필드가 없으면 예외다.
- **선행 단위**: U2, U3

## U6. TextGuideToolsV7

- **대상**: `V7/TextGuideToolsV7.cs`
- **코드 초안**

```csharp
public sealed class TextGuideToolsV7
{
    private readonly RedmineTextFormatting _format;
    public TextGuideToolsV7(RedmineOptions options) => _format = options.TextFormatting;

    [McpServerTool(Name = "get_text_guide", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("이 Redmine 서버의 텍스트 서식(Textile 또는 CommonMark)에 맞는 문법과 일감, 댓글, 위키 본문 템플릿을 반환한다. 설명, 댓글, 위키 본문을 쓰기 전에 호출한다.")]
    public string GetTextGuide([Description("issue, comment, wiki 중 하나")] string target)
    {
        (string issue, string comment, string wiki, string syntax) = _format == RedmineTextFormatting.CommonMark
            ? (CommonMarkIssue, CommonMarkComment, CommonMarkWiki, CommonMarkSyntax)
            : (TextileIssue, TextileComment, TextileWiki, TextileSyntax);
        // target switch → guide + "\n" + syntax + "\n" + SharedLinksAndMacros
    }

    private const string CommonMarkSyntax = """
        이 서버의 본문 문법은 CommonMark Markdown(GFM)이다. Textile은 쓰지 않는다.
        **굵게**  *기울임*  ~~취소선~~  `코드`
        # 제목  ## 절  - 목록  1. 번호 목록  > 인용  - [ ] 할 일
        | 표 | 셀 |
        |---|---|
        ```ruby
        코드
        ```
        """;
}
```

- 댓글 가이드(두 서식 공통)에는 "기존 댓글은 update_journal로 고친다"를 넣는다.
- **검증**: 신규 `TextGuideToolsV7Tests`
  - Textile 옵션이면 `h2.`가 있고 `**`가 없다.
  - CommonMark 옵션이면 `## `와 코드 펜스가 있고 `h2.`가 없다.
  - 두 서식 모두 `#124`, `{{toc}}`, `update_journal`이 있다.
  - target이 잘못되면 예외다.
- **선행 단위**: U1

## U7. VersionTools·IssueCategoryTools·MembershipTools

- **대상**: `V7/VersionTools.cs`, `V7/IssueCategoryTools.cs`, `V7/MembershipTools.cs`
- **코드 초안**

```csharp
// 프로젝트 경로 helper(U2에서 추가한 internal static RedminePaths)
internal static string Project(string projectId, string suffix)
{
    if (string.IsNullOrWhiteSpace(projectId)) throw new McpException("projectId is required.");
    return $"projects/{Uri.EscapeDataString(projectId.Trim())}/{suffix}";
}
internal static void EnsurePositive(int value, string name) { if (value < 1) throw new McpException($"{name} must be positive."); }

[McpServerTool(Name = "create_version", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = true)]
public Task<string> CreateVersion(string projectId, string name,
    [Description("open, locked, closed")] string? status = null,
    [Description("none, descendants, hierarchy, tree, system")] string? sharing = null,
    [Description("완료 기한. yyyy-MM-dd")] string? dueDate = null, string? description = null,
    string? wikiPageTitle = null, CancellationToken cancellationToken = default)
{
    JsonObject version = new();
    RedmineJson.Set(version, "name", name);
    RedmineJson.Set(version, "status", status);
    RedmineJson.Set(version, "sharing", sharing);
    RedmineJson.Set(version, "due_date", dueDate);
    // ...
    return _redmine.PostAsync(RedminePaths.Project(projectId, "versions.json"), "version", version, cancellationToken);
}

// delete_issue_category: 쿼리 reassign_to_id 선택
=> _redmine.DeleteAsync(RedmineClient.BuildRelativeUri($"issue_categories/{id}.json",
       [("reassign_to_id", reassignToId?.ToString(CultureInfo.InvariantCulture))]), cancellationToken);

// create_membership: {"membership":{"user_id":n,"role_ids":[..]}} — roleIds 비면 McpException
```

- `status`·`sharing` 값은 Redmine 서버가 검증하므로 클라이언트에서 따로 검증하지 않는다.
- 구성원의 `user_id`는 그룹 ID도 받는다(Description에 표기).
- **검증**: 신규 `ProjectStructureToolsTests`
  - Tool 15개의 메서드·경로·Body wrapper를 확인한다.
  - `reassign_to_id` 쿼리를 확인한다.
  - 빈 `projectId`, ID 0 이하, 수정 필드 없음은 요청 없이 예외다.
- **선행 단위**: U2

## U8. SearchTools·PrincipalTools·CustomFieldTools·ProjectContentTools

- **대상**: `V7/SearchTools.cs`, `PrincipalTools.cs`, `CustomFieldTools.cs`, `ProjectContentTools.cs`
- **코드 초안**

```csharp
[McpServerTool(Name = "search", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
[Description("Redmine 전체 또는 프로젝트 안에서 검색한다.")]
public Task<string> Search(string query, string? projectId = null,
    [Description("all, my_projects, subprojects")] string? scope = null,
    bool? allWords = null, bool? titlesOnly = null,
    [Description("검색 대상 쉼표 목록: issues, news, documents, changesets, wiki_pages, messages, projects")] string? types = null,
    int? limit = null, int? offset = null, CancellationToken cancellationToken = default)
{
    if (string.IsNullOrWhiteSpace(query)) throw new McpException("query is required.");
    string path = projectId is null ? "search.json" : RedminePaths.Project(projectId, "search.json");
    // 7.0.3 SearchController: all_words는 값이 없으면 true, 값이 있으면 present? 판정이라 "0"도 true다.
    // false는 빈 값(all_words=)으로만 보낼 수 있는데 BuildRelativeUri는 빈 값을 버린다.
    // 그래서 쿼리 문자열 전체를 직접 만들고 GetAsync(path, ct)로 보낸다(query 없는 호출은 경로를 그대로 쓴다).
    string uri = RedmineClient.BuildRelativeUri(path, [("q", query), ("scope", scope),
        ("titles_only", titlesOnly == true ? "1" : null), /* types 각 항목 → (type, "1") */
        ("limit", RedmineClient.NormalizeLimit(limit).ToString(CultureInfo.InvariantCulture)),
        ("offset", RedmineClient.NormalizeOffset(offset).ToString(CultureInfo.InvariantCulture))]);
    if (allWords == false) uri += "&all_words=";
    return _redmine.GetAsync(uri, cancellationToken);
}
// list_queries(paged), list_roles, get_role, list_groups, get_group(include=users,memberships), get_my_account,
// list_custom_fields, list_project_files, list_news(projectId 선택, paged) — 모두 GetAsync/GetPagedAsync 한 줄
```

- **검증**: 신규 `LookupToolsTests`
  - search 경로(전체/프로젝트), types 쿼리를 확인한다.
  - `allWords=false`이면 `all_words=`(빈 값), true·null이면 생략인지 확인한다.
  - `titlesOnly=true`이면 `titles_only=1`인지 확인한다.
  - 나머지 조회 9개의 경로·쿼리를 확인한다.
  - 빈 query는 예외다.
- **선행 단위**: U2(`RedminePaths`)

## U9. WikiExtraTools·AttachmentManageTools

- **대상**: `V7/WikiExtraTools.cs`, `V7/AttachmentManageTools.cs`
- **코드 초안**

```csharp
// get_wiki_page_version: GET projects/{p}/wiki/{title}/{version}.json  (version >= 1)
// delete_wiki_page: DELETE projects/{p}/wiki/{title}.json — todo 옵션은 두지 않는다.
//   7.0.3 WikiController#destroy는 API 요청에 todo가 없으면 하위 페이지를 지우지 않고 부모 연결만 끊는다.
//   Description: "하위 페이지는 삭제되지 않고 최상위 페이지가 된다."
[McpServerTool(Name = "update_attachment", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
public Task<string> UpdateAttachment(int id, string? filename = null, string? description = null, CancellationToken cancellationToken = default)
{
    JsonObject attachment = new();
    RedmineJson.Set(attachment, "filename", filename);
    RedmineJson.Set(attachment, "description", description);
    // Count==0 → McpException
    return _redmine.PatchAsync($"attachments/{id}.json", "attachment", attachment, cancellationToken);
}
```

- 위키 경로는 `RedminePaths.Project(projectId, $"wiki/{Uri.EscapeDataString(title.Trim())}...")`로 만든다.
- **검증**: 신규 `WikiAttachmentV7ToolsTests`
  - 경로 이스케이프(공백·한글 제목)를 확인한다.
  - PATCH Body를 확인한다.
  - version이 0 이하이면 예외다.
  - `delete_wiki_page` Description에 하위 페이지 안내가 있는지 확인한다.
- **선행 단위**: U2

## U10. V7 등록·annotation

- **대상**: `RedmineMcpServerBuilderExtensions.WithRedmineTools`의 V7 분기
- **코드 초안**

```csharp
if (options.Version == RedmineVersion.V7_0)
{
    builder.WithTools<IssueToolsV7>();
    builder.WithTools<RelationToolsV7>();
    builder.WithTools<TimeEntryToolsV7>();
    builder.WithTools<TextGuideToolsV7>();
    builder.WithTools<JournalTools>();
    builder.WithTools<WikiExtraTools>();
    builder.WithTools<VersionTools>();
    builder.WithTools<IssueCategoryTools>();
    builder.WithTools<MembershipTools>();
    builder.WithTools<SearchTools>();
    builder.WithTools<PrincipalTools>();
    builder.WithTools<CustomFieldTools>();
    builder.WithTools<ProjectContentTools>();
    builder.WithTools<AttachmentManageTools>();
}
```

- XML 주석을 갱신한다. 2.2는 27개, 7.0은 61개다.
- **검증**: `RedmineV7RegistrationTests`
  - V7은 61개이고 이름이 중복되지 않으며 기대 이름 목록과 같다.
  - annotation이 설계 표와 같다.
  - `TextGuideToolsV7`이 DI로 옵션 서식을 받는다(CommonMark 옵션일 때 MCP 호출 결과로 확인).
  - V2_2는 기존 27개 그대로다.
- **선행 단위**: U1, U4~U9

## U11. ReadMe

- **대상**: `ReadMe.md` Redmine 절
- **코드 초안**

```csharp
builder.Services.AddMcpServer().WithRedmineTools(o =>
{
    o.ApiEndpoint = "https://techlab.example/";
    o.ApiKey = apiKey;
    o.Version = RedmineVersion.V7_0;              // 기본 V2_2
    o.TextFormatting = RedmineTextFormatting.Textile; // 서버 텍스트 서식과 같게
});
```

- 2.2 표기를 버전 옵션 설명으로 바꾸고, 7.0 추가 Tool 목록을 적는다.
- **검증**: 문서 내 Tool 수(27/61)와 이름이 등록 테스트 목록과 같은지 대조한다.
- **선행 단위**: U10

## 순서

U1 → U2 → U3 → U4 → U5 → U6 → U7 → U8 → U9 → U10 → U11

마지막에 전체 빌드·테스트를 실행하고, `review.required`에 따른 독립 검토(가정·회귀·범위 이탈)를 한다.
