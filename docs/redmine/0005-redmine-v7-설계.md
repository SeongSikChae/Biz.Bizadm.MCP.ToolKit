---
status: done
category: redmine
feature: redmine-v7
phase: 설계
---

# redmine-v7 설계

선행: `0004-redmine-v7-방안.md`, `0002-redmine-tools-설계.md`

## 구조

```
src/Biz.Bizadm.MCP.ToolKit/
├── RedmineMcpServerBuilderExtensions.cs   변경 — 버전별 Tool 세트 등록
├── Redmine/
│   ├── RedmineOptions.cs                  변경 — Version, TextFormatting 추가
│   ├── RedmineVersion.cs                  신규 public enum { V2_2, V7_0 }
│   ├── RedmineTextFormatting.cs           신규 public enum { Textile, CommonMark }
│   ├── RedmineClient.cs                   변경 — PatchAsync 추가
│   ├── RedmineJson.cs                     변경 — bool·ID 배열·custom_fields helper 추가
│   ├── CustomFieldValue.cs                신규 public record (Tool 매개변수 스키마)
│   ├── IssueBody.cs                       신규 internal — IssueTools·IssueToolsV7 공용 Body 작성
│   ├── TimeEntryBody.cs                   신규 internal — TimeEntryTools·TimeEntryToolsV7 공용 Body 작성
│   └── RedminePaths.cs                    신규 internal — 프로젝트 경로 이스케이프, 양수 ID 검사
└── Tools/Redmine/
    ├── (기존 9개 클래스)                   IssueTools·TimeEntryTools는 Body 작성만 helper 호출로 바꾼다
    └── V7/                                신규 namespace Biz.Bizadm.MCP.ToolKit.Tools.Redmine.V7
        ├── IssueToolsV7.cs                교체 — 기존 일감 Tool 5개 + delete_issue, add_issue_watcher, remove_issue_watcher
        ├── RelationToolsV7.cs             교체 — 관계 3개, copied_to·copied_from 허용
        ├── TimeEntryToolsV7.cs            교체 — 작업 시간 4개(user_id) + delete_time_entry
        ├── TextGuideToolsV7.cs            교체 — get_text_guide, 옵션 서식에 맞춘 가이드
        ├── JournalTools.cs                신규 — update_journal
        ├── WikiExtraTools.cs              신규 — get_wiki_page_version, delete_wiki_page
        ├── VersionTools.cs                신규 — 버전 5개
        ├── IssueCategoryTools.cs          신규 — 일감 범주 5개
        ├── MembershipTools.cs             신규 — 구성원 5개
        ├── SearchTools.cs                 신규 — search, list_queries
        ├── PrincipalTools.cs              신규 — list_roles, get_role, list_groups, get_group, get_my_account
        ├── CustomFieldTools.cs            신규 — list_custom_fields
        ├── ProjectContentTools.cs         신규 — list_project_files, list_news
        └── AttachmentManageTools.cs       신규 — update_attachment, delete_attachment
```

## 옵션

```csharp
public sealed class RedmineOptions
{
    public string ApiEndpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public RedmineVersion Version { get; set; } = RedmineVersion.V2_2;                       // 신규
    public RedmineTextFormatting TextFormatting { get; set; } = RedmineTextFormatting.Textile; // 신규
}
```

- `TextFormatting`은 서버의 "관리 → 설정 → 일반 → 텍스트 서식"과 같은 값으로 둔다. 서버 서식은 전역 설정 하나뿐이라 글마다 섞어 쓸 수 없고, REST API로 조회할 수도 없다.
- `V2_2`에서 `CommonMark`를 지정하면 등록할 때 `ArgumentException`을 던진다. 2.2에는 CommonMark 서식이 없다.
- 정의되지 않은 enum 값도 `ArgumentException`이다.

## 등록

```csharp
public static IMcpServerBuilder WithRedmineTools(this IMcpServerBuilder builder, Action<RedmineOptions> configure)
```

| 세트 | V2_2 | V7_0 |
| --- | --- | --- |
| 공통: `WikiTools`, `ProjectTools`, `UserTools`, `MetadataTools`, `AttachmentTools` | O | O |
| 2.2 전용: `IssueTools`, `RelationTools`, `TimeEntryTools`, `TextGuideTools` | O | X |
| 7.0 교체: `IssueToolsV7`, `RelationToolsV7`, `TimeEntryToolsV7`, `TextGuideToolsV7` | X | O |
| 7.0 신규: 나머지 V7 클래스 10개 | X | O |

- Tool 수는 V2_2가 27개(현재와 같음), V7_0이 61개(기존 27개 + 신규 34개)다.
- 같은 Tool 이름은 버전별로 한 클래스에서만 등록된다.
- 공통 클래스는 Description에 2.2 전용 문구가 없어서 그대로 공유한다.

## 기존 Tool의 7.0 변경점

이름·기본 매개변수·엔드포인트는 2.2와 같다. 아래 항목만 추가하거나 바꾼다.

| Tool | 7.0 변경 |
| --- | --- |
| `list_issues` | 필터 추가: `subprojectId`, `parentId`, `issueIds`(쉼표 목록), `authorId`, `categoryId`, `fixedVersionId`, `createdOn`, `updatedOn`(연산자 문자열, 예: `>=2026-01-01`), `queryId`, `sort`, `include` |
| `get_issue` | `include`에 `watchers`, `allowed_statuses`를 허용하고, "watchers는 2.2 응답에 없다" 문구를 뺀다 |
| `create_issue`, `create_subtask` | `isPrivate`, `categoryId`, `fixedVersionId`, `customFields`, `watcherUserIds` 추가 |
| `update_issue` | `isPrivate`, `categoryId`, `fixedVersionId`, `customFields`, `privateNotes` 추가 |
| `create_issue_relation` | `copied_to`, `copied_from` 허용 |
| `list_time_entries` | `userId`(ID 또는 `me`) 추가, "사용자로는 거르지 못한다" 문구를 뺀다 |
| `create_time_entry` | `userId` 추가(다른 사용자 대신 기록, 권한 필요) |
| `get_text_guide` | `target`은 그대로다. `RedmineOptions.TextFormatting`에 맞는 가이드를 반환하고, 댓글 가이드에 "update_journal로 수정할 수 있다"를 넣는다 |

`CustomFieldValue`는 `record CustomFieldValue(int Id, string? Value, IReadOnlyList<string>? Values)`다.
- `Body`에는 `{"id":1,"value":"x"}` 형태로 넣고, 다중 값이면 `value`를 배열로 넣는다.
- `Value`와 `Values`를 함께 주거나 둘 다 비우면 `McpException`이다.

## 텍스트 가이드

- 링크 예약어와 매크로 블록은 두 서식에서 같으므로 공용 상수로 둔다.
- 글자·구조·코드 블록, 템플릿은 서식별 상수로 둔다.
  - Textile: 기존 내용을 그대로 쓴다.
  - CommonMark: `**굵게**`, `*기울임*`, `~~취소선~~`, `` `코드` ``, `# 제목`, `- 목록`, `1. 번호`, `> 인용`, GFM 표, ```` ```언어 ```` 코드 펜스, `- [ ] 할 일`
- 2.2 `TextGuideTools`는 바꾸지 않는다.
- `TextGuideToolsV7`은 `RedmineOptions`를 DI로 받는다. Description은 서식을 특정하지 않는다("서버 텍스트 서식에 맞는 …").

## 신규 Tool 엔드포인트

| Tool | 메서드·경로 | Body wrapper |
| --- | --- | --- |
| `delete_issue` | `DELETE issues/{id}.json` | — |
| `add_issue_watcher` | `POST issues/{id}/watchers.json` | 최상위 `{"user_id":n}` |
| `remove_issue_watcher` | `DELETE issues/{id}/watchers/{userId}.json` | — |
| `delete_time_entry` | `DELETE time_entries/{id}.json` | — |
| `update_journal` | `PUT journals/{id}.json` | `journal` {notes, private_notes} |
| `get_wiki_page_version` | `GET projects/{p}/wiki/{title}/{version}.json` | — |
| `delete_wiki_page` | `DELETE projects/{p}/wiki/{title}.json` | — |
| `list_versions` / `create_version` | `GET`·`POST projects/{p}/versions.json` | `version` {name, status, sharing, due_date, description, wiki_page_title} |
| `get_version` / `update_version` / `delete_version` | `GET`·`PUT`·`DELETE versions/{id}.json` | `version` |
| `list_issue_categories` / `create_issue_category` | `GET`·`POST projects/{p}/issue_categories.json` | `issue_category` {name, assigned_to_id} |
| `get_issue_category` / `update_issue_category` / `delete_issue_category` | `GET`·`PUT`·`DELETE issue_categories/{id}.json` | 삭제는 `reassign_to_id` 쿼리 선택 |
| `list_memberships` / `create_membership` | `GET`(페이징)·`POST projects/{p}/memberships.json` | `membership` {user_id, role_ids} |
| `get_membership` / `update_membership` / `delete_membership` | `GET`·`PUT`·`DELETE memberships/{id}.json` | `membership` {role_ids} |
| `search` | `GET search.json` 또는 `GET projects/{p}/search.json` | 쿼리 q, scope, all_words, titles_only, 유형 플래그, limit, offset |
| `list_queries` | `GET queries.json`(페이징) | — |
| `list_roles` / `get_role` | `GET roles.json` / `GET roles/{id}.json` | — |
| `list_groups` / `get_group` | `GET groups.json` / `GET groups/{id}.json?include=users,memberships` | — |
| `get_my_account` | `GET my/account.json` | — |
| `list_custom_fields` | `GET custom_fields.json` | — |
| `list_project_files` | `GET projects/{p}/files.json` | — |
| `list_news` | `GET news.json` 또는 `GET projects/{p}/news.json`(페이징) | — |
| `update_attachment` | `PATCH attachments/{id}.json` | `attachment` {filename, description} |
| `delete_attachment` | `DELETE attachments/{id}.json` | — |

- 프로젝트 경로의 `{p}`와 위키 `{title}`은 기존 `WikiTools.PagePath`처럼 trim 후 `Uri.EscapeDataString`을 적용한다.
- ID 매개변수가 1보다 작으면 요청을 보내기 전에 `McpException`을 던진다. 기존 `RelationTools.EnsurePositive`와 같은 규칙이다.
- 수정 Tool에서 바꿀 필드가 하나도 없으면 `McpException("수정할 필드를 하나 이상 지정해야 합니다.")`를 던진다. 기존 `update_issue`와 같다.

## Annotation

기존 표(`0002` D7)의 규칙을 따른다.

| 분류 | ReadOnly | Idempotent | Destructive | OpenWorld |
| --- | --- | --- | --- | --- |
| 조회(list_*, get_*, search) | true | true | false | true |
| `get_text_guide` | true | true | false | false |
| 생성(create_*), `add_issue_watcher` | false | false | false | true |
| 덮어쓰기 수정(update_version·issue_category·membership·journal·attachment) | false | true | true | true |
| `update_issue`, `update_time_entry` | false | false | true | true (기존 그대로) |
| 삭제(delete_*, remove_issue_watcher) | false | true | true | true |

- 쓰기 Tool의 Description 끝에는 기존처럼 `RedmineToolDescriptions.Mutating`을 붙인다.

## 결정 목록

| 번호 | 결정 | 근거 |
| --- | --- | --- |
| D1 | `RedmineOptions.Version`(기본 `V2_2`)으로 Tool 세트를 고른다 | 방안 A |
| D2 | 매개변수·Description이 다른 Tool은 `Tools/Redmine/V7/`에 같은 Tool 이름의 `*V7` 클래스로 만든다. Body 작성은 internal helper로 공유한다 | 사용자 결정. attribute 상수 제약 |
| D3 | 2.2 클래스는 Body 작성 코드만 helper로 옮긴다. 계약(이름·매개변수·Description·요청)은 바꾸지 않는다 | 방안 비범위 |
| D4 | 텍스트 서식은 `RedmineOptions.TextFormatting`(Textile/CommonMark)으로 지정하고, Tool 매개변수로 받지 않는다 | 사용자 결정. 서버 전역 설정 하나뿐 |
| D5 | V2_2 + CommonMark 조합은 등록할 때 거부한다 | 2.2에 CommonMark 없음 |
| D6 | `RedmineClient.PatchAsync(path, wrapper, payload, ct)`와 `PostBodyAsync(path, body, ct)`를 추가한다 | 첨부 수정(PATCH), 관찰자 추가(최상위 Body POST). 기존 `SendWrappedAsync`·`CreateJsonContent` 재사용 |
| D7 | 반환은 Redmine JSON 원문 문자열이다 | 기존 D4 |
| D8 | 신규 Tool annotation은 위 표를 따른다 | 기존 D7 규칙 |

## 의존성

- 추가 패키지는 없다.
- 테스트는 기존 MSTest와 `RecordingHandler` 방식을 쓴다.
- 기존 `Redmine22ContractTests`·`RedmineRegistrationTests`는 바꾸지 않고 통과해야 한다. 2.2 기본값이 그대로인지를 이 테스트로 확인한다.
