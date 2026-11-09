---
status: done
category: jenkins
feature: jenkins-proxy
phase: 개발
---

# jenkins-proxy 개발

선행: `0001-jenkins-proxy-방안.md`, `0002-jenkins-proxy-설계.md`

SDK: `ModelContextProtocol` 2.3.1 (`McpClient`, `HttpClientTransport`, `HttpTransportMode.StreamableHttp`).

## U1. 옵션·검증·등록

**대상**

- `src/Biz.Bizadm.MCP.ToolKit/Jenkins/JenkinsOptions.cs` — `JenkinsOptions`
- `src/Biz.Bizadm.MCP.ToolKit/Jenkins/JenkinsInstance.cs` — `JenkinsInstance`
- `src/Biz.Bizadm.MCP.ToolKit/JenkinsMcpServerBuilderExtensions.cs` — `WithJenkinsTools`

**코드 초안**

```csharp
public sealed class JenkinsInstance
{
    public string Name { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string ApiToken { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public static IMcpServerBuilder WithJenkinsTools(this IMcpServerBuilder builder, Action<JenkinsOptions> configure)
{
    ArgumentNullException.ThrowIfNull(builder);
    ArgumentNullException.ThrowIfNull(configure);

    JenkinsOptions options = new();
    configure(options);
    JenkinsOptions validated = Validate(options); // 복사본

    builder.Services.AddSingleton(validated);
    builder.Services.AddSingleton(sp => new JenkinsMcpClientPool(sp.GetRequiredService<JenkinsOptions>()));
    builder.WithTools<JenkinsTools>();
    return builder;
}

private static JenkinsOptions Validate(JenkinsOptions options)
{
    if (options.Instances is null || options.Instances.Count == 0)
        throw new ArgumentException("At least one Jenkins instance is required", nameof(options));
    HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
    List<JenkinsInstance> copies = [];
    foreach (JenkinsInstance? instance in options.Instances)
    {
        if (instance is null) throw new ArgumentException("Jenkins instance must not be null", nameof(options));
        string name = instance.Name?.Trim() ?? string.Empty;
        if (name.Length == 0) throw new ArgumentException("Jenkins instance Name is required", nameof(options));
        if (!names.Add(name)) throw new ArgumentException($"Duplicate Jenkins instance name: {name}", nameof(options));
        if (!Uri.TryCreate(instance.Endpoint?.Trim(), UriKind.Absolute, out Uri? endpoint) || endpoint.Scheme is not ("http" or "https"))
            throw new ArgumentException($"Jenkins instance '{name}' Endpoint must be an absolute http or https URI", nameof(options));
        if (string.IsNullOrWhiteSpace(instance.User)) throw new ArgumentException($"Jenkins instance '{name}' User is required", nameof(options));
        if (string.IsNullOrWhiteSpace(instance.ApiToken)) throw new ArgumentException($"Jenkins instance '{name}' ApiToken is required", nameof(options));
        copies.Add(new JenkinsInstance { Name = name, Endpoint = endpoint.AbsoluteUri, User = instance.User.Trim(), ApiToken = instance.ApiToken, Description = instance.Description?.Trim() });
    }
    return new JenkinsOptions { Instances = copies };
}
```

**검증**: `tests/.../Jenkins/JenkinsRegistrationTests.cs`

- 정상 등록 시 Tool 이름이 `jenkins_list_instances`, `jenkins_list_tools`, `jenkins_call_tool` 3개
- 인스턴스 0개·null 항목·이름 공백·중복(대소문자 무시)·Endpoint 비절대/ftp·User 공백·ApiToken 공백이면 `ArgumentException`, 메시지에 토큰 미포함
- 등록 뒤 원본 옵션을 바꿔도 DI의 `JenkinsOptions`가 바뀌지 않음
- 기존 Redmine과 함께 등록해도 예외 없음

**선행 단위**: 없음

## U2. client 풀·목록 캐시

**대상**: `src/Biz.Bizadm.MCP.ToolKit/Jenkins/JenkinsMcpClientPool.cs` — `internal sealed class JenkinsMcpClientPool : IAsyncDisposable, IDisposable`

**코드 초안**

```csharp
private static Task<McpClient> ConnectAsync(JenkinsInstance instance, CancellationToken cancellationToken)
{
    string basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{instance.User}:{instance.ApiToken}"));
    HttpClientTransport transport = new(new HttpClientTransportOptions
    {
        Endpoint = new Uri(instance.Endpoint),
        TransportMode = HttpTransportMode.StreamableHttp,
        Name = $"jenkins-{instance.Name}",
        AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Basic {basic}" },
    });
    return McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
}

public JenkinsInstance Resolve(string? name)
{
    if (!string.IsNullOrWhiteSpace(name) && _instances.TryGetValue(name.Trim(), out JenkinsInstance? instance))
        return instance;
    throw new McpException($"instance is required. Registered instances: {string.Join(", ", _instances.Keys)}");
}

public async Task<IReadOnlyList<Tool>> ListToolsAsync(JenkinsInstance instance, bool refresh, CancellationToken cancellationToken)
{
    if (!refresh && _tools.TryGetValue(instance.Name, out IReadOnlyList<Tool>? cached))
        return cached;
    IList<McpClientTool> tools = await InvokeAsync(instance, (c, ct) => c.ListToolsAsync(cancellationToken: ct).AsTask(), cancellationToken);
    Tool[] result = [.. tools.Select(t => t.ProtocolTool)];
    _tools[instance.Name] = result; // 실패하면 여기까지 오지 않으므로 캐시하지 않는다
    return result;
}

private async Task<T> InvokeAsync<T>(JenkinsInstance instance, Func<McpClient, CancellationToken, Task<T>> call, CancellationToken cancellationToken)
{
    // 연결은 호출자 취소와 분리해 취소된 Task가 캐시되지 않게 한다
    Lazy<Task<McpClient>> entry = _clients.GetOrAdd(instance.Name, _ => new(() => _connect(instance, CancellationToken.None)));
    try
    {
        McpClient client = await entry.Value.WaitAsync(cancellationToken);
        return await call(client, cancellationToken);
    }
    catch (McpProtocolException)
    {
        throw; // upstream이 보낸 오류. client 유지
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        throw; // 호출자 취소. client 유지
    }
    catch (Exception ex) when (ex is not McpException)
    {
        await EvictAsync(instance.Name, entry); // 같은 entry일 때만 제거·dispose
        throw new McpException(DescribeFailure(instance.Name, ex), ex);
    }
}

internal static string DescribeFailure(string instanceName, Exception ex)
{
    // 연결 단계 예외는 다른 예외에 감싸여 올 수 있으므로 InnerException 사슬에서 HTTP 상태를 찾는다
    HttpStatusCode? code = null;
    for (Exception? e = ex; e is not null && code is null; e = e.InnerException)
        code = (e as HttpRequestException)?.StatusCode;
    string status = code is { } c ? $" (HTTP {(int)c})" : string.Empty;
    return $"Jenkins '{instanceName}' request failed{status}: {ex.Message}"; // User·ApiToken 미포함
}
```

- `_instances`, `_clients`, `_tools`는 `StringComparer.OrdinalIgnoreCase`
- `ListToolsAsync`·`CallToolAsync`는 `ValueTask`를 반환하므로 `.AsTask()`로 넘긴다
- `CallToolAsync(instance, tool, args, ct)`는 `InvokeAsync`로 `client.CallToolAsync(tool, args, cancellationToken: ct).AsTask()` 호출
- `DisposeAsync`/`Dispose`는 완료된 client만 dispose
- 테스트용 `internal JenkinsMcpClientPool(JenkinsOptions, Func<JenkinsInstance, CancellationToken, Task<McpClient>>)`

**검증**: `tests/.../Jenkins/JenkinsMcpClientPoolTests.cs`. 테스트 안에 Channel 기반 in-memory `IClientTransport`/`ITransport` 쌍과 `McpServer.Create`로 가짜 Jenkins 서버를 띄운다.

- 같은 인스턴스는 client를 한 번만 만든다
- 두 번째 `ListToolsAsync`는 upstream을 다시 부르지 않고, `refresh=true`면 다시 부른다
- 인스턴스별로 다른 목록을 돌려준다
- 연결 실패(생성기가 `HttpRequestException(…, HttpStatusCode.Unauthorized)`)는 `McpException`으로 감싸지고, 메시지에 인스턴스 이름·`HTTP 401`이 있고 User·토큰이 없으며 `InnerException`이 원 예외다. 다음 호출에서 다시 생성한다
- 호출자 취소는 `OperationCanceledException` 그대로이고 client를 유지한다. 호출자 취소가 아닌 `TaskCanceledException`은 `McpException`으로 감싸지고 client를 폐기한다
- upstream이 JSON-RPC 오류를 보내면 `McpProtocolException` 그대로이고 client를 유지한다
- 생성한 client를 pool dispose 시 dispose한다(HttpClient 정리 확인)
- 미등록·공백 이름 `Resolve`는 `McpException`, 메시지에 등록 이름 포함·토큰 미포함
- `ConnectAsync`가 만든 transport 옵션에 Basic 헤더(`user:token` base64)가 들어간다(헤더 생성 부분을 `internal static` 메서드로 분리해 검사)

**선행 단위**: U1

## U3. 게이트웨이 Tool

**대상**: `src/Biz.Bizadm.MCP.ToolKit/Tools/Jenkins/JenkinsTools.cs` — `[McpServerToolType] public sealed class JenkinsTools`

**코드 초안**

```csharp
[McpServerTool(Name = "jenkins_list_tools", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
[Description("지정한 Jenkins 인스턴스의 MCP Tool 목록(이름, 설명, 입력 스키마, 주석)을 조회한다. 결과는 인스턴스별로 캐시된다.")]
public async Task<string> ListTools(
    [Description("Jenkins 인스턴스 이름. jenkins_list_instances로 확인")] string instance,
    [Description("true면 캐시를 무시하고 다시 조회")] bool refresh = false,
    CancellationToken cancellationToken = default)
{
    JenkinsInstance target = _pool.Resolve(instance);
    IReadOnlyList<Tool> tools = await _pool.ListToolsAsync(target, refresh, cancellationToken);
    JsonArray items = [];
    foreach (Tool tool in tools)
    {
        items.Add(new JsonObject
        {
            ["name"] = tool.Name,
            ["title"] = tool.Title,
            ["description"] = tool.Description,
            ["inputSchema"] = JsonNode.Parse(tool.InputSchema.GetRawText()),
            ["annotations"] = tool.Annotations is null ? null : JsonSerializer.SerializeToNode(tool.Annotations, McpJsonUtilities.DefaultOptions),
        });
    }
    return new JsonObject { ["instance"] = target.Name, ["tools"] = items }.ToJsonString();
}

[McpServerTool(Name = "jenkins_call_tool", ReadOnly = false, Idempotent = false, Destructive = true, OpenWorld = true)]
[Description("지정한 Jenkins 인스턴스의 MCP Tool을 호출하고 결과를 그대로 반환한다. Tool 이름과 인자는 jenkins_list_tools로 확인한다.")]
public Task<CallToolResult> CallTool(
    [Description("Jenkins 인스턴스 이름")] string instance,
    [Description("호출할 Jenkins MCP Tool 이름")] string tool,
    [Description("Tool 인자. JSON 객체 또는 JSON 객체 문자열")] JsonElement? arguments = null,
    CancellationToken cancellationToken = default)
{
    JenkinsInstance target = _pool.Resolve(instance);
    if (string.IsNullOrWhiteSpace(tool))
        throw new McpException("tool is required.");
    return _pool.CallToolAsync(target, tool.Trim(), ToArguments(arguments), cancellationToken);
}

internal static Dictionary<string, object?>? ToArguments(JsonElement? arguments)
{
    if (arguments is not { } value || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        return null;
    if (value.ValueKind == JsonValueKind.String)
    {
        string? text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
            return null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            value = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new McpException("arguments must be a JSON object or a JSON object string.");
        }
    }
    if (value.ValueKind != JsonValueKind.Object)
        throw new McpException("arguments must be a JSON object or a JSON object string.");
    return value.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
}
```

`jenkins_list_instances`는 `[{name, description, endpoint}]` JSON 문자열(User·ApiToken 제외, ReadOnly·Idempotent·Destructive=false·OpenWorld=false).

**검증**: `tests/.../Jenkins/JenkinsToolsTests.cs` (U2의 가짜 서버 재사용)

- `jenkins_list_instances` 결과에 이름·설명·Endpoint가 있고 User·토큰 문자열이 없다
- `jenkins_list_tools` 결과의 `tools[].name`·`inputSchema`가 가짜 서버와 같다
- `jenkins_call_tool`이 인자를 그대로 전달하고 upstream `CallToolResult`(content, `IsError`)를 그대로 반환한다
- `arguments` 생략·null·빈 문자열이면 인자 없이 호출된다
- `arguments`가 JSON 객체 문자열(`"{\"name\":\"job\"}"`)이면 객체와 같은 인자로 전달된다
- `instance` 미등록, `tool` 공백, `arguments`가 배열·숫자·객체가 아닌 JSON 문자열(`"[1]"`)·잘못된 JSON 문자열이면 upstream을 호출하지 않고 `McpException`
- upstream 전송 오류 시 MCP Server 경유 결과가 `IsError=true`이고 텍스트에 인스턴스 이름·HTTP 상태가 있다(일반 메시지로 바뀌지 않음)
- MCP Server 경유 호출에서 `jenkins_call_tool` 반환 `CallToolResult`가 감싸지지 않고 그대로 나간다(SDK 동작 확인)

**선행 단위**: U2

## U4. ReadMe

**대상**: `ReadMe.md` — Jenkins 절 추가

**코드 초안**

```csharp
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithJenkinsTools(o =>
    {
        o.Instances.Add(new JenkinsInstance
        {
            Name = "prod",
            Endpoint = "http://jenkins.example:8080/mcp-server/mcp",
            User = "builder",
            ApiToken = "<api token>",
            Description = "운영 빌드",
        });
    });
```

Tool 3개 표와 "API Token 암복호는 소비 서버 책임", "Streamable HTTP만 지원"을 적는다.

**검증**: 문서 예제의 타입·멤버 이름이 U1과 같은지 대조, `dotnet build` 성공

**선행 단위**: U3

## U5. 독립 검토 반영

설계 결정 11~13. U1~U4 구현 뒤 독립 검토 지적 6건을 반영한다.

**대상**: `Jenkins/JenkinsConnection.cs`(신규), `Jenkins/JenkinsMcpClientPool.cs`, `Tools/Jenkins/JenkinsTools.cs`, `JenkinsMcpServerBuilderExtensions.cs`, `ReadMe.md`, 테스트 3개 파일

**코드 초안**

```csharp
internal sealed class JenkinsConnection(McpClient client, IAsyncDisposable? owner = null) : IAsyncDisposable
{
    public McpClient Client => client;

    public async ValueTask DisposeAsync()
    {
        try { await client.DisposeAsync(); }
        finally { if (owner is not null) await owner.DisposeAsync(); } // HttpClientTransport 소유 HttpClient 정리
    }
}

private static async Task<JenkinsConnection> ConnectAsync(JenkinsInstance instance, CancellationToken ct)
{
    HttpClientTransport transport = new(CreateTransportOptions(instance));
    try { return new JenkinsConnection(await McpClient.CreateAsync(transport, cancellationToken: ct), transport); }
    catch { await transport.DisposeAsync(); throw; }
}

// InvokeAsync
catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
catch (McpProtocolException ex)
{
    throw new McpException($"Jenkins '{instance.Name}' returned error {ex.ErrorCode}: {ex.Message}", ex); // client 유지
}
catch (Exception ex)
{
    Evict(instance.Name, entry); // TryRemove 후 _ = DisposeQuietlyAsync(entry): 기다리지 않음
    throw new McpException(DescribeFailure(instance.Name, ex), ex);
}

// DisposeAsync: await Task.WhenAll(남은 entry.Select(DisposeQuietlyAsync))

// JenkinsTools
public Task<CallToolResult> CallTool(
    [Description("Jenkins 인스턴스 이름(필수)")] string? instance = null,
    [Description("호출할 Jenkins MCP Tool 이름(필수)")] string? tool = null, ...)

// 검증
if (!string.IsNullOrEmpty(endpoint.UserInfo))
    throw new ArgumentException($"Jenkins instance '{name}' Endpoint must not contain user info", nameof(options));
```

**검증**

- 테스트 생성기는 `JenkinsConnection`을 반환하도록 바꾼다(소유 자원으로 dispose 기록용 가짜 사용)
- pool dispose와 폐기 시 소유 자원이 dispose된다
- 폐기 dispose가 끝나지 않아도(완료되지 않는 소유 자원) 실패 응답은 바로 돌아온다
- upstream 프로토콜 오류는 `McpException`(인스턴스 이름·오류 코드 포함)이고 client를 유지한다
- 생성기가 SDK 일반 `McpException`을 던지면 감싸지고 재연결된다
- MCP Server 경유로 없는 upstream Tool을 호출하면 `IsError` 결과이고 텍스트에 인스턴스 이름이 있다
- `instance`·`tool`을 생략한 호출은 `McpException`이고 `instance` 생략이면 등록 이름이 안내된다
- Endpoint에 `user:token@`이 있으면 `ArgumentException`이고 메시지에 토큰이 없다
- 전체 `dotnet build`·`dotnet test` 통과

**선행 단위**: U4

## 전체 검증

- `dotnet build Biz.Bizadm.MCP.ToolKit.slnx` 종료 코드 0
- `dotnet test tests/Biz.Bizadm.MCP.ToolKit.Tests` 종료 코드 0(기존 테스트 포함)
- 실제 Jenkins(`http://20.249.211.13:8080/mcp-server/mcp`) 연결은 User·API Token이 제공될 때만 수동으로 확인한다. 제공되지 않으면 미검증으로 보고한다.
