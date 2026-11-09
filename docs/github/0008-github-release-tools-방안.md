---
status: done
category: github
feature: github-release-tools
phase: 방안
---

# github-release-tools 방안

GitHub 이슈: [#2 GitHub 릴리스 쓰기 Tool 추가](https://github.com/SeongSikChae/Biz.Bizadm.MCP.ToolKit/issues/2). 태그·ref(#1, `github-ref-tools`)와 함께 1.4.0으로 낸다.

## 문제

- GitHub MCP Server는 릴리스를 만들거나 바꾸는 Tool을 노출하지 않는다. 기본 `/mcp/`(46개)와 전체 `/mcp/x/all`(113개) 모두 `list_releases`, `get_latest_release`, `get_release_by_tag` 조회뿐이다.
- #1에서 `ApiEndpoint`와 `GitHubRestClient`로 REST 직접 호출 기반을 만들었다.

## 목표

등록한 GitHub 인스턴스의 Token으로 릴리스를 만들고, 고치고, 지우며 asset을 올리고 지운다.

## 대안

asset 업로드 주소:

| 대안 | 판단 |
| --- | --- |
| 릴리스 응답의 `upload_url` 사용 | **채택**. GitHub가 주는 주소를 그대로 쓰므로 설정 추가가 없고 Enterprise도 같은 방식이다. 업로드 전에 릴리스를 한 번 조회한다 |
| 필수 `UploadEndpoint` 설정 | 설정 항목이 늘고 호환성이 또 깨진다 |

asset 입력:

| 대안 | 판단 |
| --- | --- |
| base64 본문 + 크기 제한 | **채택**. MCP 서버의 로컬 파일을 읽지 않는다. 큰 파일은 올릴 수 없다(상한은 설계에서 정한다) |
| 서버 로컬 경로 | 에이전트가 서버의 임의 파일을 GitHub로 보낼 수 있어 허용 폴더 제한이 필요하다 |

## 채택 방안

- 노출·인스턴스 선택·인증·오류 형식·Token 비노출은 #1과 같다(전용 Tool, 필수 `instance`, `GitHubRestClient` 재사용).
- 범위(Tool 이름·인자는 설계에서 확정):

| 기능 | REST API |
| --- | --- |
| 릴리스 생성 | `POST /repos/{owner}/{repo}/releases` (`tag_name`, `target_commitish`, `name`, `body`, `draft`, `prerelease`, `generate_release_notes`) |
| 릴리스 수정 | `PATCH /repos/{owner}/{repo}/releases/{release_id}` |
| 릴리스 삭제 | `DELETE /repos/{owner}/{repo}/releases/{release_id}` (태그는 남는다) |
| asset 업로드 | `GET releases/{release_id}`로 `upload_url`을 얻어 `POST {upload_url}?name=...` (바이너리 본문) |
| asset 삭제 | `DELETE /repos/{owner}/{repo}/releases/assets/{asset_id}` |

- `tag_name`이 없는 태그면 GitHub가 `target_commitish` 기준으로 태그를 만든다. 릴리스를 지워도 태그는 남으며, 태그 삭제는 `github_delete_ref`를 쓴다.
- 필요 권한: Contents: Read and write.
- **보안**: `upload_url`은 GitHub 응답에서 오지만 Token을 실어 보내므로, 업로드 호스트가 `ApiEndpoint`와 같은 GitHub인지 검사한다(github.com은 `api.github.com` → `uploads.github.com`, Enterprise는 같은 호스트). 규칙은 설계에서 확정한다.

## 비범위

서버 로컬 파일 업로드, 큰 파일 분할·스트리밍 업로드, asset 다운로드·이름 변경, 릴리스 조회(upstream `list_releases` 등 사용), `UploadEndpoint` 설정, #1 Tool·게이트웨이 변경, MCP.Server 변경.
