---
status: done
category: redmine
feature: redmine-tools
phase: 설계
---

# redmine-tools 설계

선행: `0001-redmine-tools-방안.md`
참고: `D:\Git\Server\Redmine.MCP\src\Redmine.MCP.Server`

## 구조

```
src/Biz.Bizadm.MCP.ToolKit/
├── RedmineMcpServerBuilderExtensions.cs   (public, namespace Microsoft.Extensions.DependencyInjection)
├── Redmine/                               (namespace Biz.Bizadm.MCP.ToolKit.Redmine)
│   ├── RedmineOptions.cs                  public  — ApiEndpoint, ApiKey
│   ├── RedmineClient.cs                   public  — HTTP 호출 (원본 RedmineClient + RedmineHttpClientSetup)
│   ├── RedmineApiException.cs             public  — McpException 상속, StatusCode·ResponseBody
│   ├── UploadReference.cs                 public  — Tool 매개변수 스키마용 record
│   ├── RedmineJson.cs                     internal — Body 작성 helper
│   └── AttachmentDownload.cs              internal — 다운로드 응답 작성
└── Tools/Redmine/                         (namespace Biz.Bizadm.MCP.ToolKit.Tools.Redmine)
    ├── IssueTools.cs, RelationTools.cs, TimeEntryTools.cs, WikiTools.cs,
    ├── ProjectTools.cs, UserTools.cs, MetadataTools.cs, AttachmentTools.cs,
    ├── TextGuideTools.cs                  public sealed, [McpServerToolType]
    └── RedmineToolDescriptions.cs         internal — Mutating 안내 문구
tests/Biz.Bizadm.MCP.ToolKit.Tests/Redmine/
```

- 테스트에서 internal 타입을 쓰므로 ToolKit csproj에 `InternalsVisibleTo Include="Biz.Bizadm.MCP.ToolKit.Tests"`를 추가한다(allowed_paths에 csproj 추가).
- 원본 클래스 단위·로직을 그대로 옮기고 네임스페이스·접근자·XML 주석·annotation·예외만 바꾼다.

## 인터페이스

### 옵션

```csharp
public sealed class RedmineOptions
{
    public string ApiEndpoint { get; set; } = string.Empty; // 절대 http/https URI, 하위 경로 허용
    public string ApiKey { get; set; } = string.Empty;      // X-Redmine-API-Key
}
```

### 등록

```csharp
public static IMcpServerBuilder WithRedmineTools(this IMcpServerBuilder builder, Action<RedmineOptions> configure)
```

1. `builder`·`configure` null → `ArgumentNullException`
2. `ApiEndpoint`가 절대 http/https URI가 아니거나 `ApiKey`가 비었으면 `ArgumentException`
3. `AddSingleton(options)`, `AddSingleton<RedmineClient>(sp => new RedmineClient(options))`
4. Tool 클래스 9개 `WithTools<>` (27개 Tool)

### RedmineClient

- 생성자 2개
  - `RedmineClient(RedmineOptions options)`: `SocketsHttpHandler { PooledConnectionLifetime = 2분 }`로 `HttpClient`를 만들고 원본 `RedmineHttpClientSetup.Configure` 규칙을 적용한다(BaseAddress 끝 `/`, `X-Redmine-API-Key`, `Accept: application/json`). 새 패키지 없음.
  - `RedmineClient(HttpClient httpClient)`: 이미 구성된 클라이언트를 받는다(테스트 주입용). 원본과 같다.
- 메서드는 원본과 같다: `GetAsync`, `GetPagedAsync`, `PostAsync`, `PutAsync`, `PutBodyAsync`, `DeleteAsync`, `PostOctetStreamAsync`, `GetBytesAsync`, `NormalizeLimit`, `NormalizeOffset`.
- 원본 `RedmineHttpClientSetup`은 별도 파일로 두지 않고 `RedmineClient` 안의 internal static `Configure(HttpClient, RedmineOptions)`로 옮긴다(테스트에서 사용).

### Tool

- 클래스는 `public sealed`, 생성자에서 `RedmineClient`를 받는다(`TextGuideTools`는 의존성 없음).
- Tool 이름·매개변수 이름·타입·기본값·Description(한국어, Redmine 2.2 표기, `Mutating` 안내)·엔드포인트·Body는 원본과 같다.
- 반환은 원본처럼 `Task<string>`(Redmine 응답 JSON 원문). `UseStructuredContent`는 쓰지 않는다.

## 예외

| 상황 | 원본 | 이식 |
| --- | --- | --- |
| 입력 오류 | `ArgumentException` | `McpException`(영문 메시지는 원본 그대로) |
| Redmine 비2xx | `RedmineApiException : Exception` | `RedmineApiException : McpException`, `StatusCode`·`ResponseBody` 유지, 메시지 동일 |
| 다른 출처 content_url 등 클라이언트 검증 | `ArgumentException`/`InvalidOperationException` | `McpException` |

MCP SDK는 `McpException`이 아닌 예외의 메시지를 모델에 보내지 않으므로, 오류 내용을 전달하기 위해 통일한다.

## Annotation

ToolKit 관례대로 5개를 모두 명시한다. ReadOnly·Destructive는 원본 값, Idempotent·OpenWorld는 새로 정한다.

| Tool | ReadOnly | Idempotent | Destructive | OpenWorld |
| --- | --- | --- | --- | --- |
| 조회 Tool 16개(list_*, get_*; get_text_guide 제외) | true | true | false | true |
| `download_attachment` | true | true | false | true |
| `get_text_guide` | true | true | false | false |
| `create_issue`, `create_subtask`, `create_issue_relation`, `create_time_entry`, `upload_file` | false | false | false | true |
| `update_issue`, `update_time_entry` | false | false | true | true |
| `put_wiki_page` | false | true | true | true |
| `delete_issue_relation` | false | true | true | true |

- `update_issue`는 `notes`가 매번 댓글을 추가하므로 Idempotent false, `update_time_entry`는 원본과 같은 쓰기 군으로 false.

## 결정 목록

| 번호 | 결정 | 근거 |
| --- | --- | --- |
| D1 | HttpClient는 `RedmineClient`가 직접 만든 싱글턴 | 사용자 결정. 새 패키지 없음 |
| D2 | 예외는 `McpException`으로 통일, `RedmineApiException`은 이를 상속 | 사용자 결정 |
| D3 | public: Tool 9개, `RedmineOptions`, `RedmineClient`, `RedmineApiException`, `UploadReference` / internal: `RedmineJson`, `AttachmentDownload`, `RedmineToolDescriptions` | 사용자 결정. Tool 생성자 매개변수인 `RedmineClient`는 public이어야 한다 |
| D4 | 반환은 Redmine JSON 원문 문자열 | 방안 E |
| D5 | Tool 계약(이름·매개변수·엔드포인트·Body)은 원본과 동일 | 방안 목표 |
| D6 | 등록은 `WithRedmineTools(Action<RedmineOptions>)` | 방안 G, `AgentResourceMcpServerBuilderExtensions` 형태 재사용 |
| D7 | Annotation은 위 표 | ToolKit 관례(5개 명시) |

## 의존성

- 추가 패키지 없음. `System.Net.Http`, `System.Text.Json`(BCL), `ModelContextProtocol`(기존)만 쓴다.
- 테스트: 기존 MSTest·Microsoft.Extensions.DependencyInjection. 원본의 MCP 라운드트립 테스트(`StreamServerTransport`/`StreamClientTransport`)는 `ModelContextProtocol` 패키지에 있으므로 추가 패키지 없음.
