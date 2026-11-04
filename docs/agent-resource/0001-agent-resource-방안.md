---
status: done
category: agent-resource
feature: agent-resource
phase: 방안
---

# agent-resource 방안

참고: `D:\Git\Lib\AgentRuleSkillRepository\AgentRuleSkillRepository.MCP.Server`

## 문제

코딩 에이전트용 rule·skill·tool 문서는 별도 프로세스인 AgentRuleSkillRepository.MCP.Server(HTTP 서버)로만 조회·관리할 수 있다. 다른 MCP 호스트가 같은 기능을 쓰려면 그 서버를 따로 띄워야 한다. 기능 작업 산출물인 docs 문서(`docs/{category}/NNNN-기능-단계.md`)를 다루는 Tool도 없다.

## 목표

- ToolKit 라이브러리에 AgentResource Tool 그룹을 추가한다. 호스트는 ToolKit만 참조하면 이 Tool들을 노출할 수 있다.
- 참고 서버의 rule·skill·tool 문서 기능을 모두 이식한다.
  - rule: `{root}/rules/**.md`. 목록, 조회, 생성, 수정, 삭제
  - skill: `{root}/skills/<name>/SKILL.md`. 목록, 조회, 생성, 수정, 삭제
  - tool: `{root}/tools/<name>/TOOL.md`. 목록, 사용법 조회, 등록 가이드, 등록, 수정, 삭제
- docs 문서 기능을 추가한다.
  - `{root}/docs/{category}/NNNN-기능-단계.md`. 목록(category·feature 필터), 조회, 생성(카테고리 안 다음 NNNN 자동 부여), 수정, 삭제
  - 목록에는 front matter의 `status`·`category`·`feature`·`phase`를 보여 준다.
- 루트 폴더는 등록할 때 옵션으로 받는다.
- 결과는 구조화 객체로 반환한다. 잘못된 경로, 없는 문서, 중복 생성은 `McpException`으로 거부한다.
- 경로 이탈(`..`, 절대 경로, `:`)을 막는다.

## 대안

| 안 | 내용 | 판단 |
| --- | --- | --- |
| A | rule·skill·tool 3종 전체 이식과 docs 추가 | 채택: 사용자 결정 |
| B | 3종 조회만 이식 | 기각: 사용자 결정 |
| C | 하나의 resource 모델(kind 매개변수)로 통합 | 기각: 사용자 결정. 참고 서버와 Tool 이름 호환성이 깨진다 |
| D | 루트 경로를 등록 옵션으로 주입 | 채택: 사용자 결정 |
| E | 루트 경로를 Tool 호출마다 매개변수로 받음 | 기각: 사용자 결정. 호출마다 경로가 필요하고, 경로 노출 범위가 넓어진다 |
| F | 루트 경로를 환경 변수로 받음 | 기각: 사용자 결정 |
| G | 별도 확장 메서드 `WithAgentResourceTools(...)`로 등록 | 채택: 사용자 결정. 기존 `WithBizadmToolKit()` 호출부는 바뀌지 않는다 |
| H | `WithBizadmToolKit(options)` 오버로드에 포함 | 기각: 사용자 결정 |
| I | 구조화 record 반환, 실패하면 `McpException` | 채택: 사용자 결정. ToolKit 기존 Tool과 같은 방식 |
| J | 참고 서버처럼 `string`과 `"Error: ..."` 반환 | 기각: 사용자 결정 |
| K | docs를 category 구조(`docs/{category}/NNNN-기능-단계.md`)로 다룸 | 채택: 사용자 결정. work-process 문서 규칙과 같다 |
| L | docs를 rules처럼 자유 경로로 다루거나 skills처럼 폴더 단위로 다룸 | 기각: 사용자 결정 |

## 채택 방안

- `Tools/AgentResource/`에 종류별 Tool 클래스 4개(rule·skill·tool·doc)를 둔다.
- Tool 클래스는 옵션과 저장소 서비스를 생성자 DI로 받는 인스턴스 클래스로 만든다.
- 3종 Tool 이름은 참고 서버와 같게 한다.
  - rule: `list_rule_documents`, `get_rule_document`, `create_rule_document`, `update_rule_document`, `delete_rule_document`
  - skill: `list_skill_documents`, `get_skill_document`, `create_skill_document`, `update_skill_document`, `delete_skill_document`
  - tool: `list_tool_documents`, `get_tool_document_usage`, `get_tool_document_registration_guide`, `register_tool_document`, `update_tool_document`, `delete_tool_document`
- docs Tool은 `list_doc_documents`, `get_doc_document`, `create_doc_document`, `update_doc_document`, `delete_doc_document` 5개다.
- 경로 검증, front matter 파싱, TOOL.md 생성 로직은 참고 서버의 것을 옮겨 공통 helper로 둔다.
- 등록은 `WithAgentResourceTools(Action<AgentResourceOptions>)` 확장 메서드 하나로 한다.

## 비범위

- 참고 서버의 호스팅(HTTP, Windows 서비스, CLI 인자, YAML 설정, Serilog)
- `WithBizadmToolKit()` 등록 목록 변경
- 기존 amp·audio·speaker·storage Tool 변경
- ReadMe 갱신
- 문서 내용의 front matter 필수 필드 강제(목록에서 읽기만 함)
