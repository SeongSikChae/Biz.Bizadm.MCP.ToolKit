---
status: done
category: github
feature: mcp-param-header
phase: 개발
---

# Mcp-Param 헤더 누락 수정 — 개발 계획

단축(버그 수정): 방안·설계 생략. 관련 설계는 `0002-github-proxy-설계.md`, `docs/jenkins/0002-jenkins-proxy-설계.md`.

## 문제

- GitHub 원격 MCP(프로토콜 `2026-07-28`)는 `x-mcp-header`가 붙은 인자(`owner`, `repo`)를 `Mcp-Param-*` 헤더로도 요구한다(SEP-2243). 없으면 `-32020 HeaderMismatch`다. `/mcp/x/all` 113개 중 87개 Tool이 해당한다.
- SDK 2.3.1은 **같은 McpClient가 `tools/list`로 받은 Tool 스키마**가 있을 때만 이 헤더를 붙인다.
- `McpRelayPool`은 연결별로 `tools/list`를 보장하지 않는다. 그래서 다음 경우 실패한다.
  - 새 연결에서 `call_tool`을 먼저 호출할 때(서버 재시작 직후)
  - 연결을 버린 뒤 재연결했을 때. 풀 캐시가 남아 `list_tools`도 upstream을 부르지 않는다
- 재현: 새 client에서 목록 없이 `list_branches` 호출 → `-32020 missing Mcp-Param-owner`, 같은 client에서 `ListToolsAsync` 후 호출 → 성공.

## 결정

1. 연결마다 처음 호출 전에 `tools/list`를 한 번 받아 SDK 스키마를 확보한다. 받은 목록으로 풀 캐시도 갱신한다.
2. `call_tool`이 upstream `-32020`(`McpErrorCode.HeaderMismatch`)을 받으면 같은 연결에서 `tools/list`를 다시 받고 한 번만 재시도한다(스펙 SHOULD). 재시도도 실패하면 기존처럼 `{label} '{instance}' returned error ...`로 감싼다. 그 밖의 오류는 재시도하지 않는다(기존 결정 유지).
3. Jenkins에도 똑같이 적용된다(공통 코어). Jenkins는 연결당 `tools/list`가 1회 늘어날 뿐 결과·메시지는 같다.
4. `list_tools`의 캐시 동작은 유지한다. 캐시가 있으면 upstream을 부르지 않는다. 다만 연결을 새로 만들면 1에 따라 목록을 다시 받는다.

## 작업 단위

### 1. 연결별 목록 확보

- **대상**: `Relay/McpRelayConnection.cs`, `Relay/McpRelayPool.cs`
- **코드 초안**:

```csharp
// McpRelayConnection: 이 연결에서 tools/list를 받았는지
private int _toolsListed;
public bool ToolsListed => Volatile.Read(ref _toolsListed) == 1;
public void MarkToolsListed() => Volatile.Write(ref _toolsListed, 1);
```

```csharp
// McpRelayPool
public async Task<IReadOnlyList<Tool>> ListToolsAsync(McpRelayTarget instance, bool refresh, CancellationToken cancellationToken)
{
    if (!refresh && _tools.TryGetValue(instance.Name, out IReadOnlyList<Tool>? cached))
        return cached;
    return await InvokeAsync(instance, (c, ct) => ListOnConnectionAsync(instance, c, ct), cancellationToken, prime: false).ConfigureAwait(false);
}

public Task<CallToolResult> CallToolAsync(McpRelayTarget instance, string tool, IReadOnlyDictionary<string, object?>? arguments, CancellationToken cancellationToken)
    => InvokeAsync(instance, (c, ct) => CallWithHeaderRetryAsync(instance, c, tool, arguments, ct), cancellationToken, prime: true);

// InvokeAsync 안, 연결을 얻은 뒤
McpRelayConnection connection = await entry.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
if (prime && !connection.ToolsListed)
    await ListOnConnectionAsync(instance, connection, cancellationToken).ConfigureAwait(false);
return await call(connection, cancellationToken).ConfigureAwait(false);

private async Task<IReadOnlyList<Tool>> ListOnConnectionAsync(McpRelayTarget instance, McpRelayConnection connection, CancellationToken ct)
{
    IList<McpClientTool> tools = await connection.Client.ListToolsAsync(cancellationToken: ct).ConfigureAwait(false);
    Tool[] result = [.. tools.Select(t => t.ProtocolTool)];
    _tools[instance.Name] = result;
    connection.MarkToolsListed();
    return result;
}
```

- `call` 대리자의 인자를 `McpClient`에서 `McpRelayConnection`으로 바꾼다. 목록 확보 중 예외는 기존 예외 분류(프로토콜 오류는 연결 유지, 그 밖은 폐기)를 그대로 탄다.
- 동시 호출로 같은 연결에서 목록을 두 번 받을 수 있다. 결과는 같으므로 잠그지 않는다.
- **검증**:
  - 새 연결에서 `CallToolAsync`만 호출 → 가짜 서버 `ListToolsCount == 1`, 이어서 같은 연결로 다시 호출 → 1 그대로
  - 연결 폐기 후 재연결 → `CallToolAsync`에서 목록을 다시 받는다(`ListToolsCount` 증가)
  - 기존 `McpRelayPoolTests`·Jenkins·GitHub 테스트 단언 유지
- **선행 단위**: 없음

### 2. HeaderMismatch 재시도

- **대상**: `Relay/McpRelayPool.cs`, tests `Relay/FakeMcpServer.cs`
- **코드 초안**:

```csharp
private async Task<CallToolResult> CallWithHeaderRetryAsync(McpRelayTarget instance, McpRelayConnection connection, string tool, IReadOnlyDictionary<string, object?>? arguments, CancellationToken ct)
{
    try
    {
        return await connection.Client.CallToolAsync(tool, arguments, cancellationToken: ct).ConfigureAwait(false);
    }
    catch (McpProtocolException ex) when (ex.ErrorCode == McpErrorCode.HeaderMismatch)
    {
        // 스키마가 바뀌었을 수 있으므로 목록을 다시 받아 한 번만 재시도한다
        await ListOnConnectionAsync(instance, connection, ct).ConfigureAwait(false);
        return await connection.Client.CallToolAsync(tool, arguments, cancellationToken: ct).ConfigureAwait(false);
    }
}
```

```csharp
// FakeMcpServer: 앞의 N번 CallTool을 HeaderMismatch로 실패시킨다
public int HeaderMismatchFailures { get; set; }
// CallToolHandler 안
if (HeaderMismatchFailures > 0 && Interlocked.Decrement(ref _headerMismatchLeft) >= 0)
    throw new McpProtocolException("header mismatch: missing Mcp-Param-repo header", McpErrorCode.HeaderMismatch);
```

- **검증**:
  - 1회 실패 설정 → 결과 성공, `CallToolCount == 2`, 재시도 전에 `ListToolsCount` 1 증가, 연결 유지(`ConnectCount == 1`)
  - 2회 이상 실패 설정 → `McpException` "`Jenkins 'prod' returned error -32020`", `CallToolCount == 2`(재시도 1회만), 연결 유지
  - 다른 프로토콜 오류(`InvalidParams`)는 재시도하지 않는다 → `CallToolCount == 1`
- **선행 단위**: 1

### 3. 실제 GitHub 확인·문서

- **대상**: `ReadMe.md`(GitHub 절), 실행 확인
- `ReadMe.md`: GitHub 원격 MCP의 `Mcp-Param-*` 헤더는 ToolKit이 연결마다 `tools/list`로 받은 스키마로 자동으로 보낸다는 점, `-32020`이면 한 번 재시도한다는 점을 적는다.
- **검증**:
  - `dotnet build`, `dotnet test` 전체 통과
  - 테스트 도구로 ToolKit `McpRelayPool`을 실제 GitHub(`/mcp/`)에 연결해, 새 풀에서 목록 조회 없이 `list_branches` 호출 → 성공
- **선행 단위**: 2

### 4. 버전 1.3.1·Release pack

- 1.3.0은 이미 nuget.org에 배포됐으므로 이 수정은 **1.3.1**로 낸다(사용자 결정).
- **대상**: `src/Biz.Bizadm.MCP.ToolKit/Biz.Bizadm.MCP.ToolKit.csproj`
- **코드 초안**:

```xml
<Version>1.3.1</Version>
```

- **검증**: `dotnet build -c Release --no-incremental` → `dotnet pack -c Release --no-build` 종료 코드 0, `Biz.Bizadm.MCP.ToolKit.1.3.1.nupkg` 생성
- **선행 단위**: 3

## 순서

1 → 2 → 3 → 4. 단위별로 구현·검증한다. nuget.org 배포는 사용자가 한다.
