---
status: done
category: github
feature: github-ref-tools
phase: 방안
---

# github-ref-tools 방안

GitHub 이슈: [#1 GitHub 태그·ref 쓰기 Tool 추가](https://github.com/SeongSikChae/Biz.Bizadm.MCP.ToolKit/issues/1). 릴리스(#2)는 별도 기능 `github-release-tools`로 이어서 진행하고, 둘 다 1.4.0으로 낸다.

## 문제

- GitHub MCP Server는 태그·ref를 만들거나 바꾸는 Tool을 노출하지 않는다. 기본 `/mcp/`(46개)와 전체 `/mcp/x/all`(113개) 모두 `list_tags`, `get_tag` 조회뿐이고, `X-MCP-Toolsets`·`X-MCP-Readonly` 헤더로도 추가되지 않는다.
- 같은 fine-grained PAT로 GitHub REST API를 직접 부르면 된다. Contents Read and write에서 `POST /git/refs` 201, `DELETE` 204, Read-only에서 403(실측).

## 목표

등록한 GitHub 인스턴스의 Token으로 GitHub REST API를 직접 호출해 태그·ref를 만들고, 옮기고, 지운다.

## 대안

노출 방식:

| 대안 | 판단 |
| --- | --- |
| 전용 MCP Tool 추가 | **채택**. 에이전트가 바로 보고 호출하며 입력 스키마가 명확하다 |
| 게이트웨이(`github_list_tools`/`github_call_tool`)에 ToolKit Tool을 합침 | upstream Tool과의 이름 충돌·구분 규칙이 필요하다 |

REST API 주소:

| 대안 | 판단 |
| --- | --- |
| `GitHubInstance.ApiEndpoint` **필수** | **채택**. 인스턴스마다 명시한다(github.com `https://api.github.com/`, Enterprise `https://{host}/api/v3/`) |
| 선택, 기본값 `https://api.github.com/` | 기존 설정은 그대로지만 Enterprise 누락을 알아채기 어렵다 |
| MCP Endpoint에서 유도 | 원격 MCP(`api.githubcopilot.com`)와 REST 호스트가 달라 규칙으로 정할 수 없다 |

## 채택 방안

- 인스턴스 선택은 게이트웨이와 같이 필수 `instance` 인자, 기본 인스턴스 없음.
- 범위(Tool 이름은 설계에서 확정):

| 기능 | REST API |
| --- | --- |
| 가벼운 태그 생성 | `POST /repos/{owner}/{repo}/git/refs` (`refs/tags/{tag}`, `sha`) |
| 주석 태그 생성 | `POST /repos/{owner}/{repo}/git/tags`(태그 객체) → `POST /git/refs` |
| ref 이동 | `PATCH /repos/{owner}/{repo}/git/refs/{ref}` (`sha`, `force`) |
| ref 삭제 | `DELETE /repos/{owner}/{repo}/git/refs/{ref}` (태그·브랜치) |

- 필요 권한: Contents: Read and write. 권한 검사는 GitHub가 한다. 403·404·422 등 REST 오류는 `isError` 결과로 돌려주고 Token은 메시지·결과에 넣지 않는다.
- **호환성**: `ApiEndpoint`가 필수가 되므로 `ApiEndpoint` 없는 기존 `WithGitHubTools` 설정은 1.4.0에서 등록 시 `ArgumentException`이다. MCP.Server 설정 모델·`config.yml`에도 항목 추가가 필요하다(별도 작업).

## 비범위

릴리스 쓰기(#2, 별도 기능), 게이트웨이 3개 동작 변경, 브랜치 생성(upstream `create_branch` 사용), 브랜치 보호·ruleset, GraphQL, 자동 재시도, Token 암복호, MCP.Server 변경.
