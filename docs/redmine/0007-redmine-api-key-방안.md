---
status: done
category: redmine
feature: redmine-api-key
phase: 방안
---

# redmine-api-key 방안

선행: `0001-redmine-tools-방안.md` ~ `0006-redmine-v7-개발.md`

## 문제

사용자 조회 Tool이 Redmine 응답을 그대로 돌려준다. Redmine은 관리자이거나 본인을 조회할 때 응답에 `api_key`를 넣는다. 그래서 MCP 클라이언트(코딩 에이전트)의 대화 기록과 출력에 API 키 원문이 남는다.

2026-11-05 사내 Redmine(`techlab.cloudedi.net`)에서 확인한 결과:

| Tool | 엔드포인트 | 등록 버전 | `api_key` |
| --- | --- | --- | --- |
| `get_current_user` | `GET /users/current.json` | V2_2·V7_0 | 포함 |
| `get_user` (본인 id) | `GET /users/:id.json` | V2_2·V7_0 | 포함 |
| `get_my_account` | `GET /my/account.json` | V7_0 | 포함 |

나머지 Tool(`list_users`, 위키·프로젝트·메타데이터·첨부, V2_2 전용 일감·관계·작업 시간·텍스트 가이드)의 응답에는 사용자 키 필드가 없다.

## 목표

- 위 세 Tool의 응답에서 `user.api_key` 속성을 **제거**한다.
- `RedmineOptions.Version`과 관계없이 적용한다. 2.2 서버가 키를 주는지 확인할 수 없고, V2_2 설정으로 최신 서버에 붙어도 키가 나오기 때문이다.

## 대안

| 대안 | 내용 | 판단 |
| --- | --- | --- |
| 필드 제거 | 응답 JSON에서 `api_key` 속성을 지운다 | **채택**. 키 일부도 남지 않는다 |
| 마스킹 | 앞뒤 몇 글자만 남긴다(예: `2298…6c`) | 미채택. 키 식별은 되지만 일부가 노출된다 |

## 비범위

- `api_key` 노출을 켜고 끄는 설정 옵션
- `api_key`를 주지 않는 Tool(`list_users` 등) 변경
- 일감·댓글·위키 본문에 사용자가 직접 적은 키 문자열 필터링
