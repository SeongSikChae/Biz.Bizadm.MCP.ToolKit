---
status: done
category: agent-resource
feature: agent-resource
phase: 설계
---

# agent-resource 설계

선행: `0001-agent-resource-방안.md`
참고: `D:\Git\Lib\AgentRuleSkillRepository\AgentRuleSkillRepository.MCP.Server\Services\*DocumentService.cs`

## 구조

```
src/Biz.Bizadm.MCP.ToolKit/
  AgentResourceMcpServerBuilderExtensions.cs   # WithAgentResourceTools (신규)
  AgentResource/
    AgentResourceOptions.cs                    # 공개 옵션 (신규)
    AgentResourcePath.cs                       # internal static: 경로 정규화·이탈 방지 (신규)
    Frontmatter.cs                             # internal static: 파싱·본문 추출·생성 (신규)
    AgentResourceFile.cs                       # internal static: BOM 없는 UTF-8 쓰기, 빈 폴더 정리 (신규)
  Tools/AgentResource/
    RuleDocumentTools.cs                       # rule 5개 (신규)
    SkillDocumentTools.cs                      # skill 5개 (신규)
    ToolDocumentTools.cs                       # tool 6개 (신규)
    DocDocumentTools.cs                        # docs 5개 (신규)
  Models/AgentResource/
    *.cs                                       # 결과 record (신규)
tests/Biz.Bizadm.MCP.ToolKit.Tests/AgentResource/
  *Tests.cs                                    # 신규
```

- 기존 Tool·모델·`WithBizadmToolKit()`은 바꾸지 않는다.
- 새 NuGet 패키지는 없다. front matter는 참고 서버처럼 직접 파싱하고 YamlDotNet은 쓰지 않는다.

## 루트 레이아웃

| 종류 | 경로 | 식별자 |
| --- | --- | --- |
| rule | `{RootPath}/rules/**.md` | rules 기준 상대 경로(`dotnet/naming.md`). 확장자가 없으면 `.md`를 붙인다 |
| skill | `{RootPath}/skills/<name>/SKILL.md` | `<name>`. `<name>/SKILL.md`도 받는다 |
| tool | `{RootPath}/tools/<name>/TOOL.md` | `<name>`. `<name>/TOOL.md`도 받는다 |
| docs | `{RootPath}/docs/{category}/NNNN-{feature}-{phase}.md` | `category` + `number` |

- 종류별 폴더가 없으면 목록은 빈 결과를 낸다. 쓰기 Tool은 필요한 폴더를 만든다. 생성자에서는 폴더를 만들지 않는다.

## 등록

```csharp
// AgentResource/AgentResourceOptions.cs
public sealed class AgentResourceOptions
{
    public string RootPath { get; set; } = string.Empty;
}

// AgentResourceMcpServerBuilderExtensions.cs (namespace Microsoft.Extensions.DependencyInjection)
public static IMcpServerBuilder WithAgentResourceTools(
    this IMcpServerBuilder builder, Action<AgentResourceOptions> configure)
{
    ArgumentNullException.ThrowIfNull(builder);
    ArgumentNullException.ThrowIfNull(configure);
    AgentResourceOptions options = new();
    configure(options);
    if (string.IsNullOrWhiteSpace(options.RootPath) || !Path.IsPathFullyQualified(options.RootPath))
        throw new ArgumentException("RootPath must be an absolute path", nameof(configure));
    options.RootPath = Path.GetFullPath(options.RootPath);
    builder.Services.AddSingleton(options);
    builder.WithTools<RuleDocumentTools>().WithTools<SkillDocumentTools>()
           .WithTools<ToolDocumentTools>().WithTools<DocDocumentTools>();
    return builder;
}
```

- 등록할 때 `RootPath`가 비어 있거나 절대 경로가 아니면 `ArgumentException`을 던진다. 폴더가 실제로 있는지는 확인하지 않는다.

## Tool 클래스

```csharp
[McpServerToolType]
public sealed class RuleDocumentTools(AgentResourceOptions options) { ... }
```

- 공개 인스턴스 클래스이고 생성자로 `AgentResourceOptions`만 받는다. SDK `WithTools<T>`가 요청마다 DI로 인스턴스를 만든다.
- 파일 로직은 Tool 클래스 안의 private 메서드와 internal static helper에 둔다. 별도 서비스 클래스는 두지 않는다. 그래서 테스트는 `new RuleDocumentTools(new AgentResourceOptions { RootPath = temp })`로 직접 호출한다.
- 모든 Tool은 `UseStructuredContent = true`와 `OpenWorld = false`를 쓰고, Description은 한국어 한 문장으로 쓴다.

| 분류 | ReadOnly | Idempotent | Destructive |
| --- | --- | --- | --- |
| list / get / guide | true | true | false |
| create / register | false | false | false |
| update | false | true | true |
| delete | false | false | true |

## 인터페이스

### rule (`RuleDocumentTools`)

```csharp
RuleDocumentListResult ListRuleDocuments();                       // list_rule_documents
RuleDocument GetRuleDocument(string name);                         // get_rule_document
AgentResourceWriteResult CreateRuleDocument(string name, string content);   // create_rule_document
AgentResourceWriteResult UpdateRuleDocument(string name, string content);   // update_rule_document
AgentResourceWriteResult DeleteRuleDocument(string name);          // delete_rule_document
```

### skill (`SkillDocumentTools`)

```csharp
SkillDocumentListResult ListSkillDocuments();                      // list_skill_documents
SkillDocument GetSkillDocument(string name);                       // get_skill_document
AgentResourceWriteResult CreateSkillDocument(string name, string content);  // create_skill_document
AgentResourceWriteResult UpdateSkillDocument(string name, string content);  // update_skill_document
AgentResourceWriteResult DeleteSkillDocument(string name);         // delete_skill_document (빈 폴더 삭제)
```

### tool (`ToolDocumentTools`)

```csharp
ToolDocumentListResult ListToolDocuments();                        // list_tool_documents
ToolDocumentUsage GetToolDocumentUsage(string name);               // get_tool_document_usage
ToolRegistrationGuide GetToolDocumentRegistrationGuide();          // get_tool_document_registration_guide
AgentResourceWriteResult RegisterToolDocument(string name, string description, string language, string location, string usage);
AgentResourceWriteResult UpdateToolDocument(string name, string description, string language, string location, string usage);
AgentResourceWriteResult DeleteToolDocument(string name);          // 빈 폴더 삭제, location 대상은 건드리지 않음
```

- TOOL.md는 참고 서버의 `BuildContent`로 만든다. front matter에는 `name`·`description`·`language`·`location`을 넣고 값의 줄바꿈은 공백으로 바꾼다. 본문은 `usage`이고 줄바꿈은 LF로 맞춘다.
- 등록 가이드 내용은 참고 서버를 따르되 `RelatedTools`에는 snake_case Tool 이름을 쓴다.

### docs (`DocDocumentTools`)

```csharp
DocDocumentListResult ListDocDocuments(string? category = null, string? feature = null);   // list_doc_documents
DocDocument GetDocDocument(string category, int number);                                   // get_doc_document
AgentResourceWriteResult CreateDocDocument(string category, string feature, string phase, string content, string status = "draft");  // create_doc_document
AgentResourceWriteResult UpdateDocDocument(string category, int number, string content, string? status = null);                      // update_doc_document
AgentResourceWriteResult DeleteDocDocument(string category, int number);                   // delete_doc_document
```

- 생성: `category` 폴더 안 `^\d{4}-.*\.md$` 파일 번호의 최댓값에 1을 더한다(파일이 없으면 1). 파일명은 `{NNNN}-{feature}-{phase}.md`이다. `FileMode.CreateNew`로 써서 기존 파일을 덮어쓰지 않는다.
- 동시 생성: 번호 계산과 파일 생성을 category 폴더별 프로세스 내 잠금 안에서 한다. 잠금 객체는 `static ConcurrentDictionary<string, object>`에 category 폴더 전체 경로(대소문자 무시)를 키로 둔다. Tool 인스턴스가 요청마다 새로 생겨도 같은 잠금을 쓰게 하기 위해서다. 같은 프로세스 안에서는 feature·phase가 달라도 번호가 겹치지 않는다. 같은 루트를 쓰는 다른 프로세스와의 경합은 막지 않는다.
- Tool이 front matter를 만든다(`status`, `category`, `feature`, `phase` 순서). `content`는 본문만 받는다.
- 수정: `feature`·`phase`는 기존 front matter 값을 유지하고 본문을 `content`로 바꾼다. `status`가 null이면 기존 값을 유지한다. 기존 front matter의 다른 키는 유지하지 않는다.
- 조회·수정·삭제: `category` 폴더에서 `{number:D4}-*.md`가 정확히 1개여야 한다. 없으면 not found이고 여러 개면 duplicate 오류다.
- 목록: `docs/*/NNNN-*.md`를 모아 front matter의 `status`·`feature`·`phase`를 읽는다. 필터는 front matter 값과 대소문자를 무시하고 비교한다(`category`는 폴더명과 비교). 결과는 category, number 순으로 정렬한다.

## 모델 (`Models/AgentResource/`)

```csharp
public sealed record AgentResourceWriteResult(string Path);   // 종류 루트 기준 상대 경로

public sealed record RuleDocumentSummary(string Path, string? Description);
public sealed record RuleDocumentListResult(IReadOnlyList<RuleDocumentSummary> Rules);
public sealed record RuleDocument(string Path, string Content);

public sealed record SkillDocumentSummary(string Path, string? Name, string? Description);
public sealed record SkillDocumentListResult(IReadOnlyList<SkillDocumentSummary> Skills);
public sealed record SkillDocument(string Path, string Content);

public sealed record ToolDocumentSummary(string Path, string? Name, string? Description, string? Language, string? Location);
public sealed record ToolDocumentListResult(IReadOnlyList<ToolDocumentSummary> Tools);
public sealed record ToolDocumentUsage(string Name, string? Location, string Usage);
public sealed record ToolRegistrationGuide(string Purpose, string StorageLayout,
    IReadOnlyList<ToolRegistrationField> RequiredFields, IReadOnlyList<string> LanguageExamples,
    ToolRegistrationExample Example, ToolRegistrationRelatedTools RelatedTools);   // 하위 record 3개 포함

public sealed record DocDocumentSummary(string Path, string Category, int Number, string? Status, string? Feature, string? Phase);
public sealed record DocDocumentListResult(IReadOnlyList<DocDocumentSummary> Docs);
public sealed record DocDocument(string Path, string Category, int Number, string Content);
```

- nullable 속성에는 `[property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]`를 붙인다. 출력 스키마의 required와 맞추기 위해서다(storage 커밋 740782e 규칙).
- `Content`는 front matter를 포함한 파일 전문이다.

## 경로 검증 (`AgentResourcePath`)

참고 서버의 `TryResolvePath`를 옮긴다.

1. 앞뒤 공백을 자르고 `\`를 `/`로 바꾼 뒤 맨 앞의 `/`를 없앤다.
2. 비었거나, rooted이거나, `:`를 포함하거나, `..` 세그먼트가 있으면 거부한다.
3. `Path.GetFullPath(Combine(kindRoot, normalized))` 결과가 `kindRoot + separator`로 시작해야 한다(대소문자 무시). 종류 루트와 같으면 거부한다. Windows가 끝의 `.`·공백을 잘라 루트가 되는 `./`, `...` 같은 입력도 포함한다.
4. 세그먼트가 `.`이나 공백으로 끝나면 거부한다. `create-pr.`가 `create-pr`의 별칭이 되지 않게 하기 위해서다.
5. 파일 입출력 오류(`IOException`, `UnauthorizedAccessException`)는 `McpException`으로 감싼다. 생성할 때 대상 파일이 이미 있으면 "already exists" 메시지를 쓴다.

docs 입력 검증:

| 인자 | 규칙 |
| --- | --- |
| `category` | `^[a-z0-9]+(-[a-z0-9]+)*$` (소문자 영문·숫자·하이픈, work-process 규칙) |
| `feature` | 비지 않음. 공백, `/`, `\`, `:`, `..`, 파일명 금지 문자(`Path.GetInvalidFileNameChars`) 금지 |
| `phase` | 자유 문자열. 비지 않음. 공백, `/`, `\`, `:`, `..`, 파일명 금지 문자, `-` 금지(파일명 마지막 `-`로 feature와 나누기 위해) |
| `status` | 비지 않은 한 줄 |
| `number` | 1 ~ 9999 |

## 오류

모두 `McpException`을 던지고 메시지는 영어로 쓴다.

| 조건 | 메시지 예 |
| --- | --- |
| 잘못된 경로·이름 | `Path escapes the rules directory` / `Absolute paths are not allowed` |
| 없음 | `Rule document not found: dotnet/naming.md` |
| 이미 있음(create/register) | `Skill document already exists: create-pr` |
| 필수 필드 공백 | `description is required` |
| docs 번호 중복 파일 | `Multiple docs documents match agent-resource/0001` |
| 번호 소진(9999 초과) | `No document number left in category agent-resource` |

## 테스트

- 테스트마다 `Path.Combine(Path.GetTempPath(), Guid)` 임시 루트를 만들고 `[TestCleanup]`에서 지운다.
- 종류별 CRUD 왕복을 확인한다. create 다음 get이 같은 내용이어야 하고, 같은 이름으로 다시 create하면 예외, update 다음 get은 바뀐 내용, delete 다음 get은 예외, list에는 front matter 값이 보여야 한다.
- 경로 이탈 거부: `[DataRow("../x")]`, `"C:/x"`, `"/../x"`, `"a/../../x"`
- docs: 순번 자동 부여(0001, 0002), 기존 최댓값 다음 번호, 수정할 때 status 유지·변경, 필터, 잘못된 category·phase 거부
- 등록: `WithAgentResourceTools`로 Tool 21개가 노출되는지, `RootPath`가 상대 경로이거나 비면 `ArgumentException`인지, 기존 `WithBizadmToolKit()` Tool 목록에 AgentResource Tool이 없는지
- 스키마: list 결과 record를 `McpJsonUtilities.DefaultOptions`로 직렬화했을 때 출력 스키마의 required 속성이 모두 있는지

## 의존성

없음. 기존 `ModelContextProtocol` 2.3.1과 `Microsoft.Extensions.DependencyInjection`(테스트)만 쓴다.

## 결정 목록

1. 공개 `AgentResourceOptions.RootPath`. 등록할 때 절대 경로인지 검증하고 singleton으로 등록한다.
2. `WithAgentResourceTools(Action<AgentResourceOptions>)`를 별도 확장 메서드로 둔다. `WithBizadmToolKit()`은 바꾸지 않는다.
3. Tool 클래스는 공개 인스턴스 클래스 4개이고 생성자로 옵션을 받는다. 서비스 계층은 없고 helper는 internal static이다.
4. rule·skill·tool Tool 16개는 참고 서버 이름과 동작을 따르고, 결과는 record로, 실패는 `McpException`으로 낸다.
5. docs Tool 5개. 식별은 `category` + `number`, 파일명은 `NNNN-{feature}-{phase}.md`이고 Tool이 front matter를 만든다.
6. docs `phase`는 자유 문자열이지만 `-`는 금지한다. `status`의 기본값은 `draft`이고 자유 문자열이다.
7. 쓰기 결과는 공통 `AgentResourceWriteResult(Path)`로 낸다.
8. docs 동시 생성은 category 폴더별 프로세스 내 잠금(`static ConcurrentDictionary<string, object>`)으로 번호 계산과 생성을 묶는다. 프로세스 사이의 경합은 범위 밖이다. `FileMode.CreateNew`로 덮어쓰기 방지는 유지한다(사용자 결정, 검토 후 회귀 수정).
