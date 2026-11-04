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

Redmine 2.2.x REST API Tool은 접속 옵션과 함께 한 번에 등록합니다. 응답은 Redmine JSON 원문 문자열입니다.

```csharp
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithRedmineTools(o =>
    {
        o.ApiEndpoint = "https://redmine.example/redmine";
        o.ApiKey = "<API 키>";
    });
```

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
