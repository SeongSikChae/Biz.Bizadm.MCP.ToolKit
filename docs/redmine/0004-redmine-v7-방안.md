---
status: done
category: redmine
feature: redmine-v7
phase: 방안
---

# redmine-v7 방안

선행: `0001-redmine-tools-방안.md` ~ `0003-redmine-tools-개발.md` (Redmine 2.2 Tool 27개)

## 문제

ToolKit의 Redmine Tool 27개는 Redmine 2.2 REST API 기준이다. 그래서 2.2에 없는 기능을 쓸 수 없다. 예를 들어 watchers가 없고, 관계 `copied_to`를 거부하며, time entry를 `user_id`로 거를 수 없고, 댓글을 수정할 수 없다.

사내 Redmine `techlab.cloudedi.net`은 7.0.3이다. 로그인 페이지의 JavaScript를 공식 릴리스 원본과 SHA1로 비교해 확인했다. 이 서버의 REST 기능을 MCP Tool로 쓸 수 없다.

## 목표

- `RedmineOptions`에 서버 버전 옵션을 추가한다.
  - 값은 `V2_2`(기본)와 `V7_0`이다.
  - 기본값이 2.2이므로 기존 호스트는 코드를 바꾸지 않아도 동작이 같다.
- `V7_0`이면 아래 기능을 추가로 등록하고, 2.2 제약을 푼다.
- 엔드포인트는 Redmine 7.0.3 `config/routes.rb`로 확인했다.

### 1. 일감·작업 시간 보강

| 기능 | 엔드포인트 |
| --- | --- |
| 관찰자 추가 | `POST /issues/:id/watchers.json` `{user_id}` |
| 관찰자 삭제 | `DELETE /issues/:id/watchers/:user_id.json` |
| 일감 삭제 | `DELETE /issues/:id.json` |
| 일감 조회 include | `watchers`, `allowed_statuses` 허용 |
| 일감 목록 필터 | `sort`, `parent_id`, `subproject_id`, `created_on`, `updated_on`, `query_id`, `issue_id` 등 |
| 일감 생성·수정 필드 | `is_private`, `custom_fields`, `private_notes`, `category_id`, `fixed_version_id`, `watcher_user_ids`(생성) |
| 관계 | `copied_to`, `copied_from` 허용 |
| 작업 시간 | 목록 `user_id` 필터, 삭제 `DELETE /time_entries/:id.json` |

### 2. 댓글 수정

- 엔드포인트는 `PUT /journals/:id.json`이고 Body는 `{journal: {notes, private_notes}}`다.
- 텍스트 가이드의 "댓글 수정 REST 없음" 안내도 버전에 맞게 고친다.

### 3. 프로젝트 구조

| 대상 | 조회 | 생성 | 수정·삭제 |
| --- | --- | --- | --- |
| 버전 | `GET /projects/:id/versions.json`, `GET /versions/:id.json` | `POST /projects/:id/versions.json` | `PUT`·`DELETE /versions/:id.json` |
| 일감 범주 | `GET /projects/:id/issue_categories.json`, `GET /issue_categories/:id.json` | `POST /projects/:id/issue_categories.json` | `PUT`·`DELETE /issue_categories/:id.json` |
| 구성원 | `GET /projects/:id/memberships.json`, `GET /memberships/:id.json` | `POST /projects/:id/memberships.json` | `PUT`·`DELETE /memberships/:id.json` |

### 4. 조회 API 추가

- 검색 `GET /search.json`(프로젝트 범위 포함)
- 저장된 질의 `GET /queries.json`
- 사용자 정의 필드 `GET /custom_fields.json`(관리자)
- 역할 `GET /roles.json`, `GET /roles/:id.json`
- 그룹 `GET /groups.json`, `GET /groups/:id.json`(관리자)
- 프로젝트 파일 `GET /projects/:id/files.json`
- 뉴스 `GET /news.json`, `GET /projects/:id/news.json`
- 내 계정 `GET /my/account.json`
- 위키
  - 버전별 조회 `GET /projects/:id/wiki/:title/:version.json`
  - 삭제 `DELETE /projects/:id/wiki/:title.json`
- 첨부
  - 메타 수정 `PATCH /attachments/:id.json`
  - 삭제 `DELETE /attachments/:id.json`

## 대안

| 안 | 내용 | 판단 |
| --- | --- | --- |
| A | `RedmineOptions`에 버전 옵션을 두고 7.0일 때만 새 Tool 등록·제약 해제 | 채택: 사용자 결정. 기존 2.2 사용처 무변경 |
| B | 2.2 제약·설명을 걷어내고 7.0.3 전용으로 교체 | 기각: 2.2 서버 호환을 잃는다 |
| C | `WithRedmineTools`(2.2)는 두고 `WithRedmine7Tools`를 새로 만든다 | 기각: 등록·코드 중복이 늘어난다 |
| D | 4개 영역(일감 보강, 댓글 수정, 프로젝트 구조, 조회 API) 전부 | 채택: 사용자 결정 |

## 채택 방안

- `RedmineOptions.Version`(기본 `V2_2`)을 추가한다. `WithRedmineTools`가 이 값으로 등록할 Tool 세트를 고른다.
- 기존 원칙은 그대로 따른다.
  - 결과는 Redmine 응답 JSON 문자열 원문을 반환한다.
  - Tool 클래스는 `RedmineClient`를 생성자 DI로 받는다.
  - 테스트는 HttpMessageHandler 기록 방식을 쓴다.
- 7.0에서 Description·매개변수가 달라지는 기존 Tool(일감, 관계, 작업 시간, 위키, 텍스트 가이드)을 어떻게 표현할지는 설계에서 정한다. Description은 컴파일 시점 attribute 상수라서 정해야 하는 문제다.

## 비범위

- Biz.Bizadm.MCP.Server 연동(등록 코드, YAML 설정)
- 실제 Redmine 서버 대상 쓰기 검증(조회만 선택적으로 실행)
- 관리자 전용 쓰기
  - 사용자·프로젝트 생성/수정/삭제/보관
  - 그룹 생성·구성원 변경
  - 역할 권한 변경
  - 사용자 정의 필드 정의 변경
- 뉴스 작성·수정·삭제, 프로젝트 파일 업로드, 내 계정 수정
- 2.2 Tool 27개의 이름·매개변수·Description·요청 계약 변경(`V2_2`일 때)
