---
status: done
category: redmine
feature: redmine-api-key
phase: 개발
---

# redmine-api-key 개발

선행: `0007-redmine-api-key-방안.md`, `0008-redmine-api-key-설계.md`

## 단위 1. `RedmineJson.RemoveUserApiKey`

**대상**: `src/Biz.Bizadm.MCP.ToolKit/Redmine/RedmineJson.cs` `RedmineJson.RemoveUserApiKey`

**코드 초안**

```csharp
private static readonly JsonSerializerOptions ResponseJsonOptions = new()
{
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};

/// <summary>응답의 <c>user.api_key</c>를 지운다. 지울 것이 없으면 <paramref name="json"/>을 그대로 반환한다.</summary>
public static string RemoveUserApiKey(string json)
{
    if (string.IsNullOrWhiteSpace(json))
        return json;

    JsonNode? node;
    try
    {
        node = JsonNode.Parse(json);
    }
    catch (JsonException)
    {
        return json; // 프록시 HTML 등 JSON이 아닌 2xx 응답은 기존처럼 원문 반환
    }

    if (node is not JsonObject root
        || root["user"] is not JsonObject user
        || !user.Remove("api_key"))
        return json;

    return root.ToJsonString(ResponseJsonOptions);
}
```

**검증**: 새 테스트 `tests/Biz.Bizadm.MCP.ToolKit.Tests/Redmine/RedmineJsonApiKeyTests.cs`

- `api_key`가 있으면 사라지고 나머지 필드(`id`, `login`, `firstname` 한글)는 같은 값으로 남는다. 출력에 `\u`가 없다.
- `api_key`가 없는 `user`, `user`가 없는 객체, 루트가 배열인 경우 입력 문자열과 같다(`AreSame` 또는 `AreEqual`).
- 빈 문자열·공백 응답은 예외 없이 입력 그대로다(검토 보완).
- JSON이 아닌 응답(HTML 로그인 페이지 등)은 예외 없이 입력 그대로다(독립 검토 보완).

**한계**: 키를 지워 재직렬화한 응답은 공백·원래 이스케이프 표기가 바뀔 수 있고, 이모지 등 BMP 밖 문자는 `\uXXXX`로 출력된다. 의미는 같다.

**선행 단위**: 없음

## 단위 2. `UserTools` 적용

**대상**: `src/Biz.Bizadm.MCP.ToolKit/Tools/Redmine/UserTools.cs` `GetCurrentUser`, `GetUser`

**코드 초안**

```csharp
public async Task<string> GetCurrentUser(CancellationToken cancellationToken = default)
{
    string json = await _redmine.GetAsync("users/current.json", cancellationToken).ConfigureAwait(false);
    return RedmineJson.RemoveUserApiKey(json);
}

public async Task<string> GetUser([Description("사용자 ID")] int id, CancellationToken cancellationToken = default)
{
    string json = await _redmine.GetAsync($"users/{id.ToString(CultureInfo.InvariantCulture)}.json", cancellationToken).ConfigureAwait(false);
    return RedmineJson.RemoveUserApiKey(json);
}
```

**검증**: 새 테스트 `tests/Biz.Bizadm.MCP.ToolKit.Tests/Redmine/UserToolsApiKeyTests.cs`. `V7RecordingHandler`에 `api_key`가 든 응답을 넣고 두 메서드 결과에 `api_key`가 없음, 요청 경로(`/users/current.json`, `/users/37.json`)가 그대로임을 확인한다. 기존 `Redmine22ContractTests` 통과.

**선행 단위**: 단위 1

## 단위 3. `PrincipalTools.GetMyAccount` 적용

**대상**: `src/Biz.Bizadm.MCP.ToolKit/Tools/Redmine/V7/PrincipalTools.cs` `GetMyAccount`

**코드 초안**

```csharp
public async Task<string> GetMyAccount(CancellationToken cancellationToken = default)
{
    string json = await _redmine.GetAsync("my/account.json", cancellationToken).ConfigureAwait(false);
    return RedmineJson.RemoveUserApiKey(json);
}
```

**검증**: `UserToolsApiKeyTests`에 `GetMyAccount` 케이스 추가(`/my/account.json`, `api_key` 없음). 기존 `LookupToolsTests` 통과.

**선행 단위**: 단위 1

## 최종 검증

- `dotnet build` 종료 코드 0
- `dotnet test` 전체 통과
- 실행 확인: 서버에 배포된 뒤 MCP `get_current_user`, `get_user(37)`, `get_my_account`에 `api_key`가 없는지 확인한다. 배포(`C:\Bizadm.MCP`)는 이 작업 범위가 아니므로 사용자가 배포한 뒤 확인한다.
