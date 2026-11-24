---
출처: R01-R1, R01-R2, R01-R3, R01-R4, R01-R5, R01-R6, R01-R7, R01-R8, R01-R9, D01_01, D01_02
상태: 끝남
---

# T01_01 마일스톤 Tool 구현

## 읽을 것

- 요구사항: `R01-R1`~`R01-R9`
- 수용 기준: `R01-A1`~`R01-A13`
- 결정: `D01_01`, `D01_02`
- 계약: `C01-I1`~`C01-I5`(공통 절 포함)

## 고칠 파일

- `src/Biz.Bizadm.MCP.ToolKit/Tools/GitHub/GitHubMilestoneTools.cs`(새 파일): 다섯 Tool
- `src/Biz.Bizadm.MCP.ToolKit/GitHubMcpServerBuilderExtensions.cs`: `WithTools<GitHubMilestoneTools>()` 등록, `WithGitHubTools` XML 주석의 Tool 목록에 마일스톤 Tool 추가
- `tests/Biz.Bizadm.MCP.ToolKit.Tests/GitHub/GitHubMilestoneToolsTests.cs`(새 파일): 요청·반환·입력 오류·GitHub 오류 테스트
- `tests/Biz.Bizadm.MCP.ToolKit.Tests/GitHub/GitHubRegistrationTests.cs`: Tool 이름 목록에 다섯 이름 추가, 마일스톤 Tool 힌트·스키마 테스트 추가
- `ReadMe.md`: 제공 Tool 표(205행 근처)에 한 줄, `GitHub Tool 등록` 절 소개문(115행)의 목록·개수, `GitHub rate limit (REST)` 절 뒤에 `GitHub 마일스톤 (REST) — Tools.GitHub.GitHubMilestoneTools` 절
- `docs/feature/github/R01_마일스톤_관리/R01_마일스톤_관리.md`: 검증 뒤 요구사항 상태·수용 기준 "최신 검증"·"지금 동작"·번호 대장(4절 문서 맞춤)
- `docs/feature/github/R01_마일스톤_관리/C01_계약.md`: 검증 뒤 항목 상태와 각 항목의 "코드" 칸
- `docs/feature/github/R01_마일스톤_관리/D01_01_마일스톤_Tool_범위.md`, `D01_02_값_목록_입력의_대소문자.md`: "코드" 절
- `docs/feature/github/R01_마일스톤_관리/V01_01_마일스톤_Tool_구현.html`(새 파일): 검증 보고서

## 할 일

### 단계 1: GitHubMilestoneTools 골격과 공통 처리
- `GitHubRateLimitTools`·`GitHubReleaseTools`와 같은 모양으로 `[McpServerToolType] public sealed class GitHubMilestoneTools`를 만든다. 공개 생성자 `(IServiceProvider services)`는 `GitHubRestClient`를 DI에서 받고, 테스트용 `internal` 생성자 `(GitHubRestClient client)`를 둔다.
- 모든 Tool 메서드는 `async Task<string>`이며, 인자는 nullable과 기본값 `null`(`instance`, `owner`, `repo`, `milestoneNumber` 포함)을 쓴다. 그래야 입력 오류가 동기 예외가 아니라 실패한 `Task`로 전달된다(`GitHubRateLimitToolsTests.GetRateLimit_MissingOrUnknownInstance_ThrowsWithoutRequest`와 같은 방식).
- 공통 처리(`C01` 공통 절, `R01-R6`):
  - `Target(instance, owner, repo)`: `_client.Resolve(instance)`와 `GitHubRestClient.RepoPath(Required(owner), Required(repo))`. 오류 문구는 `C01` 공통 오류 표와 같다.
  - `Number(milestoneNumber)`: 없거나 0 이하면 `milestoneNumber must be a positive integer.`
  - `Choice(value, name, allowed)`: 공백을 뗀 값이 `allowed`(소문자)와 서수 비교로 정확히 같지 않으면 `<name> must be one of: <a>, <b>.`(`D01_02`). 값이 없으면 null.
  - `DueOn(value)`: `DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)`이 실패하면 `dueOn must be a date in YYYY-MM-DD format.`, 성공하면 `<날짜>T12:00:00Z`.
  - 모든 입력 검사를 마친 뒤에 요청을 보낸다.
- 본문이 있어야 하는 호출은 응답이 null이면 `GitHub '<인스턴스>' <메서드> <경로> returned no content.`(`R01-R7`, `C01` 공통 GitHub 오류 표)

확인: `dotnet build Biz.Bizadm.MCP.ToolKit.slnx`가 경고 없이 끝 코드 0

### 단계 2: 다섯 Tool (`C01-I1`~`C01-I5`, `R01-R1`~`R01-R5`)
- `github_list_milestones`(`ListMilestones`): `state`(open/closed/all)·`sort`(due_on/completeness)·`direction`(asc/desc)·`perPage`(1~100)·`page`(1 이상)를 검사하고, 준 값만 `state`·`sort`·`direction`·`per_page`·`page` 순서로 쿼리를 만든다. 요청은 `GitHubRestClient.SendAsync(instance, HttpMethod.Get, Uri, path, null, ct)` 오버로드로 보내고, 요청 주소에는 쿼리를 붙이되 `path` 인자(오류 메시지용)는 쿼리 없는 `repos/{owner}/{repo}/milestones`로 준다(`R01-A11`).
- `github_get_milestone`(`GetMilestone`): `GET {repoPath}milestones/{n}`
- `github_create_milestone`(`CreateMilestone`): `title` 필수, `state`(open/closed), `description`, `dueOn`. 본문 `JsonObject`에 `title`, 준 값만 `state`, `description`, `due_on` 순서로 넣어 `POST {repoPath}milestones`
- `github_update_milestone`(`UpdateMilestone`): `title`, `state`(open/closed), `description`, `dueOn`, `clearDescription`, `clearDueOn`. `description`과 `clearDescription`, `dueOn`과 `clearDueOn`을 함께 주면 `C01-I4` 오류. 본문에 준 값만 `title`, `state`, `description`(또는 `clearDescription`이면 JSON null), `due_on`(또는 `clearDueOn`이면 JSON null) 순서로 넣고, 하나도 없으면 `at least one field to update is required.`. `PATCH {repoPath}milestones/{n}`
- `github_delete_milestone`(`DeleteMilestone`): `DELETE {repoPath}milestones/{n}` 뒤 `{"deleted":{"milestoneNumber":n}}`
- 힌트(`R01-R8`): 목록·조회 `ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true`, 생성 `ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = true`, 수정·삭제 `ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true`
- 각 Tool의 `[Description]`에 GitHub 응답을 그대로 반환한다는 것, `milestoneNumber`는 `id`가 아니라 `number`라는 것, `dueOn` 형식, 필요 권한(`C01` 권한 절)을 적는다.

확인: 단계 4의 `GitHubMilestoneToolsTests`가 통과

### 단계 3: 등록 (`R01-R8`)
- `GitHubMcpServerBuilderExtensions.WithGitHubTools`에 `builder.WithTools<GitHubMilestoneTools>();`를 `GitHubRateLimitTools` 뒤에 넣고, XML 주석 Tool 목록에 `REST 마일스톤 Tool(github_list_milestones, github_get_milestone, github_create_milestone, github_update_milestone, github_delete_milestone)`을 더한다.

확인: 단계 4의 등록 테스트가 통과

### 단계 4: 테스트
- `GitHubMilestoneToolsTests`(MSTest, `GitHubRecordingHandler`, `GitHubRestClientTests.Options()`)에 아래 검증 방법 표의 테스트 메서드를 만든다.
- `GitHubRegistrationTests.WithGitHubTools_RegistersGatewayAndRefTools`의 이름 목록에 다섯 이름을 정렬 순서로 더하고, `WithGitHubTools_MilestoneToolAnnotationsAndSchemaMatchDesign`을 더한다.

확인: `dotnet test tests/Biz.Bizadm.MCP.ToolKit.Tests/Biz.Bizadm.MCP.ToolKit.Tests.csproj`가 실패 0으로 끝 코드 0

### 단계 5: README (`R01-R9`)
- 제공 Tool 표(`| GitHub rate limit (REST) | ... | 1 |` 다음 줄)에 `| GitHub 마일스톤 (REST) | `Tools.GitHub.GitHubMilestoneTools` | `WithGitHubTools(...)` | 5 |`
- `GitHub Tool 등록` 소개문: "태그·ref 쓰기, 릴리스 쓰기, rate limit 조회" 뒤에 "마일스톤 조회·쓰기", 괄호 "(태그·ref 4개, 릴리스 5개, rate limit 1개)"에 ", 마일스톤 5개"
- `GitHub rate limit (REST)` 절 뒤, `NuGet 피드 (REST)` 절 앞에 `### GitHub 마일스톤 (REST) — `Tools.GitHub.GitHubMilestoneTools``: 한 줄 소개, Tool·파라미터·REST·힌트·반환 표(다섯 줄), 그리고 `milestoneNumber`는 `number`라는 것, `dueOn`은 `YYYY-MM-DD`이고 UTC 정오로 보낸다는 것, 값 목록은 소문자 정확히 일치, 필요 권한(`C01` 권한 절과 같은 내용, 둘 다 필요한지 하나면 되는지는 미확인), 이슈에 마일스톤 붙이기는 `github_call_tool`의 `issue_write`(`milestone`), 오류 형식·Token 비노출·재시도 없음은 태그·ref Tool과 같다는 것

확인: 검증 방법 표 `R01-A10`의 순서대로 `ReadMe.md`를 검색

### 단계 6: 검증과 문서 맞춤
- 검증 방법 표대로 확인하고 `V01_01_마일스톤_Tool_구현.html`에 적는다. 통과한 기준의 "최신 검증", 요구사항·계약 상태, `C01` "코드" 칸, `D01_01`·`D01_02` "코드" 절, 이 태스크 상태, 번호 대장을 맞춘다(spec-driven 4절).

확인: 일관성 점검(`workflow/spec-driven-documents.md`)에서 어긋남 없음

## 위험

- GitHub 쪽 동작은 미확인이다(`C01` 머리말): `due_on`을 UTC 정오로 보내면 GitHub 화면에 같은 날짜로 보이는지, `description`·`due_on`을 JSON null로 보내면 지워지는지, 삭제가 204를 돌려주는지, 필요 권한. 단위 테스트는 가짜 처리기로 요청 모양만 확인하므로 이 가정이 틀려도 통과한다. 짐작과 다르면 `R01-A4`(배포 뒤 실제 호출)에서 마감일이 하루 밀리거나, 실제 사용에서 GitHub가 422를 돌려주는 것으로 드러난다. `R01-A4`는 새 버전이 배포된 Bizadm MCP 서버가 있어야 해서 이번 작업에서 미확인으로 남을 수 있다.
- `GitHubRestClient.SendAsync(instance, method, string path, ...)`는 같은 `path`를 요청 주소와 오류 메시지에 쓴다. 목록 Tool이 이 오버로드에 쿼리 붙은 경로를 넘기면 오류 메시지에 쿼리가 들어간다. `R01-A11` 테스트가 이를 잡는다.
- `GitHubRestClient.SendAsync`의 `Uri` 오버로드는 호스트 검사를 호출자에게 맡긴다(XML 주석). 목록 Tool은 `new Uri(new Uri(instance.ApiEndpoint), path + query)`로 만들어 `ApiEndpoint` 밖으로 나가지 않게 한다. `owner`·`repo`는 `RepoPath`가 인코딩하고 쿼리 값은 검사를 통과한 고정 값과 정수뿐이다.

## 검증 방법

| 수용 기준 | 종류 | 대상 | 전제 | 순서 | 기대 결과 |
| --- | --- | --- | --- | --- | --- |
| `R01-A1` | 단위 테스트 | `GitHubMilestoneToolsTests#ListMilestones_SendsGivenQueryAndReturnsResponse`, `#ListMilestones_NoOptions_SendsWithoutQuery` | 없음 | `dotnet test tests/Biz.Bizadm.MCP.ToolKit.Tests/Biz.Bizadm.MCP.ToolKit.Tests.csproj` | 첫 테스트: 요청 1건, `GET`, URI `https://api.github.com/repos/o/r/milestones?state=all&sort=completeness&direction=desc&per_page=50&page=2`, 본문 null, 반환이 넣은 응답 본문과 같음. 둘째: URI `https://api.github.com/repos/o/r/milestones`(쿼리 없음). 두 테스트 통과 |
| `R01-A2` | 단위 테스트 | `GitHubMilestoneToolsTests#GetMilestone_GetsByNumberAndReturnsResponse` | 없음 | 위와 같은 명령 | 요청 1건, `GET`, URI `https://api.github.com/repos/o/r/milestones/3`, 반환이 응답 본문과 같음. 통과 |
| `R01-A3` | 단위 테스트 | `GitHubMilestoneToolsTests#CreateMilestone_PostsOnlyGivenFields` | 없음 | 위와 같은 명령 | 첫 요청 `POST https://api.github.com/repos/o/r/milestones` 본문 `{"title":"v1.8","state":"open","description":"notes","due_on":"2026-12-31T12:00:00Z"}`, `title`만 준 둘째 요청 본문 `{"title":"v1.8"}`. 통과 |
| `R01-A4` | 직접 호출(검증 경로: 실제 MCP 호출) | 배포된 Bizadm MCP 서버의 `mcp__Bizadm_MCP__github_create_milestone`, `github_delete_milestone` | 이 작업의 새 버전이 배포된 Bizadm MCP 서버, `SeongSikChaeGitHub` 인스턴스 Token의 마일스톤 쓰기 권한, 사용자의 생성·삭제 승인. 지금 환경에는 새 버전이 배포되어 있지 않다 | 1. `github_create_milestone`(`instance=SeongSikChaeGitHub, owner=SeongSikChae, repo=Biz.Bizadm.MCP.ToolKit, title=zz-r01-a4-test, dueOn=2026-12-31`) 2. 응답의 `due_on`·`number`·`html_url`을 적는다 3. 내장 브라우저로 `html_url`을 열어 마감일 표시를 본다 4. `github_delete_milestone`(같은 저장소, `milestoneNumber=<2의 number>`) | 2의 `due_on`이 `2026-12-31`로 시작하고, 3의 화면 마감일이 2026-12-31(영문 화면이면 `Due by December 31, 2026`)이며, 4의 반환이 `{"deleted":{"milestoneNumber":<number>}}` |
| `R01-A5` | 단위 테스트 | `GitHubMilestoneToolsTests#UpdateMilestone_PatchesOnlyGivenFields`, `#UpdateMilestone_ClearFlags_SendNull` | 없음 | 위와 같은 명령 | 첫 테스트: `PATCH https://api.github.com/repos/o/r/milestones/3` 본문 `{"state":"closed"}`. 둘째: 본문 `{"description":null,"due_on":null}`. 통과 |
| `R01-A6` | 단위 테스트 | `GitHubMilestoneToolsTests#DeleteMilestone_ReturnsDeletedNumber` | 처리기가 204를 돌려줌 | 위와 같은 명령 | 요청 1건 `DELETE https://api.github.com/repos/o/r/milestones/3`, 반환 `{"deleted":{"milestoneNumber":3}}`. 통과 |
| `R01-A7` | 단위 테스트 | `GitHubMilestoneToolsTests#InvalidInput_ThrowsWithoutRequest`(`[DynamicData]`) | 없음 | 위와 같은 명령 | 데이터 행마다(공통 오류: 다섯 Tool 각각의 `instance` 없음·미등록, `owner`·`repo` 없음, `owner`·`repo`가 `..`, 조회·수정·삭제 각각의 `milestoneNumber` 없음·0. `C01-I1`: `state=foo`, `sort=foo`, `direction=foo`, `perPage=0`, `perPage=101`, `page=0`. `C01-I3`: `title` 없음, `state=all`, `dueOn=2026-02-30`, `dueOn=2026/12/31`. `C01-I4`: 바꿀 값 없음, `state=all`, `dueOn=20261231`, `description`+`clearDescription`, `dueOn`+`clearDueOn`) `McpException` 메시지가 `C01` 표의 문구와 같고 처리기 요청 0건. 모든 행 통과 |
| `R01-A8` | 단위 테스트 | `GitHubMilestoneToolsTests#GitHubError_UsesExistingFormatWithoutToken` | 처리기가 `404 {"message":"Not Found"}` | 위와 같은 명령 | 메시지 `GitHub 'public' GET repos/o/r/milestones/3 failed (HTTP 404): Not Found`, 메시지에 `ghp_secret-token` 없음. 통과 |
| `R01-A9` | 단위 테스트 | `GitHubRegistrationTests#WithGitHubTools_RegistersGatewayAndRefTools`, `#WithGitHubTools_MilestoneToolAnnotationsAndSchemaMatchDesign` | 없음 | 위와 같은 명령 | 이름 목록에 다섯 이름 포함(정렬 비교 통과). 목록·조회: `ReadOnlyHint` true·`IdempotentHint` true·`DestructiveHint` false·`OpenWorldHint` true. 생성: false·false·false·true. 수정·삭제: false·true·true·true. 입력 스키마 `properties`에 `C01` 입력 이름(공통 `instance`·`owner`·`repo`와 Tool별 입력)이 모두 있음. 통과 |
| `R01-A10` | 실행 | `ReadMe.md` | 단계 5 완료 | 1. `grep -n "GitHub 마일스톤 (REST)" ReadMe.md` 2. `grep -n "github_list_milestones\|github_get_milestone\|github_create_milestone\|github_update_milestone\|github_delete_milestone" ReadMe.md` 3. `grep -n "마일스톤 5개" ReadMe.md` 4. `grep -n "Pull requests" ReadMe.md` | 1: 제공 Tool 표 줄과 절 제목 줄 2건. 2: 절 표에 다섯 Tool이 각 한 줄(파라미터·REST·힌트·반환 칸이 채워짐). 3: `GitHub Tool 등록` 소개문 줄 1건이고 같은 줄에 "마일스톤". 4: 마일스톤 절의 권한 줄에 Issues·Pull requests와 "미확인" |
| `R01-A11` | 단위 테스트 | `GitHubMilestoneToolsTests#ListMilestones_GitHubError_MessageHasNoQuery` | 처리기가 `422 {"message":"Validation Failed"}` | 위와 같은 명령 | `state=all`로 불러 메시지가 `GitHub 'public' GET repos/o/r/milestones failed (HTTP 422): Validation Failed`와 같고 `?` 없음. 통과 |
| `R01-A12` | 단위 테스트 | `GitHubMilestoneToolsTests#GetMilestone_EmptyBody_ThrowsNoContent` | 처리기가 200과 빈 본문 | 위와 같은 명령 | 메시지 `GitHub 'public' GET repos/o/r/milestones/3 returned no content.`. 통과 |
| `R01-A13` | 단위 테스트 | `GitHubMilestoneToolsTests#UppercaseChoice_IsRejectedWithoutRequest` | 없음 | 위와 같은 명령 | 목록 `direction=DESC` → `direction must be one of: asc, desc.`, 수정 `milestoneNumber=3, state=Closed` → `state must be one of: open, closed.`, 처리기 요청 0건. 통과 |

## 원칙 점검

- 적용 규칙: `workflow/spec-driven.md`(승인된 `R01-R1`~`R9`만 구현, 커밋은 승인 뒤), `git/github-issue.md`(커밋에 `#6` 참조, 종결은 반영 뒤), `git/commit-message.md`(`feat: [github/T01_01] #6 ...`), `workflow/repository-content.md`(문서·README에 저장소 기준 상대 경로만, Token 등 비밀 정보 없음), `dotnet/project-creation.md`(MSTest, 새 프로젝트는 만들지 않음), 전역 `mcp-verification.md`(`R01-A4`는 실제 `mcp__Bizadm_MCP__` 호출로만 판정)
- 충돌: 없음. `dotnet/nuget-pack.md`는 패키징을 하지 않으므로 해당 없음.
