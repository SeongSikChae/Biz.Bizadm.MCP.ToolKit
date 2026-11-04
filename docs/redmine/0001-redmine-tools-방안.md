---
status: done
category: redmine
feature: redmine-tools
phase: 방안
---

# redmine-tools 방안

참고: `D:\Git\Server\Redmine.MCP`

## 문제

Redmine 2.2.x 일감·위키·작업 시간·첨부 Tool은 별도 프로세스인 Redmine.MCP 서버(ASP.NET Core, Stateless HTTP)로만 쓸 수 있다. 다른 MCP 호스트가 같은 기능을 쓰려면 그 서버를 따로 띄워야 한다.

## 목표

- ToolKit 라이브러리에 Redmine Tool 그룹을 추가한다. 호스트는 ToolKit만 참조하면 이 Tool들을 노출할 수 있다.
- 참고 서버의 Tool 27개를 모두 이식한다.
  - issue: `list_issues`, `get_issue`, `create_issue`, `create_subtask`, `update_issue`
  - relation: `list_issue_relations`, `create_issue_relation`, `delete_issue_relation`
  - time entry: `list_time_entries`, `get_time_entry`, `create_time_entry`, `update_time_entry`
  - wiki: `list_wiki_pages`, `get_wiki_page`, `put_wiki_page`
  - project: `list_projects`, `get_project`
  - user: `get_current_user`, `list_users`, `get_user`
  - metadata: `list_issue_statuses`, `list_trackers`, `list_issue_priorities`, `list_time_entry_activities`
  - attachment: `download_attachment`, `upload_file`
  - guide: `get_text_guide`
- Tool 이름·매개변수·Redmine 엔드포인트·요청 Body는 참고 서버와 같게 한다(호환 유지).
- 결과는 참고 서버처럼 Redmine 응답 JSON 문자열을 그대로 반환한다.
- Redmine 접속 정보(API 엔드포인트, API 키)는 등록할 때 옵션으로 받는다.

## 대안

| 안 | 내용 | 판단 |
| --- | --- | --- |
| A | 참고 서버 코드를 ToolKit 관례(폴더·주석·annotation·등록 확장)에 맞춰 이식 | 채택 |
| B | 참고 서버를 NuGet 라이브러리로 분리해 ToolKit이 참조 | 기각: Redmine.MCP 저장소 변경이 필요하고 ToolKit 관례와 맞지 않는다 |
| C | Tool 27개 전부 이식 | 채택: 사용자 결정 |
| D | 파일 Tool(`download_attachment`, `upload_file`) 제외 | 기각: 사용자 결정 |
| E | Redmine 원본 JSON 문자열 반환 | 채택: 사용자 결정. 응답 전체를 유지하고 DTO를 만들지 않는다 |
| F | `Models/Redmine/` typed record + structured content 반환 | 기각: 사용자 결정. 작업량이 크고 필드 누락 위험이 있다 |
| G | 별도 확장 메서드 `WithRedmineTools(...)`로 등록 | 채택. AgentResource와 같은 방식 |

## 채택 방안

- `Tools/Redmine/`에 참고 서버와 같은 단위의 Tool 클래스 9개(issue·relation·time entry·wiki·project·user·metadata·attachment·text guide)를 둔다.
- Tool 클래스는 Redmine 클라이언트를 생성자 DI로 받는 인스턴스 클래스로 만든다.
- HTTP 호출, JSON Body 작성, 첨부 다운로드 처리 로직은 참고 서버의 것을 옮겨 `Redmine/` 폴더의 공통 코드로 둔다.
- 등록은 `WithRedmineTools(Action<RedmineOptions>)` 확장 메서드 하나로 한다.
- 참고 서버 테스트를 같은 방식(HttpMessageHandler 기록, 모킹 라이브러리 없음)으로 이식한다.

## 비범위

- Biz.Bizadm.MCP.Server 연동(Program.cs 등록, YAML 설정, API 키 KMS 암호화)
- 참고 서버의 호스팅(Kestrel, HTTPS, Windows 서비스, CLI 인자, Serilog)
- Redmine.MCP 저장소 변경
- 기존 amp·audio·speaker·storage·agent-resource Tool 변경
- 실제 Redmine 서버 대상 연동 검증
