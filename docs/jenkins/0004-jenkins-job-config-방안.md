---
status: done
category: jenkins
feature: jenkins-job-config
phase: 방안
---

# jenkins-job-config 방안

GitHub 이슈: [#3 Jenkins Job 설정 REST Tool 추가](https://github.com/SeongSikChae/Biz.Bizadm.MCP.ToolKit/issues/3).

## 문제

- Jenkins MCP Server(`/mcp-server/mcp`)는 Job 설정(config.xml)을 읽고 쓰거나 Job을 지우는 Tool을 노출하지 않는다. 그래서 Pipeline 스크립트 수정·Job 삭제는 사람이 Jenkins 화면에서 해야 한다.
- Jenkins는 Job 설정을 읽고 쓰는 REST API를 기본 제공한다. 이미 등록한 인스턴스의 User/API Token(Basic)으로 호출할 수 있고, API Token으로 호출하면 CSRF crumb이 필요 없다.

## 목표

등록한 Jenkins 인스턴스의 User/API Token으로 Jenkins REST API를 직접 호출해 Job 설정을 읽고, 바꾸고, Job을 지운다. 재시작 없이 반영되고 빌드 기록은 유지된다.

## 대안

노출 방식:

| 대안 | 판단 |
| --- | --- |
| 전용 MCP Tool 추가 | **채택**. GitHub REST 쓰기 Tool(1.4.0)과 같은 방식. 에이전트가 바로 보고 호출하며 입력 스키마가 명확하다 |
| 게이트웨이(`jenkins_list_tools`/`jenkins_call_tool`)에 합침 | upstream Tool과 이름 충돌·구분 규칙이 필요하다 |

REST 기준 주소:

| 대안 | 판단 |
| --- | --- |
| `Endpoint`에서 유도 | **채택(사용자 결정)**. `Endpoint` 끝의 `/mcp-server/mcp`를 떼어 Jenkins 루트로 쓴다. 설정 변경이 없고 BizadmJenkins·InspienJenkins에 바로 적용된다. 끝이 다르면 REST Tool 호출 시에만 오류 |
| 선택 `BaseUrl` + 유도 fallback | 리버스 프록시 등 예외 경로에 대응하지만 현재 필요한 인스턴스가 없다 |
| 필수 `BaseUrl` | 기존 설정이 깨지고 MCP.Server 설정 변경이 필요하다 |

스크립트 수정:

| 대안 | 판단 |
| --- | --- |
| XML 전체 교체만 제공 | 다른 설정을 실수로 덮어쓸 위험이 크다 |
| XML 전체 교체 + `<script>`만 교체하는 편의 Tool | **채택**. 편의 Tool은 config.xml을 읽어 `<script>`만 바꾼 뒤 다시 올린다 |

## 채택 방안

- 인스턴스 선택은 게이트웨이와 같이 필수 `instance` 인자, 기본 인스턴스 없음.
- Job은 Jenkins 전체 이름(`folder/sub/job`)으로 받고 `/job/folder/job/sub/job/job`으로 바꾼다.
- 범위:

| Tool | REST API | 용도 |
| --- | --- | --- |
| `jenkins_get_job_config` | `GET /job/<경로>/config.xml` | Job 설정(Pipeline 스크립트 포함) 읽기 |
| `jenkins_update_job_config` | `POST /job/<경로>/config.xml` (XML 본문) | 설정 전체 교체 |
| `jenkins_update_pipeline_script` | `GET` 후 `<script>` 교체 → `POST config.xml` | 스크립트만 교체 |
| `jenkins_delete_job` | `POST /job/<경로>/doDelete` | Job 삭제 |

- 필요 권한: 조회는 Job ExtendedRead 또는 Configure(Read만으로는 403), 수정은 Configure, 삭제는 Delete. 권한 검사는 Jenkins가 한다. 401·403·404 등 REST 오류는 `isError` 결과로 돌려주고 API Token은 메시지·결과에 넣지 않는다.
- **호환성**: 등록 검증은 바꾸지 않는다. `Endpoint`가 `/mcp-server/mcp`로 끝나지 않는 인스턴스도 기존처럼 등록되고 게이트웨이 3개 Tool은 그대로 동작한다. 그 인스턴스로 REST Tool을 부를 때만 `isError` 결과를 돌려준다(사용자 결정). 현재 등록한 두 인스턴스는 모두 이 형식이다.

## 비범위

API Token KMS 암호화(호스트 MCP.Server 책임), MCP.Server 변경, Job 생성·복사·이름 변경, 빌드 실행, 게이트웨이 3개 Tool 동작 변경, 자동 재시도.
