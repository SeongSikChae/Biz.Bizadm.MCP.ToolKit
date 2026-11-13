---
status: done
category: nuget
feature: nuget-tools
phase: 설계
---

# nuget-tools 설계

선행: `0001-nuget-tools-방안.md`. 참고: `../jenkins/0005-jenkins-job-config-설계.md`(REST client 구조, 오류 형식).

## 공개 API

| 타입 | 멤버 | 변경 |
| --- | --- | --- |
| `NuGet.NuGetInstance` | `Name`, `ServiceIndex`, `Description?`, `ApiKey?`, `User?`, `Password?` | **추가** |
| `NuGet.NuGetOptions` | `IList<NuGetInstance> Instances` | **추가** |
| `NuGetMcpServerBuilderExtensions` | `WithNuGetTools(IMcpServerBuilder, Action<NuGetOptions>)` | **추가**. 네임스페이스 `Microsoft.Extensions.DependencyInjection` (기존 확장과 같음) |
| `Tools.NuGet.NuGetTools` | 아래 8개 Tool | **추가** (`[McpServerToolType]`) |

기존 GitHub·Jenkins·Redmine 등 등록 API와 Tool은 바꾸지 않는다. 내부 `McpRelayValidation.NameAndEndpoint`에 선택 인자 `property`(기본 `"Endpoint"`)를 추가한다. 기존 호출부와 오류 문구는 그대로다.

## 등록 검증 (`WithNuGetTools`)

등록 시 `ArgumentException`으로 실패한다. 기존 `McpRelayValidation`을 재사용한다.

- 인스턴스 1개 이상, null 항목 없음, `Name` 공백 불가·대소문자 무시 중복 불가
- `ServiceIndex`: `NameAndEndpoint(..., property: "ServiceIndex")`로 절대 http/https URI, 사용자 정보(`user@`) 불가를 검사한다. 경로가 `index.json`으로 끝나는지는 검사하지 않는다(Azure Artifacts 등 경로가 다양함)
- http도 허용한다(사용자 결정). 자격 증명이 있는 인스턴스가 http면 평문 전송된다는 점을 README에 적는다
- `User`와 `Password`는 둘 다 있거나 둘 다 없어야 한다. `User`에 `:`가 있으면 거부한다(Basic 형식)
- `ApiKey`·`User`·`Password`에 제어 문자(CR·LF 등)가 있으면 거부한다. 헤더 설정 예외 메시지로 값이 새지 않게 하기 위해서다. 오류 문구에는 값을 넣지 않는다
- `ApiKey`는 선택. 없으면 쓰기 Tool만 호출 시 실패한다
- 값은 trim한 복사본으로 저장한다(`ApiKey`·`Password`는 trim하지 않음). 공백 문자열 선택 값은 null로 본다

## service index

- `GET {ServiceIndex}`의 `resources[]`에서 `@type`으로 `@id`를 고른다. 여러 후보가 있으면 아래 표의 앞쪽 타입을 쓰고, 같은 타입이 여러 개면 첫 항목을 쓴다.

| 용도 이름(오류 문구) | `@type` 우선순위 |
| --- | --- |
| `search` | `SearchQueryService/3.5.0`, `SearchQueryService/3.0.0-rc`, `SearchQueryService/3.0.0-beta`, `SearchQueryService` |
| `package base address` | `PackageBaseAddress/3.0.0` |
| `registrations` | `RegistrationsBaseUrl/3.6.0`, `RegistrationsBaseUrl/3.4.0`, `RegistrationsBaseUrl/3.0.0-rc`, `RegistrationsBaseUrl/3.0.0-beta`, `RegistrationsBaseUrl` |
| `publish` | `PackagePublish/2.0.0` |

- `RegistrationsBaseUrl/3.6.0`은 SemVer 2.0.0 패키지를 포함하고 gzip으로 응답한다. 그래서 client는 자동 압축 해제를 켠다.
- 인스턴스마다 처음 필요할 때 한 번 읽고 프로세스 수명 동안 캐시한다(`ConcurrentDictionary<string, Lazy<Task<...>>>`). 읽기에 실패하면, 그 실패를 받은 호출자가 캐시에서 지워 다음 호출에서 다시 읽는다(실패한 Task를 이미 기다리던 호출자들은 같은 실패를 받는다). 공유 읽기는 호출자 취소 토큰을 쓰지 않고 읽기 시간 제한만 쓴다. 피드 주소가 바뀌면 재시작이 필요하다(README).
- `resources`가 없거나 배열이 아니면 unexpected JSON 오류다. `@id`가 절대 URI가 아닌 항목은 건너뛴다. `@type`이 배열이면 원소마다 항목으로 펼친다(NuGet client와 같은 동작).
- 리소스 `@id`에 query나 fragment가 있으면 경로 결합(`Child`)을 쓰는 리소스(package base address, registrations, publish)에서는 unexpected JSON 오류다. 검색은 `UriBuilder`로 query를 이어 붙인다.
- 필요한 리소스가 없으면 요청 없이 `McpException`: `NuGet '{name}' service index has no {용도 이름} resource ({타입 목록}).`

## 노출 Tool (`NuGetTools`)

공통: `instance`(필수, `nuget_list_instances`가 아닌 Tool). 필수 인자는 nullable로 받아 직접 검사하고, 누락·공백이면 요청 없이 `McpException`이다. 패키지 `id`는 trim한 뒤 NuGet ID 규칙(`^\w+([.-]\w+)*$`, 최대 100자)을 검사하고, 어긋나면 요청 전 `McpException`: `id '{id}' is not a valid NuGet package id.` (`..`·`.`·`/`가 URL 경로를 벗어나 ApiKey·Basic이 붙은 요청이 다른 경로로 가는 것을 막는다). `version`은 `NuGet.Versioning`으로 파싱해 정규화한다(파싱 실패면 요청 전 오류).

| Tool | 추가 인자 | 요청 | 주석 |
| --- | --- | --- | --- |
| `nuget_list_instances` | - | 없음 | 읽기 전용, 멱등, 닫힘 |
| `nuget_search_packages` | `query?`, `prerelease`(기본 false), `packageType?`, `skip`(기본 0), `take`(기본 20, 1~100) | `GET {search}?q=&skip=&take=&prerelease=&semVerLevel=2.0.0[&packageType=]` | 읽기 전용, 멱등, 외부 |
| `nuget_list_versions` | `id`(필수), `prerelease`(기본 true) | `GET {base}/{id 소문자}/index.json` | 읽기 전용, 멱등, 외부 |
| `nuget_get_package_metadata` | `id`(필수), `version?` | `GET {reg}/{id 소문자}/index.json` (+ 항목이 없는 page는 page `@id` GET) | 읽기 전용, 멱등, 외부 |
| `nuget_check_vulnerabilities` | `id`(필수), `version`(필수) | 메타데이터와 같음 | 읽기 전용, 멱등, 외부 |
| `nuget_push_package` | `path?`, `contentBase64?`, `skipDuplicate`(기본 false) | `PUT {publish}`, multipart `package` | 쓰기, 비파괴, 비멱등, 외부 |
| `nuget_unlist_package` | `id`(필수), `version`(필수) | `DELETE {publish}/{id}/{version}` | 쓰기, **파괴적**, 멱등, 외부 |
| `nuget_relist_package` | `id`(필수), `version`(필수) | `POST {publish}/{id}/{version}` | 쓰기, 비파괴, 멱등, 외부 |

### 세부 규칙

- 응답 JSON의 모든 속성 접근은 대상이 객체일 때만 한다(루트가 배열이거나 원소가 다른 형식이면 unexpected JSON, `JsonNode` 인덱서의 `InvalidOperationException` 방지). 응답 본문의 UTF-8 BOM은 파싱 전에 건너뛴다.
- 응답 JSON은 피드마다 필드가 다르다. 스펙상 선택 필드와 nuget.org 전용 필드(검색의 `verified`·`totalDownloads`·`versions`, registration의 `dependencyGroups`·`targetFramework`·`range`·`deprecation.message`·`alternatePackage`·`vulnerabilities`·`published`·`listed` 등)는 없으면 null(수치는 0, 배열은 빈 배열)로 둔다. 필수 필드(검색 `data[].id`·`version`, flat container `versions`의 문자열 원소, registration `items`·`catalogEntry.version`, page `@id`)가 없거나 형식이 다르면 unexpected JSON 오류다.
- 결과 JSON은 피드 응답 노드를 그대로 옮기지 않고 값을 꺼내 새 노드로 만든다(`JsonNode` 재부모화 예외 방지).
- `nuget_search_packages`: 검색 `@id`에 query가 이미 있으면 `&`로 이어 붙인다(`UriBuilder`). `packageType` 필터는 `SearchQueryService/3.5.0`만 지원하고, 다른 버전은 무시할 수 있다(Tool 설명). `prerelease` 기본값은 `dotnet` CLI 검색과 같은 false다.
- `nuget_list_versions`: flat container는 **unlisted 버전도 포함**한다. listed 여부는 `nuget_get_package_metadata`로 확인한다(Tool 설명). 결과는 `NuGetVersion` 오름차순으로 정렬하고, 파싱하지 못한 버전 문자열은 원문 그대로 끝에 둔다. `prerelease` 기본값은 true(전체 목록)이고 false면 prerelease 버전을 뺀다. 패키지가 없으면(404) `McpException`: `NuGet '{name}' package '{id}' was not found.`
- `nuget_get_package_metadata`: registration page에 `items`가 없으면 page `@id`를 GET한다. `version`이 없으면 모든 page를 읽고 버전 요약 목록만, 있으면 그 버전의 상세만 돌려준다. 버전이 없으면 `... package '{id}' version '{version}' was not found.`
- `nuget_check_vulnerabilities`: registration의 `vulnerabilities`만 근거로 한다. `VulnerabilityInfo/6.7.0` 리소스(전체 취약점 DB 다운로드 + 버전 범위 평가)는 쓰지 않는다(결정 4).
- `vulnerabilities`·`deprecation`은 nuget.org 형태 피드만 주고, nuget.org도 해당 없는 버전에서는 필드를 **빼고** 준다. 그래서 "정보 제공 피드"인지를 응답 필드가 아니라 **피드 단위**로 판정한다: 캐시된 service index에 `VulnerabilityInfo/6.7.0` 리소스가 **있으면** 정보 제공 피드다(리소스를 호출하지는 않으므로 결정 4와 충돌하지 않는다).
  - 정보 제공 피드: 필드가 없으면 `vulnerable`·`deprecated` = false, `vulnerabilities` = 빈 배열
  - 그 밖의 피드: 필드가 있으면 그 값을 쓰고, 없으면 `vulnerable`·`deprecated`·`vulnerabilities` = **null**(정보 없음, 사용자 결정) Tool 설명·README에 "사설 피드는 취약점·deprecation 정보를 주지 않을 수 있다"를 적는다.
- `RegistrationsBaseUrl/3.6.0`이 없는 피드는 SemVer 2.0.0 버전이 빠질 수 있다(Tool 설명).
- `version`을 지정해도 모든 registration page를 읽는다(page `lower`·`upper` 최적화는 하지 않음).
- `nuget_push_package`
  - `path`와 `contentBase64` 중 정확히 하나. 둘 다 있거나 둘 다 없으면 요청 전 오류
  - `path`: MCP 서버 호스트의 절대 경로, 확장자 `.nupkg`(대소문자 무시, `.snupkg` 거부), 파일 존재. UNC·장치 경로는 파일에 접근하기 전에 거부한다(SMB 인증으로 NTLM 해시 유출 방지, 사용자 결정). 거부 목록 대신 **허용 형태만** 받는다.
    - Windows: `^[A-Za-z]:[\\/]` 드라이브 경로만 허용한다(`\\host`, `//host`, `/\host`, `\/host`, `\\?\`, `\\.\`, `\??\`는 모두 탈락).
    - 그 밖의 OS: `/`로 시작하고 `//`로 시작하지 않는 경로만 허용한다.
    - `Path.GetFullPath` 결과에도 같은 검사를 한 번 더 한다. `GetFullPath` 예외는 같은 `McpException`이다.
    - 네트워크 맵 드라이브(`Z:\`)와 UNC를 가리키는 심볼릭 링크·junction을 통한 원격 접근은 막지 않는다(남은 위험, README에 명시).
  - `.nuspec` 루트 판정은 `/`와 `\` 구분자를 모두 본다
  - `contentBase64`: Base64 디코딩 실패면 요청 전 오류
  - 올리기 전에 zip을 열어 루트의 `.nuspec`에서 `id`·`version`을 읽는다. zip이 아니거나 `.nuspec`이 없으면 요청 전 오류. 결과에 이 값을 쓴다
  - 본문: `multipart/form-data`, part 이름 `package`, 파일 이름 `package.nupkg`, `application/octet-stream`
  - 성공: 200·201·202·204(`SendPublishAsync`의 성공 코드와 같음). 409(이미 있음)는 `skipDuplicate=true`면 성공(`skipped: true`), 아니면 실패
- `nuget_unlist_package`: Tool 설명·README에 "nuget.org는 unlist(복구 가능)이지만 일부 사설 피드는 실제 삭제일 수 있다"를 적는다.
- 쓰기 Tool 3개는 인스턴스에 `ApiKey`가 없으면 service index를 읽기 전에 `McpException`: `NuGet '{name}' has no ApiKey; write tools are unavailable.`

## 결과 (성공, JSON 문자열)

- `nuget_list_instances`: `[{"name","description","serviceIndex","canWrite"}]` (`canWrite` = ApiKey 유무). 자격 증명은 넣지 않는다
- `nuget_search_packages`: `{"totalHits":n,"packages":[{"id","version","description","totalDownloads","verified","versionCount"}]}` (`description`은 앞 300자)
- `nuget_list_versions`: `{"id","versions":[...]}`
- `nuget_get_package_metadata`
  - `version` 없음: `{"id","versions":[{"version","listed","published","deprecated","vulnerable"}]}`
  - `version` 있음: `{"id","version","listed","published","projectUrl","dependencyGroups":[{"targetFramework","dependencies":[{"id","range"}]}],"deprecation":{"reasons","message","alternatePackage":{"id","range"}}|null,"vulnerabilities":[{"severity","advisoryUrl"}]}`
  - `listed`가 없으면 true로 본다
  - `published`가 1900년이면 null로 바꾼다(nuget.org는 unlisted 버전을 1900년으로 둔다, 사용자 결정)
  - `vulnerabilities[].severity`는 피드 값 `"0"`~`"3"`을 `low`·`moderate`·`high`·`critical`로 바꾸고, 그 밖의 값은 원문 그대로, 없으면 null로 둔다
- `nuget_check_vulnerabilities`의 `vulnerable`·`deprecated`·`vulnerabilities`와 메타데이터 요약의 `vulnerable`·`deprecated`는 "세부 규칙"의 null 규칙을 따른다
- `nuget_check_vulnerabilities`: `{"id","version","vulnerable":bool|null,"vulnerabilities":[...]|null,"deprecated":bool|null}`
- `nuget_push_package`: `{"pushed":{"id","version"},"skipped":bool}`
- `nuget_unlist_package`: `{"unlisted":{"id","version"}}`
- `nuget_relist_package`: `{"relisted":{"id","version"}}`

## 인증·전송

- `User`/`Password`가 있으면 `Authorization: Basic base64(User:Password)`를 **요청 URL의 scheme·host·port가 `ServiceIndex`와 같을 때만** 붙인다. nuget.org처럼 리소스가 다른 호스트에 있으면 그 호스트에는 보내지 않는다(자격 증명 유출 방지).
- `ApiKey`는 `X-NuGet-ApiKey` 헤더로 **PackagePublish 요청에만** 붙인다(NuGet client와 같은 동작). nuget.org는 publish 호스트가 index 호스트와 달라 origin 제한을 두지 않는다. 그래서 service index를 신뢰한다는 전제다(README에 명시). 단, `ServiceIndex`가 https인데 publish `@id`가 http면 요청 전에 `McpException`: `NuGet '{name}' publish resource is not https; ApiKey would be sent in clear text.` (scheme 하향 방지. `ServiceIndex` 자체가 http인 경우는 결정 10대로 허용) 같은 origin이면 publish 요청에 Basic도 함께 붙는다(Azure Artifacts 형태).
- `User-Agent: Biz.Bizadm.MCP.ToolKit/{버전}`, `X-NuGet-Protocol-Version: 4.1.0`(NuGet client와 같은 헤더. nuget.org가 이 헤더로 응답 형태를 정할 수 있다).
- 요청 HTTP 버전은 1.1로 고정한다(`HttpClient.DefaultRequestVersion`). push 오류 사유를 ReasonPhrase로 읽는데 HTTP/2에는 ReasonPhrase가 없기 때문이다.
- 응답 본문은 **50MB 상한**으로 읽는다(사용자 결정). 넘으면 `NuGet '{instance}' {METHOD} {url} response exceeded {n} bytes.` (`{n}`은 실제 상한, 기본 52428800)
- 헤더 설정을 포함한 요청 생성은 전송 오류 처리 `try` 안에서 한다(헤더 형식 예외도 `McpException`, 값은 메시지에 넣지 않음).
- 자동 리다이렉트는 끈다. `X-NuGet-ApiKey`는 .NET이 리다이렉트 시 지우지 않는 사용자 헤더라 다른 호스트로 넘어갈 수 있기 때문이다. 3xx는 실패이며 메시지에 `Location`을 넣는다.
- `SocketsHttpHandler { PooledConnectionLifetime = 2분, AllowAutoRedirect = false, AutomaticDecompression = All }`.
- 시간 제한: `HttpClient.Timeout`은 무한으로 두고 요청마다 연결한 CTS로 건다. 읽기 100초, push 10분.

## 오류

- 실패는 `McpException`(→ `isError`): `NuGet '{instance}' {METHOD} {url} failed (HTTP {code}): {요약}`. `{url}`은 사용자 정보와 query를 뺀 절대 URL(`GetComponents(Scheme | Host | Port | Path)`).
- 404를 Tool이 의미 있는 문구로 바꾸는 경우는 패키지·버전·registration page 없음뿐이다. service index와 검색의 404는 위 일반 실패 형식이다.
- 요약: 본문이 있으면 공백을 줄인 앞 200자, 없으면 `ReasonPhrase`(nuget.org push는 오류 사유를 ReasonPhrase로 준다).
- 3xx 요약의 `Location`도 query를 뺀다(서명 query 유출 방지).
- 401·403이면 요약 뒤에 안내를 붙인다. 쓰기 요청: `(check ApiKey and its package scope; Basic is sent only to the ServiceIndex origin)`, 읽기 요청: `(check User/Password; Basic is sent only to the ServiceIndex origin)`.
- 전송 오류(호출자 취소 제외): `NuGet '{instance}' {METHOD} {url} failed: {예외 메시지}`.
- 내부 시간 초과: `NuGet '{instance}' {METHOD} {url} timed out after {n}s.`
- 호출자 취소는 감싸지 않고 `OperationCanceledException`으로 전파한다.
- JSON 파싱 실패: `NuGet '{instance}' {url} returned invalid JSON: {메시지}`.
- JSON 구조 오류(필수 필드 없음·형식 다름, 상대 `@id`): `NuGet '{instance}' {url} returned unexpected JSON: {설명}`.
- 사용자 인자 버전 파싱 실패는 요청 전 `version '{version}' is not a valid NuGet version.` 피드가 준 버전을 파싱하지 못하면 원문을 그대로 쓰고 비교에서는 제외한다. Tool 밖으로 `McpException`이 아닌 예외를 내보내지 않는다(호출자 취소 제외).
- 미등록 인스턴스: `Unknown NuGet instance '{name}'. Registered instances: ...` (GitHub·Jenkins와 같은 형식).
- ApiKey·Password·Authorization 값은 메시지·결과에 넣지 않는다. 재시도하지 않는다.

## 구조

```
src/Biz.Bizadm.MCP.ToolKit/
  NuGet/NuGetInstance.cs, NuGetOptions.cs        (public)
  NuGet/NuGetFeedClient.cs                       (internal sealed, IDisposable, DI singleton)
  NuGet/NuGetServiceIndex.cs                     (internal, resources 파싱·타입 선택)
  NuGet/NuGetPackageFile.cs                      (internal static, nupkg → id·version)
  NuGetMcpServerBuilderExtensions.cs
  Tools/NuGet/NuGetTools.cs
```

- `NuGetFeedClient`
  - 생성 인자: 검증한 `NuGetOptions`, 선택 `HttpMessageHandler`(테스트 주입)
  - `Instances` → 등록 순서의 인스턴스 목록
  - `Resolve(name)` → `NuGetInstance`
  - `GetResourceAsync(instance, purpose, ct)` → 리소스 `Uri` (service index 캐시 사용)
  - `GetJsonAsync(instance, uri, ct, notFoundAsNull = true)` → `JsonNode?`. 404는 `null`로 돌려 Tool이 메시지를 정한다(service index·검색은 `notFoundAsNull: false`로 일반 실패). `JsonDocument` 대신 `JsonNode`로 받아 dispose 수명 문제를 없앤다
  - `SendPublishAsync(instance, method, uri, content?, ct)` → `(int Status, string Body, string? Reason)`(409 실패 메시지에 서버 사유를 넣기 위해); 200·201·202·204·409를 돌려주고 그 밖은 실패. 409 처리는 Tool이 정한다(push는 `skipDuplicate`, unlist·relist는 `Failure`로 실패)
  - `Failure(...)`, `Unexpected(instance, uri, 설명)`: 오류 절의 `McpException` 생성 도우미(internal)
  - `NuGetServiceIndex.TypesOf(resource)`, `NameOf(resource)`: 오류 문구용 타입 목록과 용도 이름(internal)
  - URI 결합은 공통 도우미 `Child(baseUri, relative)` 하나로 한다(끝 `/` 보정)
- `NuGetTools`는 `NuGetFeedClient`만 쓴다. `McpRelayPool`과 무관하다.
- 패키지 의존성 추가: `NuGet.Versioning`(Directory.Packages.props, 규칙 `dotnet/nuget-package-selection.md`로 버전 선택).

## 테스트

- 등록: Tool 8개와 주석, 검증 실패 케이스(인스턴스 없음, 중복 이름, 상대 URI, user@ URI, User만 있음)
- service index: 타입 우선순위, 같은 타입 첫 항목, 리소스 없음 오류(요청 없음), 캐시(두 번째 호출에 index GET 없음), 실패 후 재시도
- 요청 계약(가짜 `HttpMessageHandler`): Tool마다 메서드·URL·query·본문·헤더. id 소문자, 버전 정규화(`1.0` → `1.0.0`, build metadata 제거)
- 인증: 같은 호스트에만 Basic, 다른 호스트엔 없음, ApiKey는 publish 요청에만, ApiKey 없으면 쓰기 Tool 요청 없이 실패
- registration: inline page, 외부 page GET, `listed` 없음 → true, 버전 없음 오류
- push: path·Base64 각각, 둘 다/둘 다 없음 거부, `.snupkg` 거부, 잘못된 Base64, zip 아님, nuspec 없음, multipart part 이름, 201·202 성공, 409 + skipDuplicate
- 오류: 404 메시지, 401·403 안내, 3xx + Location(query 제거), ReasonPhrase 요약(push 400 포함), 전송 오류, 시간 초과 문구, invalid JSON, unexpected JSON(`resources` 없음, 필수 필드 없음, 상대 `@id`), 메시지·결과에 ApiKey·Password 없음
- 인증 추가: ApiKey가 읽기 요청(service index, 검색, registration)에 붙지 않음, 같은 origin publish에 Basic + ApiKey
- 등록 추가: `User`에 `:`, ApiKey·Password 제어 문자 거부(메시지에 값 없음), `ServiceIndex` 속성 이름 문구, 기존 `NameAndEndpoint` 호출부 문구 불변
- 취소: 호출자 취소가 `OperationCanceledException`으로 전파, 공유 index 로드 중 한 호출자 취소 후에도 캐시 유지, 동시 두 호출에 index GET 1회
- 응답 변형: 선택 필드가 빠진 사설 피드 형태(검색·registration), 검색 `@id`에 query 있음·끝 `/` 없음, 버전 정렬과 파싱 불가 버전, `severity` 매핑, `published` 1900 → null, 버전 미발견 문구
- push 추가: UNC·장치 경로 거부(파일 접근 없음, `/\host`·`\/host`·`\??\UNC\` 포함), `\` 구분자 nuspec
- 재검토 추가: id 규칙(`..`·`.`·`a/b` 거부, 요청 0건), check 결과 JSON(재부모화 예외 없음), 취약점·deprecation 필드 없는 피드 → null, 비문자열 `versions` 원소, `User-Agent`·`X-NuGet-Protocol-Version` 헤더, `{url}`에서 사용자 정보 제거, 응답 50MB 초과
- 3차 검토 추가: nuget.org 형태(필드 없음 + `VulnerabilityInfo` 리소스 있음) → false, 리소스 없는 피드 → null, 루트 배열·`data` 원소 문자열·page 문자열 → unexpected JSON, https 인스턴스의 http publish 주소 거부(요청 0건), `IsLocalAbsolute` 두 OS 분기(OS 무관), `GetFullPath` 예외(`\0` 포함 경로), BOM 응답, `@type` 배열, query가 있는 `@id`, 409 서버 사유
- 테스트 처리기: 시간 초과·동시 호출·취소 테스트를 위해 비동기·게이팅·스레드 안전 처리기 `NuGetRecordingHandler`를 새로 둔다(기존 `GitHubRecordingHandler`는 동기 큐라 불가). multipart `name` 단언은 따옴표 유무에 관대하게

## 결정

1. 전용 Tool 8개, 접두사 `nuget_`, `NuGetTools` 한 클래스, `WithNuGetTools`로 등록
2. 리소스 주소는 service index에서 `@type` 우선순위로 고르고 인스턴스별 캐시(실패는 캐시 안 함)
3. Basic은 `ServiceIndex`와 같은 origin에만, ApiKey는 PackagePublish에만. 자동 리다이렉트 끔
4. 취약점은 registration `vulnerabilities`만 사용. `VulnerabilityInfo` 리소스는 쓰지 않는다(전체 DB 다운로드와 버전 범위 평가가 필요해 범위 대비 비용이 큼. 방안 문서에 제외 표시)
5. 버전 파싱·정규화는 `NuGet.Versioning` 사용(직접 구현하지 않음)
6. push는 `.nuspec`을 미리 읽어 id·version을 결과에 넣고, 잘못된 패키지는 요청 전에 거부
7. push `path`는 MCP 서버 호스트 파일을 읽는다. 허용 루트 제한은 두지 않는다(확장자·zip·nuspec 검사만). README에 명시
8. 오류는 `McpException`(→ `isError`), 재시도 없음, 자격 증명 비노출
9. README에 8개 Tool과 설정 예시를 추가한다. csproj `PackageTags`에 `nuget`을 추가한다. 버전 올림은 이 작업에 포함하지 않는다
10. (독립 검토 반영, 사용자 결정) http ServiceIndex는 자격 증명이 있어도 허용하고 README에 평문 전송 위험만 적는다
11. (독립 검토 반영, 사용자 결정) push `path`의 UNC·장치 경로는 거부한다
12. (독립 검토 반영, 사용자 결정) `NameAndEndpoint`에 선택 인자 `property`를 추가해 재사용한다
13. (독립 검토 반영, 사용자 결정) `published` 1900년은 null, `severity`는 이름으로 매핑
14. (독립 검토 반영) 선택 필드는 없으면 null, 필수 필드 없음은 unexpected JSON. Tool 밖으로 `McpException` 외 예외를 내보내지 않는다. `JsonNode`로 받아 dispose 수명 문제를 없앤다
15. (독립 검토 반영) `-beta` 타입 별칭 추가, flat container의 unlisted 포함과 `packageType` 3.5.0 한정을 Tool 설명에 명시, 버전 목록은 `NuGetVersion` 정렬
16. (독립 검토 반영) 시간 초과 문구 분리, Location query 제거, 401·403 안내에 Basic origin 제한 명시, ApiKey·User·Password 제어 문자와 User `:` 거부. README에 ServiceIndex에 비밀을 넣지 말 것, service index 캐시(재시작 필요), publish ApiKey는 service index를 신뢰한다는 전제를 적는다
17. (재검토 반영) push `path`는 허용 형태(드라이브 경로)만 받는다. 패키지 id는 NuGet ID 규칙으로 검사한다
18. (재검토 반영, 사용자 결정) 취약점·deprecation 필드가 없는 피드는 null(정보 없음)로 구분한다
19. (재검토 반영, 사용자 결정 포함) 응답 50MB 상한. HTTP/1.1 고정, `X-NuGet-Protocol-Version: 4.1.0`, 오류 URL에서 사용자 정보 제거, 결과 노드는 새로 만든다, 비동기 테스트 처리기 추가
20. (3차 검토 반영) 취약점 정보 제공 여부는 피드 단위(`VulnerabilityInfo/6.7.0` 리소스 존재)로 판정한다. 결정 18의 null 규칙은 정보를 주지 않는 피드에만 적용한다
21. (3차 검토 반영) https 인스턴스의 http publish 주소 거부, 객체가 아닌 JSON 노드·BOM·`@type` 배열 처리, query가 있는 `@id`의 경로 결합 거부, 409 서버 사유 보존, 맵 드라이브·링크는 README에 남은 위험으로 명시
