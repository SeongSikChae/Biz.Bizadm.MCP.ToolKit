---
status: done
category: jenkins
feature: jenkins-proxy
phase: 설계
---

# jenkins-proxy 설계

선행: `0001-jenkins-proxy-방안.md`

## 공개 API

| 타입 | 멤버 | 설명 |
| --- | --- | --- |
| `Jenkins.JenkinsOptions` | `Instances` | 등록 인스턴스 목록(`IList<JenkinsInstance>`). 기본 인스턴스는 없다 |
| `Jenkins.JenkinsInstance` | `Name` | 인스턴스 이름. `instance` 인자 값 |
| | `Endpoint` | Jenkins MCP 주소 전체(예: `http://host:8080/mcp-server/mcp`) |
| | `User`, `ApiToken` | Basic 인증용 Jenkins 사용자와 API Token |
| | `Description` | 용도 설명(선택). 에이전트가 인스턴스를 고르는 근거 |
| `JenkinsMcpServerBuilderExtensions` | `WithJenkinsTools(Action<JenkinsOptions>)` | 옵션 검증 후 서비스와 Tool 등록. 네임스페이스는 Redmine과 같이 `Microsoft.Extensions.DependencyInjection` |

## 검증(`ArgumentException`)

- `Instances` 1개 이상, 항목 null 아님
- `Name` 공백 아님·중복 없음(대소문자 무시)
- `Endpoint` 절대 http/https URI, 사용자 정보(`user:pass@`) 없음
- `User`, `ApiToken` 공백 아님. `Description`은 선택
- 예외 메시지에 토큰을 넣지 않는다
- 검증한 값은 복사본으로 보관한다. 등록 뒤 소비 코드가 옵션을 바꿔도 반영되지 않는다

## 노출 Tool (`JenkinsTools`, `[McpServerToolType]`)

| Tool | 인자 | 결과 | 주석 |
| --- | --- | --- | --- |
| `jenkins_list_instances` | 없음 | JSON 문자열 `[{name, description, endpoint}]`. User·ApiToken 제외 | ReadOnly, Idempotent |
| `jenkins_list_tools` | `instance`(필수), `refresh`(기본 false) | JSON 문자열 `{instance, tools:[{name, title, description, inputSchema, annotations}]}` | ReadOnly, OpenWorld |
| `jenkins_call_tool` | `instance`(필수), `tool`(필수), `arguments`(JSON 객체 또는 JSON 객체 문자열, 선택) | upstream `CallToolResult` 그대로 | Destructive, OpenWorld |

- `jenkins_list_tools`는 인스턴스별로 캐시한다. 첫 조회 또는 `refresh=true`일 때만 upstream `tools/list`를 보낸다. 조회가 실패하면 캐시하지 않는다.
- `jenkins_call_tool`은 캐시 목록으로 Tool 이름을 검사하지 않고 그대로 upstream에 보낸다. 없는 Tool이면 upstream 오류가 돌아간다.
- `arguments`가 JSON 객체면 그 속성을 그대로 upstream 인자로 넘긴다. JSON 문자열이면 JSON 객체로 파싱해 같은 방식으로 넘긴다(일부 클라이언트는 객체 인자를 문자열로 보낸다). null·생략·빈 문자열은 인자 없음이다. 파싱 실패 또는 객체가 아닌 값(배열·숫자 등)이면 오류다.

## 구조

- `internal sealed class JenkinsMcpClientPool : IAsyncDisposable, IDisposable` (DI singleton). MS DI는 동기 `Dispose()`에서 `IAsyncDisposable`만 구현한 서비스에 예외를 던지므로 둘 다 구현한다.
  - 인스턴스별 연결(`JenkinsConnection`: `McpClient` + 소유 자원) 지연 생성·캐시. `HttpClientTransport`(`HttpTransportMode.StreamableHttp`) + `Authorization: Basic base64(User:ApiToken)`
  - `McpClient.DisposeAsync`는 세션 전송만 정리하고 `HttpClientTransport`가 소유한 HttpClient는 정리하지 않는다(SDK 2.3.1). 그래서 연결은 `HttpClientTransport`를 소유 자원으로 함께 보관하고, client dispose 뒤 transport도 dispose한다. `McpClient.CreateAsync`가 실패해도 transport를 dispose한다.
  - 인스턴스별 Tool 목록 캐시
  - 테스트용 연결 생성기 주입: `internal JenkinsMcpClientPool(JenkinsOptions, Func<JenkinsInstance, CancellationToken, Task<JenkinsConnection>>)`
- `instance` 생략·공백·미등록, `tool` 생략·공백, `arguments`가 객체 아님: upstream을 호출하지 않고 `McpException`(SDK가 `IsError` 결과로 변환). `instance` 문제면 등록 이름 목록을 안내한다.
  - 인자 자체를 생략하면 SDK 바인딩이 일반 오류로 끝나 안내가 나오지 않으므로 `instance`, `tool` 매개변수는 nullable·기본값 null로 받고 직접 검사한다. 그 결과 입력 스키마의 `required`에서는 빠지며, 필수임은 설명에 적는다.
- upstream 연결·호출이 실패하면 모두 인스턴스 이름을 담은 `McpException`으로 감싸 던지고 원 예외는 `InnerException`으로 보존한다. User·ApiToken·Authorization 헤더 값은 넣지 않는다.
  - 원 예외를 그대로 던지면 SDK가 일반 메시지("An error occurred invoking ...")로 바꾸거나(일반 예외), JSON-RPC 오류로 다시 던져(`McpProtocolException`) 에이전트가 결과로 받지 못하기 때문이다.
  - upstream 프로토콜 오류(`McpProtocolException`): client 유지. 메시지 `Jenkins '{instance}' returned error {ErrorCode}: {Message}`
  - 그 밖의 예외(전송 계열, SDK의 일반 `McpException`, 호출자 취소가 아닌 `OperationCanceledException` 포함): client 폐기. 메시지 `Jenkins '{instance}' request failed[ (HTTP {status})]: {Message}`. HTTP 상태는 InnerException 사슬의 `HttpRequestException.StatusCode`가 있을 때만 붙인다. 다음 호출에서 재연결한다. Tool 목록 캐시는 지우지 않는다.
  - 호출자 취소는 client를 유지하고 그대로 전파한다.
- 폐기한 연결의 dispose는 기다리지 않고 백그라운드로 실행한다. SDK가 세션 종료 DELETE를 `CancellationToken.None`으로 보내 응답 없는 호스트에서 최대 HttpClient 시간 초과만큼 걸리므로, 실패 응답이 그만큼 늦어지지 않게 한다. pool dispose는 남은 연결을 병렬로 dispose하고 기다린다.
- Endpoint에 사용자 정보(`http://user:token@host/...`)가 있으면 등록 시 `ArgumentException`. `jenkins_list_instances`로 자격 증명이 노출되는 것을 막는다.

## 결정

1. 게이트웨이 Tool 3개, 이름 접두사 `jenkins_` 고정
2. 인스턴스 선택은 필수 `instance` 인자, 기본 인스턴스 없음
3. 전송은 Streamable HTTP만, Basic 인증
4. Tool 목록은 인스턴스별 캐시 + `refresh` 인자
5. `jenkins_list_tools`는 Tool 전체 정보를 한 번에 반환
6. `jenkins_call_tool`은 upstream `CallToolResult`를 그대로 반환
7. 재시도 없음. 전송 계열 예외에서만 client 폐기
8. ReadMe에 Jenkins Tool 등록 방법과 3개 Tool을 추가한다
9. 전송 계열 오류는 인스턴스 이름·HTTP 상태·예외 메시지를 담은 `McpException`으로 감싼다(토큰 제외)
10. `arguments`는 JSON 객체와 JSON 객체 문자열을 모두 받는다
11. (독립 검토 반영) upstream 프로토콜 오류도 인스턴스 이름을 담은 `McpException`으로 감싸 결과(`IsError`)로 전달하고 client는 유지한다. 9번을 SDK 일반 `McpException`까지 넓힌다
12. (독립 검토 반영) 연결은 `HttpClientTransport`를 함께 소유·dispose하고, 폐기 dispose는 백그라운드로 실행한다
13. (독립 검토 반영) `instance`·`tool`은 nullable로 받아 직접 검사하고, Endpoint 사용자 정보는 거부한다
