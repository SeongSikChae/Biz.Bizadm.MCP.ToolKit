---
status: done
category: jenkins
feature: jenkins-proxy
phase: 방안
---

# jenkins-proxy 방안

## 문제

ToolKit을 쓰는 MCP Server에서 Jenkins 작업도 하려 한다. Jenkins는 여러 대(운영·개발 등)이고 상황에 따라 다른 Jenkins를 제어해야 한다. 각 Jenkins는 MCP Server 플러그인으로 원격 MCP를 제공한다.

| 엔드포인트 | 용도 |
| --- | --- |
| `{Jenkins}/mcp-server/mcp` | Streamable HTTP (POST) |
| `{Jenkins}/mcp-server/sse` | SSE |

인증 없이 POST하면 403 "No valid crumb"이다. Jenkins 사용자 + API Token으로 Basic 인증을 하면 crumb 없이 호출된다.

## 목표

- Jenkins를 N개 등록하고, 호출마다 인스턴스를 고른다.
- 인스턴스마다 플러그인 버전이 달라 Tool 목록이 다를 수 있으므로, 선택한 인스턴스에서 목록을 가져와 보여준다.

## 대안

연결 방식:

| 대안 | 판단 |
| --- | --- |
| A. ToolKit이 MCP Client로 Jenkins MCP에 붙어 중계 | **채택** |
| B. Jenkins REST API 직접 구현 | 플러그인 기능을 따라가기 어렵다 |

노출 방식:

| 대안 | 판단 |
| --- | --- |
| 게이트웨이 Tool 3개(`jenkins_list_instances`, `jenkins_list_tools`, `jenkins_call_tool`) | **채택**. 인스턴스별 목록·스키마를 그대로 보여준다 |
| 첫 인스턴스 Tool을 이름 접두사로 직접 노출 + 목록 Tool | 다른 인스턴스 전용 Tool 호출 경로가 따로 필요하다 |
| 인스턴스별 Tool 이름 접두사 | Tool 수가 N배 |

인스턴스 선택: 필수 `instance` 인자. 생략하면 오류이며 기본 인스턴스는 없다.

전송: Streamable HTTP만 쓴다.

등록 항목: 인스턴스마다 이름, Endpoint(`/mcp-server/mcp` 전체 URL), User, ApiToken, Description(용도 설명. 에이전트가 상황에 맞는 인스턴스를 고르는 근거). 토큰 암복호는 소비 서버 책임이다.

## 비범위

SSE 전송, crumb 처리, 토큰 암복호, 자동 재시도, 여러 인스턴스 목록 병합, upstream Tool 목록 변경 알림 중계, github-proxy와의 공통 코어 추출.
