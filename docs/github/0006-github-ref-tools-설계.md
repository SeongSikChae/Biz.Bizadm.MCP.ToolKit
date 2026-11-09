---
status: done
category: github
feature: github-ref-tools
phase: 설계
---

# github-ref-tools 설계

선행: `0005-github-ref-tools-방안.md`, `0002-github-proxy-설계.md`

## 공개 API

| 타입 | 멤버 | 변경 |
| --- | --- | --- |
| `GitHub.GitHubInstance` | `ApiEndpoint` | **추가, 필수**. GitHub REST API 기본 주소(예: `https://api.github.com/`, Enterprise `https://{host}/api/v3/`) |
| `GitHubMcpServerBuilderExtensions` | `WithGitHubTools` | 검증에 `ApiEndpoint` 추가. 태그·ref Tool도 함께 등록 |
| `Tools.GitHub.GitHubRefTools` | 아래 4개 Tool | **추가** (`[McpServerToolType]`) |

게이트웨이 3개(`GitHubTools`)의 이름·인자·결과는 바꾸지 않는다. `github_list_instances` 결과에도 `ApiEndpoint`를 넣지 않는다.

## 검증 (`ArgumentException`, 기존 GitHub 검증에 추가)

- `ApiEndpoint`: 절대 http/https URI, 사용자 정보 없음. 메시지는 Endpoint와 같은 형식(`GitHub instance '{name}' ApiEndpoint must be an absolute http or https URI` 등), 공통 검증 `McpRelayValidation`에 URI 검사 함수를 추가해 함께 쓴다.
- 정규화: 끝에 `/`가 없으면 붙인다(상대 경로 결합용). 검증한 값은 복사본으로 보관한다.

## 노출 Tool (`GitHubRefTools`)

공통 인자: `instance`(필수), `owner`(필수), `repo`(필수). 필수 인자는 게이트웨이와 같이 nullable로 받아 직접 검사하고, 누락·공백이면 요청하지 않고 `McpException`이다.

| Tool | 인자 | REST | 주석 |
| --- | --- | --- | --- |
| `github_create_lightweight_tag` | `tag`, `sha` | `POST repos/{o}/{r}/git/refs` `{"ref":"refs/tags/{tag}","sha"}` | 쓰기, 비파괴, 비멱등, 외부 |
| `github_create_annotated_tag` | `tag`, `sha`, `message`, `taggerName`·`taggerEmail`(선택, 둘 다 있거나 둘 다 없음) | ① `POST git/tags` `{"tag","message","object":sha,"type":"commit","tagger"?}` ② `POST git/refs` `{"ref":"refs/tags/{tag}","sha":<①의 sha>}` | 쓰기, 비파괴, 비멱등, 외부 |
| `github_update_ref` | `ref`, `sha`, `force`(기본 false) | `PATCH repos/{o}/{r}/git/refs/{ref}` `{"sha","force"}` | 쓰기, **파괴적**, 멱등, 외부 |
| `github_delete_ref` | `ref` | `DELETE repos/{o}/{r}/git/refs/{ref}` | 쓰기, **파괴적**, 멱등, 외부 |

- `ref`는 `tags/v1.0`, `heads/feature`, `refs/tags/v1.0`을 받는다. 앞의 `refs/`는 떼고 보낸다. 그 밖의 형식 검사는 GitHub에 맡긴다.
- 경로의 `owner`, `repo`, ref의 각 세그먼트는 URL 인코딩한다(ref의 `/`는 구분자로 유지).
- `taggerName`·`taggerEmail`을 생략하면 `tagger`를 보내지 않는다(GitHub가 토큰 사용자로 채운다). 하나만 주면 요청 전 `McpException`.

## 결과 (성공)

GitHub 응답 본문 JSON을 그대로 문자열로 돌려준다.

- 가벼운 태그·ref 이동: `git/refs` 응답 그대로(`ref`, `node_id`, `url`, `object{sha,type,url}`)
- 주석 태그: 응답이 두 개이므로 `{"tag": <git/tags 응답>, "ref": <git/refs 응답>}`
- ref 삭제: GitHub가 204(본문 없음)를 주므로 `{"deleted":"refs/{ref}"}`

## 오류

- 2xx가 아니면 `McpException`(SDK가 `isError` 결과로 변환): `GitHub '{instance}' {METHOD} {path} failed (HTTP {code}): {message}`. `message`는 응답 JSON의 `message`(있으면 `errors` 요약 덧붙임), JSON이 아니면 본문 앞 200자.
- 전송 오류·시간 초과(호출자 취소 제외): `GitHub '{instance}' {METHOD} {path} failed: {예외 메시지}`.
- 주석 태그 ②가 실패하면 메시지에 ①에서 만든 태그 객체 sha를 붙인다(참조 없는 객체로 남으며 GitHub가 정리한다).
- Token·Authorization 헤더 값은 메시지·결과에 넣지 않는다. 재시도하지 않는다.

## 구조

- `internal sealed class GitHubRestClient : IDisposable` (DI singleton)
  - 생성 인자: 검증한 `GitHubOptions`, 선택 `HttpMessageHandler`(테스트 주입). 기본은 `SocketsHttpHandler { PooledConnectionLifetime = 2분 }`
  - `HttpClient` 하나를 소유한다. 요청마다 헤더: `Authorization: Bearer {Token}`, `Accept: application/vnd.github+json`, `X-GitHub-Api-Version: 2022-11-28`, `User-Agent: Biz.Bizadm.MCP.ToolKit/{버전}`
  - `Resolve(name)`: 미등록·공백이면 게이트웨이와 같은 형식의 `McpException`(`Unknown GitHub instance '{name}'. Registered instances: ...`)
  - `SendAsync(instance, method, path, body?)` → 응답 JSON 문자열, 오류는 위 규칙
- `GitHubRefTools`는 `GitHubRestClient`만 쓴다. 게이트웨이 풀(`McpRelayPool`)과 독립이다.
- `WithGitHubTools`: `GitHubRestClient` singleton 등록, `WithTools<GitHubRefTools>()` 추가.

## 테스트

- 등록: GitHub Tool 7개(게이트웨이 3 + 태그·ref 4), 주석(파괴적 여부), `ApiEndpoint` 검증 오류에 토큰 없음, 복사본·`/` 정규화
- 요청 계약(가짜 `HttpMessageHandler`): Tool마다 메서드·URL·본문·헤더(Bearer, API 버전, User-Agent), ref 정규화·인코딩, Enterprise `ApiEndpoint` 결합
- 결과: 응답 그대로, 주석 태그 두 응답 결합, 삭제 `{"deleted"}`
- 오류: 403·422 메시지 형식, 비JSON 본문, 주석 태그 ② 실패 시 객체 sha, 입력 누락은 요청 없음, 메시지에 토큰 없음

## 결정

1. 전용 Tool 4개, 접두사 `github_`, `GitHubRefTools` 별도 클래스
2. `ApiEndpoint` 필수(호환성 깨짐, 1.4.0), 끝 `/` 정규화, `github_list_instances`에는 노출하지 않음
3. `WithGitHubTools`에 항상 포함
4. 성공 결과는 GitHub 응답 그대로(주석 태그는 두 응답 결합, 삭제는 `{"deleted"}`)
5. REST 호출은 게이트웨이 풀과 분리한 `GitHubRestClient`, 자체 `HttpClient` 1개
6. 오류는 `McpException`(→ `isError`), 재시도 없음, Token 비노출
7. ReadMe에 `ApiEndpoint`와 4개 Tool을 추가한다. 버전 1.4.0은 #2까지 끝낸 뒤 올린다
9. (독립 검토 반영) `owner`·`repo`·`ref` 경로 세그먼트가 비었거나 `.`·`..`이면 요청 전 `McpException`이다. `Uri` 결합이 dot-segment를 정리해 ref 범위 밖(예: `DELETE repos/other/repo`)으로 나가는 것을 막는다
10. (독립 검토 반영) `ApiEndpoint`에 쿼리·fragment가 있으면 등록 시 `ArgumentException`이다
8. 태그 삭제 전용 Tool은 두지 않는다. GitHub REST에도 태그 전용 삭제가 없으며 `github_delete_ref`(`tags/{tag}`)로 지운다. 주석 태그 객체는 삭제 API가 없어 ref만 지운다(사용자 확정)
