---
status: done
category: redmine
feature: redmine-api-key
phase: 설계
---

# redmine-api-key 설계

선행: `0007-redmine-api-key-방안.md`

## 구조

```
get_current_user / get_user / get_my_account
  └─ RedmineClient.GetAsync(path)            // 변경 없음
       └─ RedmineJson.RemoveUserApiKey(json) // 추가
            └─ Tool 반환
```

- 제거 로직은 기존 `internal static class RedmineJson`(`src/Biz.Bizadm.MCP.ToolKit/Redmine/RedmineJson.cs`)에 메서드 하나로 추가한다. 새 파일·타입은 만들지 않는다.
- `RedmineClient`는 바꾸지 않는다. 다른 Tool의 응답은 지금처럼 원문 그대로다.

## 인터페이스

```csharp
/// 응답의 user.api_key를 지운다. 지울 것이 없으면 json을 그대로 반환한다.
internal static string RemoveUserApiKey(string json);
```

| 대상 | 변경 |
| --- | --- |
| `UserTools.GetCurrentUser` | `GetAsync` 결과에 `RemoveUserApiKey` 적용 (async로 변경) |
| `UserTools.GetUser` | 같음 |
| `PrincipalTools.GetMyAccount` | 같음 |

Tool 이름·인수·`Description`·반환 타입(`Task<string>`)은 바꾸지 않는다.

## 데이터

- 입력: Redmine 응답 문자열 `{"user":{...,"api_key":"..."}}`
- 처리: `JsonNode.Parse` → 루트가 객체이고 `user`가 객체이며 `api_key` 속성이 있을 때만 `Remove("api_key")`
- 출력: 지웠으면 `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`으로 재직렬화해 한글 등 비ASCII 문자를 원문 그대로 둔다. 지울 것이 없으면 입력 문자열을 그대로 반환한다.

## 의존성

- `System.Text.Json.Nodes`, `System.Text.Encodings.Web` (BCL). 새 패키지 없음.

## 결정

| # | 결정 | 이유 |
| --- | --- | --- |
| 1 | `RedmineJson` helper에 둔다 | 영향 범위가 세 Tool로 한정되고 기존 타입을 재사용한다 |
| 2 | 재직렬화 시 `UnsafeRelaxedJsonEscaping` | 기본 인코더는 한글을 `\uXXXX`로 바꿔 지금 응답과 모양이 달라진다 |
| 3 | 키가 없으면 원문 반환 | 다른 사용자 조회·구버전 서버 응답을 바꾸지 않는다 |
| 4 | 버전 무관 적용 | V2_2 설정으로 최신 서버에 붙어도 키가 나온다 |
