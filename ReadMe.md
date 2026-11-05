# Biz.Bizadm.MCP.ToolKit

ASP.NET Core MCP Server에 `WithTools`로 등록하는 Tool 모음 클래스 라이브러리입니다.

- 대상 프레임워크: `net10.0`
- 의존 패키지: [`ModelContextProtocol`](https://www.nuget.org/packages/ModelContextProtocol) 2.3.1
- 전송 방식(HTTP, stdio)과 무관하며, 전송 설정은 서버 쪽에서 합니다.

## 사용법

### 패키지 참조

```xml
<PackageReference Include="Biz.Bizadm.MCP.ToolKit" Version="1.0.0" />
```

### 개별 Tool 등록

Tool 클래스는 `public`이므로 필요한 것만 골라 등록할 수 있습니다.

```csharp
using Biz.Bizadm.MCP.ToolKit.Tools;

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<XxxTools>();
```

### Redmine Tool 등록

Redmine REST API Tool은 접속 옵션과 함께 한 번에 등록합니다. 응답은 Redmine JSON 원문 문자열입니다.

```csharp
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithRedmineTools(o =>
    {
        o.ApiEndpoint = "https://redmine.example/redmine";
        o.ApiKey = "<API 키>";
        o.Version = RedmineVersion.V7_0;                  // 기본 V2_2
        o.TextFormatting = RedmineTextFormatting.Textile; // 서버 텍스트 서식과 같게. 기본 Textile
    });
```

| 옵션 | 값 | 설명 |
| --- | --- | --- |
| `Version` | `V2_2`(기본), `V7_0` | 대상 서버 버전. `V2_2`는 Tool 27개, `V7_0`은 61개를 등록한다 |
| `TextFormatting` | `Textile`(기본), `CommonMark` | 서버의 "관리 → 설정 → 일반 → 텍스트 서식"과 같은 값. `get_text_guide`가 이 서식의 문법을 반환한다. `V2_2`에서는 `Textile`만 쓸 수 있다 |

## 제공 Tool

| Tool 클래스 | Tool | 설명 |
| --- | --- | --- |
| `Tools.Redmine.IssueTools` | `list_issues`, `get_issue`, `create_issue`, `create_subtask`, `update_issue` | Redmine 일감 조회·생성·수정 |
| `Tools.Redmine.RelationTools` | `list_issue_relations`, `create_issue_relation`, `delete_issue_relation` | 일감 관계 조회·등록·삭제 |
| `Tools.Redmine.TimeEntryTools` | `list_time_entries`, `get_time_entry`, `create_time_entry`, `update_time_entry` | 작업 시간 조회·등록·수정 |
| `Tools.Redmine.WikiTools` | `list_wiki_pages`, `get_wiki_page`, `put_wiki_page` | 위키 조회·저장 |
| `Tools.Redmine.ProjectTools` | `list_projects`, `get_project` | 프로젝트 조회 |
| `Tools.Redmine.UserTools` | `get_current_user`, `list_users`, `get_user` | 사용자 조회 |
| `Tools.Redmine.MetadataTools` | `list_issue_statuses`, `list_trackers`, `list_issue_priorities`, `list_time_entry_activities` | 상태·트래커·우선순위·작업 분류 조회 |
| `Tools.Redmine.AttachmentTools` | `download_attachment`, `upload_file` | 첨부 다운로드·업로드 |
| `Tools.Redmine.TextGuideTools` | `get_text_guide` | Textile 작성 가이드 |

위 9개 클래스가 `Version = V2_2`일 때 등록된다. `Version = V7_0`이면 `WikiTools`·`ProjectTools`·`UserTools`·`MetadataTools`·`AttachmentTools`는 그대로 쓰고, 나머지는 아래 `Tools.Redmine.V7` 클래스로 바뀌거나 추가된다.

| Tool 클래스 (`Tools.Redmine.V7`) | Tool | 설명 |
| --- | --- | --- |
| `IssueToolsV7` | `list_issues`, `get_issue`, `create_issue`, `create_subtask`, `update_issue`, `delete_issue`, `add_issue_watcher`, `remove_issue_watcher` | 일감 조회·생성·수정·삭제·관찰자. 목록 필터, 사용자 정의 필드, 비공개 여부·댓글 지원 |
| `RelationToolsV7` | `list_issue_relations`, `create_issue_relation`, `delete_issue_relation` | 일감 관계. `copied_to`, `copied_from` 허용 |
| `TimeEntryToolsV7` | `list_time_entries`, `get_time_entry`, `create_time_entry`, `update_time_entry`, `delete_time_entry` | 작업 시간. 사용자 필터·대신 기록 지원 |
| `JournalTools` | `update_journal` | 일감 댓글 수정 |
| `TextGuideToolsV7` | `get_text_guide` | `TextFormatting`(Textile/CommonMark)에 맞는 작성 가이드 |
| `WikiExtraTools` | `get_wiki_page_version`, `delete_wiki_page` | 위키 버전 조회·페이지 삭제 |
| `VersionTools` | `list_versions`, `get_version`, `create_version`, `update_version`, `delete_version` | 프로젝트 버전 관리 |
| `IssueCategoryTools` | `list_issue_categories`, `get_issue_category`, `create_issue_category`, `update_issue_category`, `delete_issue_category` | 일감 범주 관리 |
| `MembershipTools` | `list_memberships`, `get_membership`, `create_membership`, `update_membership`, `delete_membership` | 프로젝트 구성원 관리 |
| `SearchTools` | `search`, `list_queries` | 검색·저장된 질의 조회 |
| `PrincipalTools` | `list_roles`, `get_role`, `list_groups`, `get_group`, `get_my_account` | 역할·그룹·내 계정 조회 |
| `CustomFieldTools` | `list_custom_fields` | 사용자 정의 필드 정의 조회(관리자) |
| `ProjectContentTools` | `list_project_files`, `list_news` | 프로젝트 파일·뉴스 조회 |
| `AttachmentManageTools` | `update_attachment`, `delete_attachment` | 첨부 파일명·설명 수정, 삭제 |

## 빌드·테스트

요구 사항: .NET 10 SDK

```bash
dotnet build Biz.Bizadm.MCP.ToolKit.slnx
```

```bash
dotnet test Biz.Bizadm.MCP.ToolKit.slnx
```

폴더 구조:

```
Biz.Bizadm.MCP.ToolKit.slnx
Directory.Build.props        # 공통 TFM·Nullable·ImplicitUsings
Directory.Packages.props     # 중앙 패키지 버전 관리
src/Biz.Bizadm.MCP.ToolKit/  # 라이브러리
tests/Biz.Bizadm.MCP.ToolKit.Tests/  # MSTest
docs/toolkit/                # 방안·설계·개발 문서
docs/redmine/                # Redmine Tool 방안·설계·개발 문서
```

## 라이선스

[MIT](LICENSE)
