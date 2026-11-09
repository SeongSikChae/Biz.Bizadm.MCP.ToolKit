---
status: done
category: github
feature: github-proxy
phase: 방안
---

# github-proxy 방안

## 문제

ToolKit을 쓰는 MCP Server에서 GitHub 작업도 하려 한다. GitHub 계정·조직(github.com, GitHub Enterprise 등)이 여럿일 수 있고 상황에 따라 다른 곳을 제어해야 한다. GitHub는 공식 GitHub MCP Server를 제공한다.

| 엔드포인트 | 용도 |
| --- | --- |
| `https://api.githubcopilot.com/mcp/` | GitHub 원격 MCP (Streamable HTTP) |
| 자체 호스팅 `github-mcp-server` HTTP 모드 | GitHub Enterprise 등 |

인증은 `Authorization: Bearer {PAT}`다.

## 목표

- GitHub 인스턴스를 N개 등록하고, 호출마다 인스턴스를 고른다.
- 인스턴스마다 Tool 목록이 다를 수 있으므로 선택한 인스턴스에서 목록을 가져와 보여준다.
- Jenkins 중계와 같은 동작을 공통 코어로 공유한다.

## 대안

연결 방식:

| 대안 | 판단 |
| --- | --- |
| A. ToolKit이 MCP Client로 GitHub MCP Server에 붙어 중계 | **채택** |
| B. GitHub REST API 직접 구현 | 기능 추가마다 코드가 필요하다 |

코드 구조:

| 대안 | 판단 |
| --- | --- |
| Jenkins 코드를 두고 GitHub를 별도 구현 | 연결 풀·오류 감싸기·arguments 파싱이 중복된다 |
| 공통 중계 코어 추출, Jenkins·GitHub가 함께 사용 | **채택**. Jenkins 공개 API·Tool 동작은 유지한다 |

노출 방식: Jenkins와 같이 게이트웨이 Tool 3개(`github_list_instances`, `github_list_tools`, `github_call_tool`).

인스턴스 선택: 필수 `instance` 인자. 기본 인스턴스는 없다.

전송: Streamable HTTP만 쓴다.

등록 항목: 인스턴스마다 이름, Endpoint(전체 URL), Token(PAT), Description(용도 설명). 토큰 암복호는 소비 서버 책임이다.

## 비범위

GitHub REST API 직접 구현, `X-MCP-Toolsets`·`X-MCP-Readonly` 등 추가 헤더 등록, SSE 전송, 토큰 암복호, 자동 재시도, 여러 인스턴스 목록 병합, upstream Tool 목록 변경 알림 중계, Jenkins 공개 API·Tool 동작 변경.
