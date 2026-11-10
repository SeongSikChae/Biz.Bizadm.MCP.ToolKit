---
status: done
category: jenkins
feature: jenkins-job-config
phase: 설계
---

# jenkins-job-config 설계

선행: `0004-jenkins-job-config-방안.md`, `0002-jenkins-proxy-설계.md`, 참고 `../github/0006-github-ref-tools-설계.md`

## 공개 API

| 타입 | 멤버 | 변경 |
| --- | --- | --- |
| `JenkinsInstance`, `JenkinsOptions` | - | 변경 없음 |
| `JenkinsMcpServerBuilderExtensions` | `WithJenkinsTools` | 등록 검증은 그대로. `JenkinsRestClient` singleton과 `JenkinsJobTools`를 함께 등록 |
| `Tools.Jenkins.JenkinsJobTools` | 아래 4개 Tool | **추가** (`[McpServerToolType]`) |

게이트웨이 3개(`JenkinsTools`)의 이름·인자·결과는 바꾸지 않는다.

## REST 기준 주소

- `Endpoint`의 경로가 `/mcp-server/mcp`(끝 `/` 허용, 대소문자 무시)로 끝나면 그 부분을 뗀 주소가 Jenkins 루트다. 컨텍스트 경로는 유지한다(예: `http://h/jenkins/mcp-server/mcp` → `http://h/jenkins/`). 끝에 `/`를 붙인다.
- 끝이 다르면 루트는 없음(null)이다. 등록은 성공하고, 그 인스턴스로 REST Tool을 부르면 요청 없이 `McpException`: `Jenkins '{name}' Endpoint does not end with /mcp-server/mcp; REST tools are unavailable.`
- 루트는 `JenkinsRestClient` 생성 시 인스턴스마다 한 번 계산한다.

## Job 경로

- 인자 `job`: Jenkins 전체 이름(`folder/sub/job`). 앞뒤 공백과 앞뒤 `/`는 뗀다.
- 세그먼트마다 URL 인코딩해 `job/{s1}/job/{s2}/...`로 만든다. 빈 세그먼트·`.`·`..`이면 요청 전 `McpException`(루트 밖 경로 방지, GitHub과 같은 규칙). `\`가 들어 있어도 요청 전 `McpException`이다.
- 전체 이름만 받는다. 표시 이름(display name)이 아닌 Job 이름이며, `job/a/job/b`나 URL은 받지 않는다. 둘 다 Tool 설명에 적는다. Multibranch 브랜치 이름에 들어 있는 `%2F`는 그대로 인코딩해 `%252F`로 보낸다(Jenkins가 기대하는 형태).

## 노출 Tool (`JenkinsJobTools`)

공통 인자: `instance`(필수), `job`(필수). 필수 인자는 nullable로 받아 직접 검사하고, 누락·공백이면 요청하지 않고 `McpException`이다.

| Tool | 추가 인자 | REST | 주석 |
| --- | --- | --- | --- |
| `jenkins_get_job_config` | - | `GET {job}/config.xml` | 읽기 전용, 멱등, 외부 |
| `jenkins_update_job_config` | `configXml`(필수) | `POST {job}/config.xml`, 본문 그대로, `Content-Type: application/xml; charset=utf-8` | 쓰기, **파괴적**, 멱등, 외부 |
| `jenkins_update_pipeline_script` | `script`(필수, 빈 문자열 불가) | `GET config.xml` → `<script>` 교체 → `POST config.xml` | 쓰기, **파괴적**, 멱등, 외부 |
| `jenkins_delete_job` | - | `POST {job}/doDelete` | 쓰기, **파괴적**, 멱등, 외부 |

- `jenkins_delete_job` Tool 설명과 README에 적는다: 폴더·Multibranch Job이면 그 안의 모든 Job과 빌드 기록까지 지운다. 이미 없는 Job이면 404 오류다(멱등 주석은 서버 상태 기준이다).

### `<script>` 교체

- 대상은 루트 `flow-definition`의 자식 `definition` 중 `class`가 `org.jenkinsci.plugins.workflow.cps.CpsFlowDefinition`인 요소의 자식 `script` 하나다. 없으면(`CpsScmFlowDefinition` = SCM Jenkinsfile, Freestyle Job 등) 올리지 않고 `McpException`: `Jenkins '{name}' job '{job}' is not an inline Pipeline script job (definition class: {class 또는 none}).`
- Jenkins는 `<?xml version='1.1' encoding='UTF-8'?>` 선언을 쓰는데 .NET XML 파서는 1.1을 읽지 못한다. 그래서 맨 앞 BOM을 먼저 떼고(선언 유무와 무관), 문서 맨 앞의 XML 선언을 떼어 DTD를 금지한 `XmlReader`로 파싱하고(`LoadOptions.PreserveWhitespace`), 저장할 때 원래 선언 문자열을 그대로 앞에 붙인다. 선언이 없으면 붙이지 않는다.
- `script` 요소의 내용을 새 텍스트 노드 하나로 바꾼다(특수문자는 XML 직렬화가 이스케이프). 다른 요소·속성·공백은 유지한다.
- 직렬화는 `XmlWriterSettings { OmitXmlDeclaration = true, Indent = false, NewLineHandling = Entitize }`로 문서를 쓴다(들여쓰기 없음, OS 개행으로 바꾸지 않음). `<script>` 밖 내용은 **의미가 같다**(XML 정보 집합 기준). 문자열은 달라질 수 있다(`<a/>` → `<a />`, `&quot;` → `"`, 문자 참조 → 문자).
- 개행: 파서가 `\r\n`을 `\n`으로 정규화한다. 새 스크립트의 `\r`은 `Entitize`가 `&#xD;`로 써서 그대로 보존된다. `previousScriptLength`와 `scriptLength`는 모두 `\r\n`·`\r`을 `\n`으로 정규화한 뒤의 길이다(같은 기준으로 비교 가능).
- 제한: XML 1.0 파서를 쓰므로 config.xml이나 새 스크립트에 XML 1.0에서 허용하지 않는 제어 문자(`&#1;` 등)가 있으면 처리하지 않는다. 파싱 실패(`XmlException`)와 직렬화 실패(`ArgumentException`)는 모두 `McpException`(`... config.xml is not valid XML 1.0: {메시지}` / `... script contains characters not allowed in XML 1.0`)이고 POST하지 않는다.
- GET과 POST 사이의 다른 변경은 막지 않는다(Jenkins에 조건부 갱신이 없다).

## 결과 (성공)

- `jenkins_get_job_config`: config.xml 원문 문자열. Jenkins가 암호화해 저장한 secret(`{AQAAAB...}`)도 그대로 들어 있을 수 있다(README에 명시)
- `jenkins_update_job_config`: `{"updated":"{job}"}`
- `jenkins_update_pipeline_script`: `{"updated":"{job}","previousScriptLength":n,"scriptLength":m}`
- `jenkins_delete_job`: `{"deleted":"{job}"}`

`{job}`은 정규화한 전체 이름(`folder/sub/job`)이다.

## 오류

- 성공 판정: 2xx. **자동 리다이렉트는 끈다**. 따라가면 POST가 302 뒤 GET으로 바뀌어 실패가 가려지고, 로그인 페이지 리다이렉트가 200 성공으로 보이기 때문이다.
- `doDelete`는 성공 시 302로 부모(폴더 또는 루트)로 보내지만, 리다이렉트 전에 이미 삭제가 끝났을 수 있고 로그인 리다이렉트와 구분할 수 없다. 그래서 `doDelete` 응답이 2xx·3xx이면 `GET {job}/config.xml`로 확인한다. 404면 성공, 그 밖(200·3xx·다른 오류)이면 실패 `Jenkins '{instance}' POST {path} returned HTTP {code}[ ({Location})]; job '{job}' was not deleted or could not be verified (GET config.xml: HTTP {code2}).` (`[ ]`는 Location이 있을 때만). 확인 GET이 전송 오류로 실패해도 같은 형식으로 `(GET config.xml: {예외 메시지})`를 쓴다. `doDelete`의 4xx·5xx는 일반 실패 규칙을 따른다.
- `doDelete` 외 요청의 3xx는 실패이며 메시지에 `Location`을 넣는다.
- 실패: `McpException` → `isError`: `Jenkins '{instance}' {METHOD} {path} failed (HTTP {code}): {요약}`. 요약은 HTML(`<html` 또는 `<title` 포함)이면 `<title>` 내용(대소문자 무시, 여러 줄 허용), title이 없으면 태그를 제거한 텍스트. 5xx면 title 뒤에 ` - ` + 태그 제거 텍스트를 덧붙인다(Jenkins 오류 페이지 title은 일반 문구). HTML이 아니면 본문 그대로. HTML이면 엔티티를 디코딩하고, 모두 공백을 줄인 뒤 앞 200자.
- 403 본문에 `crumb`이 있으면 요약 뒤에 `(ApiToken must be a Jenkins API token, not a password)`를 붙인다.
- `jenkins_update_job_config`는 받은 XML을 그대로 UTF-8로 보낸다. XML 선언의 encoding은 검사하지 않는다(Tool 설명에 명시).
- 전송 오류·시간 초과(호출자 취소 제외): `Jenkins '{instance}' {METHOD} {path} failed: {예외 메시지}`.
- User·API Token·Authorization 헤더 값은 메시지·결과에 넣지 않는다. 재시도하지 않는다.

## 구조

- `internal sealed class JenkinsRestClient : IDisposable` (DI singleton, `Jenkins/` 폴더)
  - 생성 인자: 검증한 `JenkinsOptions`, 선택 `HttpMessageHandler`(테스트 주입). 기본은 `SocketsHttpHandler { PooledConnectionLifetime = 2분, AllowAutoRedirect = false }`
  - 요청 헤더: `Authorization: Basic base64(User:ApiToken)`, `User-Agent: Biz.Bizadm.MCP.ToolKit/{버전}`. crumb은 보내지 않는다(API Token 인증)
  - `Resolve(name)`: 미등록·공백이면 게이트웨이와 같은 형식(`Unknown Jenkins instance '{name}'. Registered instances: ...`). 찾은 인스턴스의 REST 루트가 없으면 여기서 실패한다(요청 본문을 만들기 전)
  - `SendAsync(instance, method, jobPath, action, content?, cancellationToken)` → 응답 본문 문자열. 2xx만 성공
  - `SendRawAsync(instance, method, jobPath, action, content?, cancellationToken)` → `(int Status, Uri? Location, string Body)`. 상태 판정 없이 돌려준다(`doDelete`와 삭제 확인 GET에 사용). 전송 오류 처리는 같다
- `JenkinsJobTools`는 `JenkinsRestClient`만 쓴다. 게이트웨이 풀(`McpRelayPool`)과 독립이다.
- `<script>` 교체는 `internal static` 순수 함수(`PipelineScript.Replace(xml, script)`)로 분리해 단위 테스트한다.

## 테스트

- 등록: Jenkins Tool 7개(게이트웨이 3 + Job 4), 주석, `/mcp-server/mcp`가 아닌 Endpoint도 등록 성공
- 루트 유도: 기본, 컨텍스트 경로, 끝 `/`, 대소문자, 불일치 시 호출 오류(요청 없음)
- 요청 계약(가짜 `HttpMessageHandler`): Tool마다 메서드·URL·본문·Content-Type·Basic 헤더, 폴더 경로 인코딩, `%2F` → `%252F`, `.`/`..`/빈 세그먼트/`\` 거부
- `<script>` 교체: 1.1 선언 보존, 선언 없이 BOM만 있는 XML, 특수문자 이스케이프, `<script>` 밖 내용 의미 동일, CRLF 스크립트 왕복과 길이 기준, 제어 문자 거부, SCM·Freestyle 거부, 잘못된 XML
- `<script>` 밖 비교는 재파싱 후 `XNode.DeepEquals`(문자열 비교 아님). URL 비교는 `AbsoluteUri`
- Tool 설명: `jenkins_delete_job`에 하위 항목 삭제·404 문구 포함
- 결과·오류: 삭제 실패 메시지 전체 문자열, 삭제 302+확인 GET 404 성공, 삭제 200+확인 GET 404 성공, 삭제 302(`/login`)+확인 GET 200 실패, 확인 GET 403 실패, 삭제 403은 확인 GET 없이 실패, 다른 요청 302 실패, 401·404 메시지, HTML title·태그 제거 요약, 403 crumb 안내, 메시지에 토큰 없음

## 결정

1. 전용 Tool 4개, 접두사 `jenkins_`, `JenkinsJobTools` 별도 클래스, `WithJenkinsTools`에 항상 포함
2. REST 루트는 `Endpoint`에서 유도, 불일치는 호출 시 오류(등록 검증 불변)
3. `<script>` 교체는 XML 선언을 떼어 파싱 후 원래 선언 복원
4. 자동 리다이렉트 끔. `doDelete`의 2xx·3xx는 결정 7대로 확인 GET으로 판정하고, 다른 요청의 3xx는 실패
5. 오류는 `McpException`(→ `isError`), 재시도 없음, 자격 증명 비노출
6. ReadMe에 4개 Tool을 추가한다. 버전 올림은 이 작업에 포함하지 않는다
7. (재검토 반영) `doDelete`의 2xx·3xx는 `GET config.xml` 404로 삭제를 확인해 판정(`Location` 검사 없음)
8. (독립 검토 반영) 직렬화는 `XmlWriterSettings`(들여쓰기 없음, `Entitize`) 하나로 통일, 제어 문자는 XML 1.0 제한으로 거부, CRLF 보존과 `previousScriptLength` 기준 명시
9. (독립 검토 반영) 오류 요약은 title → 태그 제거 텍스트 → 본문, 403 crumb은 ApiToken 안내
10. (독립 검토 반영) Job은 전체 이름만, `\` 거부. get 결과의 암호화 secret과 update의 UTF-8 전송은 README·Tool 설명에 명시(encoding 선언 검사는 하지 않음)
11. (재검토 반영) `<script>` 밖은 의미 동일(문자열 동일 아님), 비인라인 판정은 예외 종류가 아니라 `Replace` 결과의 definition class로 한다
12. (3차 검토 반영) 삭제 Tool 설명·README에 하위 항목 삭제와 404 명시, 두 스크립트 길이는 개행 정규화 뒤 기준, BOM은 선언과 무관하게 먼저 뗀다
14. (5차 검토 반영, 사용자 결정으로 재검토 없이 구현) 확인 GET 전송 오류도 삭제 확인 실패 형식, 5xx 요약에 본문 덧붙임·비HTML은 본문 그대로, 조회 권한은 ExtendedRead 또는 Configure
13. (4차 검토 반영) 이전 스크립트 길이도 정규화 함수로 계산, title 정규식은 대소문자 무시·여러 줄·엔티티 디코딩, DTD 금지 파싱, 루트 검사는 `Resolve`에서, `job`은 표시 이름이 아닌 Job 이름
