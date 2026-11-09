---
status: done
category: github
feature: github-release-tools
phase: 설계
---

# github-release-tools 설계

선행: `0008-github-release-tools-방안.md`, `0006-github-ref-tools-설계.md`(`GitHubRestClient`·오류 형식·경로 세그먼트 규칙)

## 공개 API

| 타입 | 변경 |
| --- | --- |
| `Tools.GitHub.GitHubReleaseTools` | **추가** (`[McpServerToolType]`), 아래 5개 Tool |
| `GitHubMcpServerBuilderExtensions.WithGitHubTools` | `GitHubReleaseTools`도 등록. GitHub Tool은 7개 → 12개 |

설정 항목은 추가하지 않는다(`ApiEndpoint`·`Token` 재사용). 게이트웨이 3개와 태그·ref 4개는 바꾸지 않는다.

## 노출 Tool (`GitHubReleaseTools`)

공통 인자: `instance`, `owner`, `repo`(필수). 릴리스 지정은 `releaseId`(정수) **또는** `tag` 중 정확히 하나. 둘 다 없거나 둘 다 주면 요청 전 `McpException`.

| Tool | 인자 | REST | 주석 |
| --- | --- | --- | --- |
| `github_create_release` | `tagName`(필수), `targetCommitish`, `name`, `body`, `draft`(false), `prerelease`(false), `generateReleaseNotes`(false) | `POST repos/{o}/{r}/releases` | 쓰기, 비파괴, 비멱등, 외부 |
| `github_update_release` | 릴리스 지정, `tagName`, `targetCommitish`, `name`, `body`, `draft`, `prerelease`(모두 선택, 준 값만 보냄) | `PATCH repos/{o}/{r}/releases/{id}` | 쓰기, **파괴적**, 멱등, 외부 |
| `github_delete_release` | 릴리스 지정 | `DELETE repos/{o}/{r}/releases/{id}` | 쓰기, **파괴적**, 멱등, 외부 |
| `github_upload_release_asset` | 릴리스 지정, `name`(필수), `contentBase64`(필수), `contentType`(기본 `application/octet-stream`), `label` | 릴리스 조회로 `upload_url` → `POST {upload_url}?name=&label=` | 쓰기, 비파괴, 비멱등, 외부 |
| `github_delete_release_asset` | `assetId`(정수, 필수) | `DELETE repos/{o}/{r}/releases/assets/{id}` | 쓰기, **파괴적**, 멱등, 외부 |

- `tag`로 지정하면 `GET repos/{o}/{r}/releases/tags/{tag}`로 `id`를 찾는다. GitHub는 이 API로 **초안(draft) 릴리스를 찾지 않으므로** 초안은 `releaseId`로 지정한다(Tool 설명에 적는다).
- `github_update_release`에서 바꿀 값을 하나도 주지 않으면 요청 전 `McpException`.
- 업로드는 `releaseId`로 지정해도 `GET releases/{id}`를 한 번 호출해 `upload_url`을 얻는다. `tag`로 지정하면 `releases/tags/{tag}` 응답의 `upload_url`을 쓴다(추가 조회 없음).

## asset 업로드

- `contentBase64`: 표준 base64(`Convert.FromBase64String`). 공백·줄바꿈은 허용, 그 밖에 형식 오류면 `McpException("contentBase64 is not valid base64.")`.
- 크기 상한: 디코딩 후 **10 MiB**(10,485,760 B). 디코딩 전에 base64 길이로 먼저 상한을 넘는지 확인해 큰 입력을 디코딩하지 않는다. 0바이트는 오류.
- `upload_url`은 `https://uploads.github.com/repos/o/r/releases/1/assets{?name,label}` 같은 URI 템플릿이다. `{`부터 끝까지 떼고 `?name={이름}`(+ `&label={라벨}`)을 URL 인코딩해 붙인다.
- **호스트 검사(엄격)**: 업로드 URI의 scheme·port가 `ApiEndpoint`와 같고, host가 `ApiEndpoint` host와 같아야 한다. 예외는 `ApiEndpoint` host가 `api.github.com`일 때 `uploads.github.com` 하나. 맞지 않으면 요청 전 `McpException`(`GitHub '{instance}' upload_url host '{host}' does not match ApiEndpoint`). `upload_url`이 없거나 절대 URI가 아니어도 오류.
- 본문은 `ByteArrayContent`, `Content-Type`은 `contentType`.

## 결과·오류

- 성공: GitHub 응답 JSON 그대로(생성·수정·업로드). 삭제는 본문이 없으므로 `{"deleted":{"releaseId":N}}`, `{"deleted":{"assetId":N}}`.
- 오류 형식·Token 비노출·재시도 없음은 `GitHubRestClient`와 같다. 경로 세그먼트(`owner`, `repo`, `tag`) 규칙(빈·`.`·`..` 거부)도 같다. `releaseId`·`assetId`는 양의 정수만 허용한다.

## 구조

- `GitHubRestClient`에 `SendAsync(instance, method, Uri absoluteUri, HttpContent? content, ct)` 오버로드를 둔다. 기존 JSON `SendAsync(… path, JsonObject? …)`는 이것을 호출한다. 오류 메시지의 `{path}`는 `ApiEndpoint` 기준 상대 경로, 업로드는 업로드 URI의 경로다.
- 업로드 호스트 검사는 `GitHubRestClient.ResolveUploadUri(instance, uploadUrlTemplate, name, label)`(internal static에 가까운 순수 함수)로 분리해 단위 테스트한다.
- 릴리스 id 조회(`releaseId` 또는 `tag` → id, `upload_url`)는 `GitHubReleaseTools` 안의 private 함수.

## 테스트

- 등록: GitHub Tool 12개, 주석, 입력 스키마 인자 이름
- 요청 계약(기록 핸들러): Tool별 메서드·URL·본문, update는 준 필드만, tag 지정 시 조회 후 id 사용
- 업로드: 템플릿 처리·name/label 인코딩, `Content-Type`·바이트 본문, github.com→uploads 허용, 다른 호스트·scheme·port 거부(요청 없음), Enterprise 같은 호스트 허용, 10 MiB 초과·0바이트·잘못된 base64 거부
- 입력 검사: `releaseId`·`tag` 둘 다/둘 다 없음, 음수 id, update 빈 변경
- 실제 확인: 초안 릴리스 생성 → asset 업로드 → asset 삭제 → 수정 → 릴리스 삭제 → 생긴 태그를 `github_delete_ref`로 삭제, 잔여 없음

## 결정

1. 전용 Tool 5개, `GitHubReleaseTools` 별도 클래스, `WithGitHubTools`에 항상 포함
2. 릴리스 지정은 `releaseId` 또는 `tag`(정확히 하나), 초안은 `releaseId`로만
3. 업로드 주소는 릴리스 응답 `upload_url`, 호스트는 엄격 검사(같은 host, github.com은 uploads 예외)
4. asset은 base64, 디코딩 후 10 MiB 상한
5. 결과는 GitHub 응답 그대로, 삭제는 `{"deleted":{...}}`
6. ReadMe에 5개 Tool 추가, 이후 버전 1.4.0
7. (독립 검토 반영) `tag`의 `/`는 구분자로 두고 세그먼트마다 인코딩한다(`release/1.0` → `releases/tags/release/1.0`). 빈·`.`·`..` 세그먼트는 거부한다. 슬래시 tag 실제 호출은 공개 릴리스가 필요해 확인하지 않았다
8. (독립 검토 반영) `upload_url`이 문자열이 아니면 "no valid upload_url" 오류로 알린다
