---
status: done
category: github
feature: github-proxy
phase: 설계
---

# github-proxy 설계

선행: `0001-github-proxy-방안.md`, Jenkins 설계 `docs/jenkins/0002-jenkins-proxy-설계.md`

## 공개 API

GitHub (추가):

| 타입 | 멤버 | 설명 |
| --- | --- | --- |
| `GitHub.GitHubOptions` | `Instances` | 등록 인스턴스 목록(`IList<GitHubInstance>`). 기본 인스턴스 없음 |
| `GitHub.GitHubInstance` | `Name` | 인스턴스 이름. `instance` 인자 값 |
| | `Endpoint` | GitHub MCP 주소 전체(예: `https://api.githubcopilot.com/mcp/`) |
| | `Token` | Bearer 인증용 PAT |
| | `Description` | 용도 설명(선택) |
| `GitHubMcpServerBuilderExtensions` | `WithGitHubTools(Action<GitHubOptions>)` | 옵션 검증 후 서비스와 Tool 등록. 네임스페이스 `Microsoft.Extensions.DependencyInjection` |
| `Tools.GitHub.GitHubTools` | `github_list_instances`, `github_list_tools`, `github_call_tool` | Jenkins 3개 Tool과 인자·결과·주석 동일 |

Jenkins: 공개 API(`JenkinsOptions`, `JenkinsInstance`, `WithJenkinsTools`, `JenkinsTools`)와 `jenkins_*` Tool 이름·인자·결과·오류 메시지는 그대로 둔다.

## 공통 코어 (`Relay` 네임스페이스, 모두 internal)

| 타입 | 역할 |
| --- | --- |
| `McpRelayTarget` | 중계 대상 한 개: `Name`, `Endpoint`, `Description`, `Headers`(인증 헤더 등). 서비스별 Instance에서 만든다 |
| `McpRelayConnection` | `McpClient` + 소유 transport. 기존 `JenkinsConnection`을 옮긴다 |
| `McpRelayPool` | 기존 `JenkinsMcpClientPool` 로직을 옮긴다. 생성 인자 `label`("Jenkins"/"GitHub")과 대상 목록. 테스트용 연결 생성기 주입 생성자 유지 |
| `McpRelayTools` | Tool 본문 공통 구현: 인스턴스 목록 JSON, Tool 목록 JSON, 호출, `ToArguments` |
| `McpRelayValidation` | 등록 공통 검증: 목록 비어 있음, null 항목, 이름 공백·중복, Endpoint 절대 http/https·사용자 정보 없음 |

- 서비스별 풀(`JenkinsMcpClientPool`, `JenkinsConnection`)은 제거한다.
- 오류·안내 메시지의 서비스 이름은 `label`로 채운다. Jenkins 메시지는 기존 문자열과 같게 나온다.
  - `instance is required. Registered instances: ...`
  - `Unknown {label} instance '{name}'. Registered instances: ...`
  - `{label} '{instance}' returned error {code}: {message}`
  - `{label} '{instance}' request failed[ (HTTP {status})]: {message}`
- transport `Name`은 `{label 소문자}-{instance}` (Jenkins는 기존과 같은 `jenkins-{name}`).
- 연결 캐시·Tool 목록 캐시·예외 분류(프로토콜 오류는 연결 유지, 그 밖은 폐기 후 백그라운드 dispose, 호출자 취소는 그대로 전파)·dispose 동작은 Jenkins 설계 그대로다.

## DI

- 풀 타입이 하나이므로 keyed singleton으로 등록한다: 키 `"jenkins"`, `"github"`.
- `JenkinsTools(IServiceProvider)`는 `GetRequiredKeyedService<McpRelayPool>("jenkins")`, `GitHubTools`는 `"github"`로 풀을 얻는다.
- `WithJenkinsTools`는 기존처럼 검증한 `JenkinsOptions`도 singleton으로 등록한다. `WithGitHubTools`도 `GitHubOptions`를 등록한다.

## GitHub 검증(`ArgumentException`)

- 공통 검증(위)에 더해 `Token` 공백 아님. `Description`은 선택
- 메시지에 토큰을 넣지 않는다. 검증한 값은 복사본으로 보관한다
- 인증 헤더: `Authorization: Bearer {Token}`

## 테스트

- `JenkinsMcpClientPoolTests` → 공통 코어 `McpRelayPoolTests`로 옮긴다(동작 단언 유지)
- `FakeJenkinsServer` → 인증 헤더를 인자로 받는 `FakeMcpServer`로 일반화
- Jenkins 등록·Tool 테스트는 공개 동작 단언을 유지하고 내부 타입 참조만 바꾼다
- GitHub 등록·Tool 테스트 추가(Bearer 헤더, 검증 오류, 3개 Tool, Jenkins와 동시 등록)

## 결정

1. 공통 풀 하나(`McpRelayPool`)만 두고 서비스별 풀은 제거한다
2. 풀은 keyed singleton(`"jenkins"`, `"github"`)으로 구분한다
3. Tool 클래스는 서비스별로 두고(`[McpServerTool]` 이름이 정적이므로) 본문은 `McpRelayTools`에 위임한다
4. 메시지·transport 이름은 `label`로 만들고 Jenkins 문자열은 바꾸지 않는다
5. 표기는 `GitHub`, Tool 접두사는 `github_`
6. GitHub 인증은 Bearer PAT, 추가 헤더 등록 없음
7. ReadMe에 GitHub 등록 방법과 3개 Tool을 추가하고 패키지 버전을 1.3.0으로 올린다
