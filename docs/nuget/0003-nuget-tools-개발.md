---
status: done
category: nuget
feature: nuget-tools
phase: 개발
---

# nuget-tools 개발

선행: `0001-nuget-tools-방안.md`, `0002-nuget-tools-설계.md`. GitHub 이슈 #4.

기존 패턴: `Jenkins/JenkinsRestClient.cs`(전송·오류), `JenkinsMcpServerBuilderExtensions.cs`(등록 검증), 테스트는 새 `NuGetRecordingHandler`(단위 2)를 쓴다. 기존 `GitHubRecordingHandler`는 동기 큐라 지연·게이팅 응답과 동시 호출을 다룰 수 없다. gzip 해제와 `AllowAutoRedirect`는 처리기를 주입하면 검증할 수 없으므로 기본 처리기 설정 코드 검토로 확인한다.

네임스페이스 `Biz.Bizadm.MCP.ToolKit.NuGet`, `...Tools.NuGet`, `...Tests.NuGet` 안에서는 `NuGet.Versioning`이 상대 이름으로 해석될 수 있다. `using NuGet.Versioning;`은 파일 최상단(네임스페이스 선언 밖)에 두고, 정규화 이름이 필요하면 `global::NuGet.Versioning`을 쓴다.

## 단위 1. 설정·등록·인스턴스 목록

**대상**: `NuGet/NuGetInstance.cs`, `NuGet/NuGetOptions.cs`, `Relay/McpRelayValidation.cs`(`NameAndEndpoint` 선택 인자), `NuGetMcpServerBuilderExtensions.cs`, `NuGet/NuGetFeedClient.cs`(생성자·`Instances`·`Resolve`만), `Tools/NuGet/NuGetTools.cs`(`nuget_list_instances`만), `Directory.Packages.props`·csproj(`NuGet.Versioning`, `PackageTags`에 `nuget`)

**코드 초안**

```csharp
public sealed class NuGetInstance
{
    public string Name { get; set; } = "";
    public string ServiceIndex { get; set; } = "";   // index.json 절대 http/https URL. 비밀을 넣지 않는다
    public string? Description { get; set; }
    public string? ApiKey { get; set; }               // 쓰기용 X-NuGet-ApiKey
    public string? User { get; set; }                 // 읽기 인증이 필요한 피드용 Basic
    public string? Password { get; set; }
}
```

```csharp
// McpRelayValidation: 기존 호출부는 그대로
public static (string Name, string Endpoint) NameAndEndpoint(string label, HashSet<string> names, string? name,
    string? endpoint, string credentialHint, string property = "Endpoint")
{
    // ... 기존 이름 검사
    return (trimmed, AbsoluteHttpUri(label, trimmed, property, endpoint, credentialHint));
}
```

```csharp
private static NuGetOptions Validate(NuGetOptions options)
{
    McpRelayValidation.RequireInstances(Label, options.Instances);
    HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
    List<NuGetInstance> copies = [];
    foreach (NuGetInstance? item in options.Instances)
    {
        NuGetInstance instance = McpRelayValidation.NotNull(Label, item);
        (string name, string serviceIndex) = McpRelayValidation.NameAndEndpoint(Label, names, instance.Name,
            instance.ServiceIndex, "User and Password", property: "ServiceIndex");
        string? user = Blank(instance.User)?.Trim(), password = Blank(instance.Password), apiKey = Blank(instance.ApiKey);
        if ((user is null) != (password is null))
            throw new ArgumentException($"NuGet instance '{name}' User and Password must be set together", nameof(options));
        if (user?.Contains(':') == true)
            throw new ArgumentException($"NuGet instance '{name}' User must not contain ':'", nameof(options));
        RejectControl(name, "User", user); RejectControl(name, "Password", password); RejectControl(name, "ApiKey", apiKey);
        copies.Add(new NuGetInstance { Name = name, ServiceIndex = serviceIndex, User = user, Password = password,
            ApiKey = apiKey, Description = Blank(instance.Description)?.Trim() });
    }
    return new NuGetOptions { Instances = copies };
}

// 값은 메시지에 넣지 않는다
private static void RejectControl(string name, string property, string? value)
{
    if (value is not null && value.Any(char.IsControl))
        throw new ArgumentException($"NuGet instance '{name}' {property} must not contain control characters", "options");
}
```

```csharp
[McpServerTool(Name = "nuget_list_instances", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
public string ListInstances() => new JsonArray([.. _client.Instances.Select(i => (JsonNode)new JsonObject
{
    ["name"] = i.Name, ["description"] = i.Description, ["serviceIndex"] = i.ServiceIndex, ["canWrite"] = i.ApiKey is not null,
})]).ToJsonString();
```

`NuGet.Versioning` 버전은 규칙 `dotnet/nuget-package-selection.md`로 고른다.

**검증**: `NuGetRegistrationTests` — 등록 성공·Tool 주석, 검증 실패(인스턴스 없음, 중복 이름, 상대 URI, `user@` URI → 문구에 `ServiceIndex`, User만 있음, User에 `:`, ApiKey·Password 제어 문자 → 메시지에 값 없음), 기존 Jenkins·GitHub 등록 테스트 통과(`Endpoint` 문구 불변), `nuget_list_instances` 결과에 ApiKey·Password 없음. `dotnet build`·`dotnet test` 통과.

**선행 단위**: 없음

## 단위 2. 전송·service index·인증·오류

**대상**: `NuGet/NuGetFeedClient.cs`, `NuGet/NuGetServiceIndex.cs`, `tests/.../NuGet/NuGetRecordingHandler.cs`(먼저 작성)

**코드 초안**

```csharp
// 테스트 처리기: 비동기 응답, 게이팅, 스레드 안전 기록
internal sealed class NuGetRecordingHandler : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly Queue<Func<CancellationToken, Task<HttpResponseMessage>>> _responses = new();
    private readonly List<(HttpMethod Method, Uri Uri, string? Body, Dictionary<string, string> Headers)> _requests = [];

    public IReadOnlyList<(HttpMethod Method, Uri Uri, string? Body, Dictionary<string, string> Headers)> Requests
    { get { lock (_gate) return [.. _requests]; } }

    public void Enqueue(HttpStatusCode status, string body = "", string mediaType = "application/json", Action<HttpResponseMessage>? configure = null);
    public void EnqueueException(Exception exception);
    public TaskCompletionSource<HttpResponseMessage> EnqueueGate();   // 테스트가 완료 시점을 정한다
    public void EnqueueDelay(TimeSpan delay);                          // ct가 취소되면 OperationCanceledException

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        // 본문·헤더 기록(lock) → 큐에서 꺼내(lock, 비면 200 {}) → await next(ct)
    }
}
```

```csharp
internal enum NuGetResource { Search, PackageBase, Registrations, Publish }

internal sealed class NuGetServiceIndex(IReadOnlyList<(string Type, Uri Id)> resources)
{
    private static readonly Dictionary<NuGetResource, (string Name, string[] Types)> Map = new()
    {
        [NuGetResource.Search] = ("search", ["SearchQueryService/3.5.0", "SearchQueryService/3.0.0-rc", "SearchQueryService/3.0.0-beta", "SearchQueryService"]),
        [NuGetResource.PackageBase] = ("package base address", ["PackageBaseAddress/3.0.0"]),
        [NuGetResource.Registrations] = ("registrations", ["RegistrationsBaseUrl/3.6.0", "RegistrationsBaseUrl/3.4.0",
            "RegistrationsBaseUrl/3.0.0-rc", "RegistrationsBaseUrl/3.0.0-beta", "RegistrationsBaseUrl"]),
        [NuGetResource.Publish] = ("publish", ["PackagePublish/2.0.0"]),
    };

    public static string NameOf(NuGetResource r) => Map[r].Name;
    public static IReadOnlyList<string> TypesOf(NuGetResource r) => Map[r].Types;

    public Uri? Find(NuGetResource r) => Map[r].Types
        .Select(t => resources.FirstOrDefault(x => x.Type == t).Id).FirstOrDefault(u => u is not null);

    // 취약점 정보 제공 피드 판정(호출하지 않고 존재만 본다)
    public bool ProvidesVulnerabilityInfo => resources.Any(r => r.Type == "VulnerabilityInfo/6.7.0");

    // resources 없음·배열 아님·루트가 객체 아님 → null(호출자가 unexpected JSON)
    // 절대 URI가 아닌 @id는 건너뜀, @type이 문자열 배열이면 원소마다 항목으로 펼침
    public static NuGetServiceIndex? Parse(JsonNode root) { /* Json.Prop(root, "resources") as JsonArray ... */ }
}

// 모든 속성 접근은 이 도우미로: 객체가 아니면 null(인덱서의 InvalidOperationException 방지)
internal static class Json
{
    public static JsonNode? Prop(JsonNode? node, string name) => (node as JsonObject)?[name];
}
```

```csharp
private readonly ConcurrentDictionary<string, Lazy<Task<NuGetServiceIndex>>> _indexes = new(StringComparer.OrdinalIgnoreCase);

public async Task<Uri> GetResourceAsync(NuGetInstance instance, NuGetResource resource, CancellationToken ct)
{
    NuGetServiceIndex index = await GetIndexAsync(instance, ct).ConfigureAwait(false);
    Uri uri = index.Find(resource) ?? throw new McpException($"NuGet '{instance.Name}' service index has no "
        + $"{NuGetServiceIndex.NameOf(resource)} resource ({string.Join(", ", NuGetServiceIndex.TypesOf(resource))}).");
    // scheme 하향 방지: https 인스턴스가 http publish 주소로 ApiKey를 보내지 않게
    if (resource == NuGetResource.Publish && _origins[instance.Name].Scheme == Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttps)
        throw new McpException($"NuGet '{instance.Name}' publish resource is not https; ApiKey would be sent in clear text.");
    // 경로 결합을 쓰는 리소스는 query·fragment가 있으면 거부(검색은 UriBuilder)
    if (resource != NuGetResource.Search && (uri.Query.Length > 0 || uri.Fragment.Length > 0))
        throw Unexpected(instance, new Uri(instance.ServiceIndex), $"{NuGetServiceIndex.NameOf(resource)} @id must not contain query or fragment");
    return uri;
}

public async Task<bool> ProvidesVulnerabilityInfoAsync(NuGetInstance instance, CancellationToken ct) =>
    (await GetIndexAsync(instance, ct).ConfigureAwait(false)).ProvidesVulnerabilityInfo;

private async Task<NuGetServiceIndex> GetIndexAsync(NuGetInstance instance, CancellationToken ct)
{
    Lazy<Task<NuGetServiceIndex>> entry = _indexes.GetOrAdd(instance.Name, _ => new(() => LoadIndexAsync(instance)));
    NuGetServiceIndex index;
    try
    {
        index = await entry.Value.WaitAsync(ct).ConfigureAwait(false);
    }
    catch (Exception) when (!ct.IsCancellationRequested)
    {
        _indexes.TryRemove(new KeyValuePair<string, Lazy<Task<NuGetServiceIndex>>>(instance.Name, entry));
        throw;
    }
    return index;
}

// 공유 Task: 호출자 토큰을 쓰지 않는다(CancellationToken.None + 읽기 시간 제한)
private async Task<NuGetServiceIndex> LoadIndexAsync(NuGetInstance instance)
{
    Uri uri = new(instance.ServiceIndex);
    JsonNode root = (await GetJsonAsync(instance, uri, CancellationToken.None, notFoundAsNull: false).ConfigureAwait(false))!;
    return NuGetServiceIndex.Parse(root) ?? throw Unexpected(instance, uri, "resources array is missing");
}
```

```csharp
private async Task<(int Status, string Body, string? Reason, Uri? Location)> SendAsync(NuGetInstance instance,
    HttpMethod method, Uri uri, HttpContent? content, bool publish, CancellationToken ct)
{
    TimeSpan timeout = method == HttpMethod.Put ? _pushTimeout : _readTimeout;   // 기본 10분 / 100초, internal 생성 인자로 주입 가능
    using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(timeout);
    try
    {
        // 헤더 설정도 try 안: 형식 예외도 McpException(값은 메시지에 넣지 않음)
        using HttpRequestMessage request = new(method, uri) { Content = content, Version = HttpVersion.Version11 };
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Add("X-NuGet-Protocol-Version", "4.1.0");
        if (instance.User is not null && SameOrigin(uri, _origins[instance.Name]))
            request.Headers.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{instance.User}:{instance.Password}")));
        if (publish)
            request.Headers.Add("X-NuGet-ApiKey", instance.ApiKey);

        using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
        string body = await ReadLimitedAsync(response.Content, _maxResponseBytes, cts.Token).ConfigureAwait(false)
            ?? throw new McpException($"NuGet '{instance.Name}' {method} {Display(uri)} response exceeded {_maxResponseBytes} bytes.");
        Uri? location = response.Headers.Location is { } l ? new Uri(uri, l) : null;
        return ((int)response.StatusCode, body, response.ReasonPhrase, location);
    }
    catch (McpException) { throw; }
    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
    catch (OperationCanceledException ex) when (cts.IsCancellationRequested)
    {
        throw new McpException($"NuGet '{instance.Name}' {method} {Display(uri)} timed out after {(int)timeout.TotalSeconds}s.", ex);
    }
    catch (Exception ex)
    {
        // FormatException(헤더)은 메시지에 값이 들어갈 수 있어 일반 문구로 바꾼다
        string reason = ex is FormatException ? "invalid request header value" : ex.Message;
        throw new McpException($"NuGet '{instance.Name}' {method} {Display(uri)} failed: {reason}", ex);
    }
}

// Content-Length가 상한을 넘거나 읽은 바이트가 상한을 넘으면 null
private static async Task<string?> ReadLimitedAsync(HttpContent content, long max, CancellationToken ct)
{
    if (content.Headers.ContentLength > max) return null;
    await using Stream stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
    using MemoryStream buffer = new();
    byte[] chunk = new byte[81920];
    int read;
    while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
    {
        if (buffer.Length + read > max) return null;
        buffer.Write(chunk, 0, read);
    }
    // 자동 압축 해제 뒤 바이트 기준. UTF-8 BOM은 건너뛴다(JsonNode.Parse(string)가 ﻿에서 실패)
    ReadOnlySpan<byte> bytes = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
    if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) bytes = bytes[3..];
    return Encoding.UTF8.GetString(bytes);
}

// notFoundAsNull=false면 404도 Failure(service index·검색)
public async Task<JsonNode?> GetJsonAsync(NuGetInstance instance, Uri uri, CancellationToken ct, bool notFoundAsNull = true)
{
    (int code, string body, string? reason, Uri? location) = await SendAsync(instance, HttpMethod.Get, uri, null, false, ct).ConfigureAwait(false);
    if (code == 404 && notFoundAsNull) return null;
    if (code is < 200 or >= 300) throw Failure(instance, HttpMethod.Get, uri, code, body, reason, location, publish: false);
    try { return JsonNode.Parse(body) ?? throw Unexpected(instance, uri, "empty JSON"); }
    catch (JsonException ex) { throw new McpException($"NuGet '{instance.Name}' {Display(uri)} returned invalid JSON: {ex.Message}", ex); }
}

// 409는 서버 사유를 보존해 돌려주고 처리는 Tool이 정한다
public async Task<(int Status, string Body, string? Reason)> SendPublishAsync(NuGetInstance instance, HttpMethod method, Uri uri,
    HttpContent? content, CancellationToken ct)
{
    (int code, string body, string? reason, Uri? location) = await SendAsync(instance, method, uri, content, true, ct).ConfigureAwait(false);
    return code is 200 or 201 or 202 or 204 or 409 ? (code, body, reason) : throw Failure(instance, method, uri, code, body, reason, location, publish: true);
}

internal static McpException Failure(NuGetInstance instance, HttpMethod method, Uri uri, int code, string body,
    string? reason, Uri? location, bool publish)
{
    string summary = code is >= 300 and < 400 ? $"redirected to {(location is null ? "(none)" : Display(location))}"
        : Summarize(body) is { Length: > 0 } s ? s : reason ?? "";
    if (code is 401 or 403)
        summary += publish ? " (check ApiKey and its package scope; Basic is sent only to the ServiceIndex origin)"
                           : " (check User/Password; Basic is sent only to the ServiceIndex origin)";
    return new McpException($"NuGet '{instance.Name}' {method} {Display(uri)} failed (HTTP {code}): {summary}");
}

internal static McpException Unexpected(NuGetInstance instance, Uri uri, string detail) =>
    new($"NuGet '{instance.Name}' {Display(uri)} returned unexpected JSON: {detail}");

// 사용자 정보·query 제거
internal static string Display(Uri uri) =>
    uri.GetComponents(UriComponents.Scheme | UriComponents.Host | UriComponents.Port | UriComponents.Path, UriFormat.UriEscaped);

internal static bool SameOrigin(Uri a, Uri b) =>
    Uri.Compare(a, b, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;

// 끝 '/' 보정 후 상대 경로 결합. parent는 query·fragment가 없음(GetResourceAsync가 보장)
internal static Uri Child(Uri parent, string relative) =>
    new(parent.AbsoluteUri.EndsWith('/') ? parent : new Uri(parent.AbsoluteUri + "/"), relative);
```

- 처리기: `SocketsHttpHandler { PooledConnectionLifetime = 2분, AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All }`, `HttpClient { Timeout = Timeout.InfiniteTimeSpan, DefaultRequestVersion = HttpVersion.Version11, DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact }`.
- 생성자: `NuGetFeedClient(NuGetOptions options, HttpMessageHandler? handler = null, TimeSpan? readTimeout = null, TimeSpan? pushTimeout = null, long? maxResponseBytes = null)` (뒤 네 인자는 테스트용, internal 생성자로 분리해도 된다. 기본 상한 50MB = 52,428,800바이트).
- `Child`가 받는 Registration page `@id`(service index 밖 주소)는 `Child`를 쓰지 않고 그대로 GET하므로 query 규칙과 무관하다.
- `Child`는 query가 있는 `@id`에 쓰지 않는다(검색은 단위 3에서 `UriBuilder`).

**검증**: `NuGetFeedClientTests`
- service index: 타입 우선순위(`-beta` 포함), 같은 타입 첫 항목, 리소스 없음 문구(용도 이름), `resources` 없음 → unexpected JSON, 상대 `@id` 건너뜀, 캐시(두 번째 호출에 index GET 없음), 실패 후 재시도, 동시 두 호출에 index GET 1회, 한 호출자 취소 후 캐시 유지
- 인증: 같은 origin에만 Basic, 다른 호스트엔 없음, ApiKey는 publish에만(읽기 요청엔 없음), 같은 origin publish에 Basic + ApiKey
- 오류: 404 → null, `notFoundAsNull: false`면 404 일반 실패, service index 404 일반 실패 형식, 3xx + Location(query 제거), ReasonPhrase 요약, 401·403 안내, 전송 오류 형식, 시간 초과 문구(`EnqueueDelay` + 짧은 `readTimeout`·`pushTimeout`), 호출자 취소 → `OperationCanceledException`, invalid JSON, 응답 50MB 초과(Content-Length와 본문 길이 각각, 테스트는 상한을 작게 주입할 수 있게 `MaxResponseBytes`도 internal 생성 인자), `{url}`에 사용자 정보 없음, 메시지에 Password·ApiKey 없음
- 헤더: `User-Agent`, `X-NuGet-Protocol-Version: 4.1.0`
- 3차 검토 추가: `@type` 배열 펼침, `ProvidesVulnerabilityInfo`, 루트가 배열인 service index → unexpected JSON, BOM 응답 파싱, https 인스턴스 + http publish `@id` → 거부(요청은 index GET 1건뿐), http 인스턴스 + http publish → 허용, query가 있는 base·registrations·publish `@id` → unexpected JSON, 409 본문·ReasonPhrase 보존
- 동시성: `EnqueueGate`로 index 응답을 막아 두고 두 호출을 시작 → index GET 1회, 한 호출자 취소 후 다른 호출 성공·캐시 유지

**선행 단위**: 단위 1

## 단위 3. 검색·버전 목록

**대상**: `NuGetTools.SearchPackages`, `NuGetTools.ListVersions`, 공통 인자 도우미(`RequireId`, `RequireVersion`, JSON 읽기 도우미)

**코드 초안**

```csharp
public async Task<string> SearchPackages(string? instance = null, string? query = null, bool prerelease = false,
    string? packageType = null, int skip = 0, int take = 20, CancellationToken cancellationToken = default)
{
    NuGetInstance target = _client.Resolve(instance);
    if (skip < 0 || take is < 1 or > 100)
        throw new McpException("skip must be >= 0 and take must be 1..100.");
    Uri search = await _client.GetResourceAsync(target, NuGetResource.Search, cancellationToken).ConfigureAwait(false);

    UriBuilder builder = new(search);
    string q = $"q={Uri.EscapeDataString(query?.Trim() ?? "")}&skip={skip}&take={take}&prerelease={(prerelease ? "true" : "false")}&semVerLevel=2.0.0"
        + (string.IsNullOrWhiteSpace(packageType) ? "" : "&packageType=" + Uri.EscapeDataString(packageType.Trim()));
    builder.Query = string.IsNullOrEmpty(builder.Query) ? q : builder.Query.TrimStart('?') + "&" + q;

    JsonNode root = (await _client.GetJsonAsync(target, builder.Uri, cancellationToken, notFoundAsNull: false).ConfigureAwait(false))!;
    JsonArray data = Json.Prop(root, "data") as JsonArray ?? throw NuGetFeedClient.Unexpected(target, search, "data array is missing");
    JsonArray packages = [.. data.Select(p => (JsonNode)new JsonObject
    {
        ["id"] = Required(target, search, p, "id"),
        ["version"] = Required(target, search, p, "version"),
        ["description"] = Truncate(Optional(p, "description"), 300),
        ["totalDownloads"] = OptionalLong(p, "totalDownloads"),   // 없으면 0
        ["verified"] = OptionalBool(p, "verified"),               // 없으면 null
        ["versionCount"] = (Json.Prop(p, "versions") as JsonArray)?.Count ?? 0,
    })];
    return new JsonObject { ["totalHits"] = OptionalLong(root, "totalHits"), ["packages"] = packages }.ToJsonString();
}
```

```csharp
public async Task<string> ListVersions(string? instance = null, string? id = null, bool prerelease = true, CancellationToken cancellationToken = default)
{
    NuGetInstance target = _client.Resolve(instance);
    string packageId = RequireId(id);
    Uri baseAddress = await _client.GetResourceAsync(target, NuGetResource.PackageBase, cancellationToken).ConfigureAwait(false);
    Uri uri = NuGetFeedClient.Child(baseAddress, $"{Uri.EscapeDataString(packageId.ToLowerInvariant())}/index.json");
    JsonNode root = await _client.GetJsonAsync(target, uri, cancellationToken).ConfigureAwait(false)
        ?? throw new McpException($"NuGet '{target.Name}' package '{packageId}' was not found.");
    List<string> raw = (Json.Prop(root, "versions") as JsonArray ?? throw NuGetFeedClient.Unexpected(target, uri, "versions array is missing"))
        .Select(v => v?.GetValueKind() == JsonValueKind.String ? v.GetValue<string>()
            : throw NuGetFeedClient.Unexpected(target, uri, "versions must contain strings")).ToList();

    List<(string Text, NuGetVersion? Parsed)> versions = [.. raw.Select(v => (v, NuGetVersion.TryParse(v, out NuGetVersion? p) ? p : null))];
    IEnumerable<string> ordered = versions.Where(v => v.Parsed is not null && (prerelease || !v.Parsed.IsPrerelease))
        .OrderBy(v => v.Parsed).Select(v => v.Text)
        .Concat(versions.Where(v => v.Parsed is null).Select(v => v.Text));   // 파싱 불가 원문은 끝에
    return new JsonObject { ["id"] = packageId, ["versions"] = new JsonArray([.. ordered.Select(v => (JsonNode)v)]) }.ToJsonString();
}
```

- 코드 초안의 모든 속성 접근(`Required`·`Optional*` 포함)은 `Json.Prop`을 쓴다. `node["x"]` 인덱서를 직접 쓰지 않는다.
- `Required`는 문자열이 아니거나 없으면 `Unexpected`, `Optional*`은 없거나 형식이 다르면 기본값. `GetValue<T>`의 `InvalidOperationException`·`FormatException`은 도우미 안에서 처리한다. 도우미는 항상 값(string·long·bool)을 꺼내 돌려주므로 결과에 피드 노드가 그대로 들어가지 않는다.

```csharp
private static readonly Regex PackageId = new(@"^\w+([.-]\w+)*$", RegexOptions.CultureInvariant);

private static string RequireId(string? id)
{
    string value = id?.Trim() ?? "";
    if (value.Length == 0)
        throw new McpException("id is required.");
    if (value.Length > 100 || !PackageId.IsMatch(value))
        throw new McpException($"id '{value}' is not a valid NuGet package id.");
    return value;
}
```
- Tool 설명: 검색 `packageType`은 `SearchQueryService/3.5.0`만 지원, 버전 목록은 unlisted 포함.

**검증**: `NuGetToolsTests` — 검색 루트가 배열·`data` 원소가 문자열 → unexpected JSON, id 규칙(`..`·`.`·`a/b`·101자 거부, 요청 0건), 비문자열 `versions` 원소 → unexpected JSON, 검색 404 일반 실패 형식, 검색 query 문자열(인코딩, packageType 유무, prerelease, 기존 query가 있는 `@id`), take·skip 범위 오류(요청 없음), description 300자 절단, 선택 필드 없는 사설 피드 응답, `data` 없음 → unexpected JSON, 버전 목록 URL(id 소문자, base 끝 `/` 없음), 정렬·prerelease 제외·파싱 불가 버전 끝에, 404 문구, id 누락 오류.

**선행 단위**: 단위 2

## 단위 4. 메타데이터·취약점

**대상**: `NuGetTools.GetPackageMetadata`, `NuGetTools.CheckVulnerabilities`, 내부 `ReadCatalogEntriesAsync`, `Detail`, `Severity`

**코드 초안**

```csharp
// JsonNode라 dispose할 문서가 없다
private async Task<List<JsonObject>> ReadCatalogEntriesAsync(NuGetInstance target, string id, CancellationToken ct)
{
    Uri reg = await _client.GetResourceAsync(target, NuGetResource.Registrations, ct).ConfigureAwait(false);
    Uri indexUri = NuGetFeedClient.Child(reg, $"{Uri.EscapeDataString(id.ToLowerInvariant())}/index.json");
    JsonNode index = await _client.GetJsonAsync(target, indexUri, ct).ConfigureAwait(false)
        ?? throw new McpException($"NuGet '{target.Name}' package '{id}' was not found.");

    List<JsonObject> entries = [];
    foreach (JsonNode? page in Json.Prop(index, "items") as JsonArray ?? throw NuGetFeedClient.Unexpected(target, indexUri, "items array is missing"))
    {
        if (page is not JsonObject)
            throw NuGetFeedClient.Unexpected(target, indexUri, "registration page must be an object");
        JsonArray? items = Json.Prop(page, "items") as JsonArray;
        if (items is null)
        {
            string? pageId = Json.Prop(page, "@id") is JsonValue idNode && idNode.GetValueKind() == JsonValueKind.String ? idNode.GetValue<string>() : null;
            if (!Uri.TryCreate(pageId, UriKind.Absolute, out Uri? pageUri))
                throw NuGetFeedClient.Unexpected(target, indexUri, "registration page @id is missing or not absolute");
            JsonNode pageRoot = await _client.GetJsonAsync(target, pageUri, ct).ConfigureAwait(false)
                ?? throw new McpException($"NuGet '{target.Name}' registration page for '{id}' was not found.");
            items = Json.Prop(pageRoot, "items") as JsonArray ?? throw NuGetFeedClient.Unexpected(target, pageUri, "items array is missing");
        }
        entries.AddRange(items.Select(leaf => Json.Prop(leaf, "catalogEntry") as JsonObject
            ?? throw NuGetFeedClient.Unexpected(target, indexUri, "catalogEntry is missing")));
    }
    return entries;
}
```

```csharp
string normalized = RequireVersion(version);   // TryParse 실패 → McpException("version '...' is not a valid NuGet version.")
// catalogEntry.version은 필수(Required): 없으면 unexpected JSON. 파싱 불가 값은 비교에서 제외
JsonObject? entry = entries.FirstOrDefault(e => NuGetVersion.TryParse(Required(target, indexUri, e, "version"), out NuGetVersion? v)
    && v.ToNormalizedString().Equals(normalized, StringComparison.OrdinalIgnoreCase));
if (entry is null)
    throw new McpException($"NuGet '{target.Name}' package '{packageId}' version '{normalized}' was not found.");

bool listed = OptionalBool(entry, "listed") ?? true;
string? published = Published(entry);   // 파싱한 날짜의 연도가 1900이면 null

static string? Severity(string? raw) => raw switch
{
    "0" => "low", "1" => "moderate", "2" => "high", "3" => "critical", _ => raw,
};

// 정보 제공 피드(service index에 VulnerabilityInfo/6.7.0)는 필드가 없으면 false·빈 배열, 그 밖은 null(정보 없음)
bool provides = await _client.ProvidesVulnerabilityInfoAsync(target, ct).ConfigureAwait(false);
JsonArray? vulnerabilities = Json.Prop(entry, "vulnerabilities") is JsonArray va
    ? [.. va.Select(x => (JsonNode)new JsonObject { ["severity"] = Severity(Optional(x, "severity")), ["advisoryUrl"] = Optional(x, "advisoryUrl") })]
    : provides ? [] : null;
bool? vulnerable = vulnerabilities is null ? null : vulnerabilities.Count > 0;
bool? deprecated = Json.Prop(entry, "deprecation") is JsonObject ? true : provides ? false : null;
```

- 결과의 모든 노드는 위처럼 값을 꺼내 새로 만든다. `nuget_check_vulnerabilities`도 `Detail`이 만든 노드를 다른 객체에 다시 넣지 않고, 같은 계산 함수에서 값을 받아 새 객체를 만든다(재부모화 `InvalidOperationException` 방지).
- 요약 목록: `{version, listed, published, deprecated, vulnerable}` (위 null 규칙)
- 상세: `projectUrl`, `dependencyGroups[].targetFramework`(없으면 null), `dependencies[].id/range`(range 없으면 null), `deprecation.reasons/message/alternatePackage.id/range`, `vulnerabilities[].severity(매핑)/advisoryUrl`. 모두 선택 필드 규칙을 따른다.
- `nuget_check_vulnerabilities`는 같은 `Detail` 결과에서 `{id, version, vulnerable, vulnerabilities, deprecated}`만 만든다.

**검증**: inline page와 외부 page GET, page `@id` 없음 → unexpected JSON, 정규화 비교(`1.0` ↔ `1.0.0`, build metadata), 버전 미발견 문구(`InvalidOperationException` 아님), 잘못된 버전 인자(요청 없음), `listed` 없음 → true, `published` 1900 → null, `severity` 매핑·알 수 없는 값·없음(null), 선택 필드 없는 registration, nuget.org 형태(service index에 `VulnerabilityInfo/6.7.0` + 필드 없음) → false·빈 배열, 리소스 없는 사설 피드 + 필드 없음 → null, 리소스 없는 피드라도 필드가 있으면 그 값, page·leaf가 객체가 아님 → unexpected JSON, `catalogEntry.version` 없음 → unexpected JSON, check 결과 JSON 직접 검증(예외 없음), 요약·상세 결과 JSON, 취약점 있음/없음.

**선행 단위**: 단위 2

## 단위 5. push

**대상**: `NuGet/NuGetPackageFile.cs`, `NuGetTools.PushPackage`, `ReadPackageFileAsync`

**코드 초안**

```csharp
internal static class NuGetPackageFile
{
    public static (string Id, string Version) ReadIdentity(byte[] package)
    {
        try
        {
            using ZipArchive zip = new(new MemoryStream(package), ZipArchiveMode.Read);
            ZipArchiveEntry nuspec = zip.Entries.FirstOrDefault(e => e.FullName.IndexOfAny(['/', '\\']) < 0
                && e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
                ?? throw new McpException("package has no .nuspec at its root.");
            using Stream stream = nuspec.Open();
            using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            XElement metadata = XDocument.Load(reader).Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "metadata")
                ?? throw new McpException(".nuspec has no metadata element.");
            string? Value(string name) => metadata.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim();
            string id = Value("id") is { Length: > 0 } i ? i : throw new McpException(".nuspec has no id.");
            return NuGetVersion.TryParse(Value("version"), out NuGetVersion? v)
                ? (id, v.ToNormalizedString())
                : throw new McpException($".nuspec version '{Value("version")}' is not a valid NuGet version.");
        }
        catch (InvalidDataException ex) { throw new McpException($"package is not a valid .nupkg (zip): {ex.Message}", ex); }
        catch (XmlException ex) { throw new McpException($".nuspec is not valid XML: {ex.Message}", ex); }
    }
}
```

```csharp
private static async Task<byte[]> ReadPackageFileAsync(string path, CancellationToken ct)
{
    // 파일에 접근하기 전에 검사: UNC·장치 경로는 SMB 인증을 일으킬 수 있다. 거부 목록이 아니라 허용 형태만 받는다
    if (!IsLocalAbsolute(path, OperatingSystem.IsWindows()) || !IsLocalAbsolute(FullPath(path), OperatingSystem.IsWindows()))
        throw new McpException("path must be an absolute local file path; UNC and device paths are not allowed.");
    if (!path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
        throw new McpException("path must be a .nupkg file (.snupkg is not supported).");
    if (!File.Exists(path))
        throw new McpException($"package file was not found: {path}");
    try { return await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false); }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    { throw new McpException($"package file could not be read: {ex.Message}", ex); }
}

private static readonly Regex DrivePath = new(@"^[A-Za-z]:[\\/]", RegexOptions.CultureInvariant);

// Windows: 드라이브 경로만. 그 밖: '/'로 시작하고 '//'로 시작하지 않음. windows 인자로 두 분기를 OS와 무관하게 테스트
internal static bool IsLocalAbsolute(string path, bool windows) => windows
    ? DrivePath.IsMatch(path)
    : path.StartsWith('/') && !path.StartsWith("//", StringComparison.Ordinal);

private static string FullPath(string path)
{
    try { return Path.GetFullPath(path); }
    catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
    { throw new McpException("path must be an absolute local file path; UNC and device paths are not allowed.", ex); }
}
```

```csharp
public async Task<string> PushPackage(string? instance = null, string? path = null, string? contentBase64 = null,
    bool skipDuplicate = false, CancellationToken cancellationToken = default)
{
    NuGetInstance target = RequireWritable(instance);   // ApiKey 없으면 service index 읽기 전에 오류
    if (string.IsNullOrWhiteSpace(path) == string.IsNullOrWhiteSpace(contentBase64))
        throw new McpException("Specify exactly one of path or contentBase64.");
    byte[] bytes = !string.IsNullOrWhiteSpace(path)
        ? await ReadPackageFileAsync(path.Trim(), cancellationToken).ConfigureAwait(false)
        : DecodeBase64(contentBase64!);   // FormatException → McpException("contentBase64 is not valid Base64.")
    (string id, string version) = NuGetPackageFile.ReadIdentity(bytes);

    Uri publish = await _client.GetResourceAsync(target, NuGetResource.Publish, cancellationToken).ConfigureAwait(false);
    using MultipartFormDataContent form = new();
    ByteArrayContent file = new(bytes);
    file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
    form.Add(file, "package", "package.nupkg");
    (int status, string body, string? reason) = await _client.SendPublishAsync(target, HttpMethod.Put, publish, form, cancellationToken).ConfigureAwait(false);
    if (status == 409 && !skipDuplicate)
        throw new McpException($"NuGet '{target.Name}' package '{id}' {version} already exists (HTTP 409): "
            + $"{NuGetFeedClient.Summarize(body) is { Length: > 0 } s ? s : reason}. Use skipDuplicate to ignore.");
    return new JsonObject { ["pushed"] = new JsonObject { ["id"] = id, ["version"] = version }, ["skipped"] = status == 409 }.ToJsonString();
}
```

`using HttpRequestMessage`가 `form`도 dispose하므로 `form`의 `using`은 이중 dispose이지만 안전하다(`HttpContent.Dispose`는 멱등).

**검증**: `NuGetPackageFileTests` — 정상 nupkg(테스트에서 `ZipArchive`로 생성), `\` 구분자 루트 판정, 루트 nuspec 없음, zip 아님, 잘못된 XML, id 없음, 잘못된 version, version 정규화. `NuGetToolsTests` — path·Base64 각각 요청 URL·`PUT`·`X-NuGet-ApiKey`·multipart part 이름 `package`, 둘 다/둘 다 없음 거부, 상대 경로, `\\host\share\a.nupkg`·`\\?\C:\a.nupkg`·`\\.\C:\a.nupkg`·`//host/share/a.nupkg`·`/\host\share\a.nupkg`·`\/host/share/a.nupkg`·`\??\UNC\host\share\a.nupkg` 거부(요청 0건, Windows 전용 Tool 테스트), `IsLocalAbsolute(path, windows: true/false)` 두 분기 단위 테스트(OS 무관), `\0`이 든 경로(`GetFullPath` 예외) → `McpException`, 409 오류에 서버 사유 포함, multipart `name` 단언은 따옴표 유무 허용, `.snupkg` 거부, 없는 파일, 잘못된 Base64, ApiKey 없음(요청 0건), 201·202 성공, 400 ReasonPhrase 요약, 409 오류, 409 + `skipDuplicate` → `skipped: true`.

**선행 단위**: 단위 2

**4차 검토 반영(구현 시)**: 패키지 250MB·`.nuspec` 1MB 상한과 오류 문구는 설계 `nuget_push_package` 세부 규칙을 따른다. 409 메시지는 보간 구멍 안의 조건식을 지역 변수(`why`, 사유가 없으면 `Conflict`)로 뺀다(CS8361 방지).

## 단위 6. unlist·relist

**대상**: `NuGetTools.UnlistPackage`, `NuGetTools.RelistPackage`

**코드 초안**

```csharp
[McpServerTool(Name = "nuget_unlist_package", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
[Description("... nuget.org에서는 unlist(복구 가능, nuget_relist_package)지만 일부 사설 피드는 버전을 실제로 삭제할 수 있다. ...")]
public Task<string> UnlistPackage(string? instance = null, string? id = null, string? version = null, CancellationToken cancellationToken = default)
    => ChangeListingAsync(instance, id, version, HttpMethod.Delete, "unlisted", cancellationToken);

private async Task<string> ChangeListingAsync(string? instance, string? id, string? version, HttpMethod method, string key, CancellationToken ct)
{
    NuGetInstance target = RequireWritable(instance);
    string packageId = RequireId(id), normalized = RequireVersion(version);
    Uri publish = await _client.GetResourceAsync(target, NuGetResource.Publish, ct).ConfigureAwait(false);
    Uri uri = NuGetFeedClient.Child(publish, $"{Uri.EscapeDataString(packageId)}/{Uri.EscapeDataString(normalized)}");
    (int status, string body, string? reason) = await _client.SendPublishAsync(target, method, uri, null, ct).ConfigureAwait(false);
    if (status == 409)
        throw NuGetFeedClient.Failure(target, method, uri, 409, body, reason, null, publish: true);   // 서버 사유 보존
    return new JsonObject { [key] = new JsonObject { ["id"] = packageId, ["version"] = normalized } }.ToJsonString();
}
```

**검증**: `DELETE`·`POST` URL(`{publish}/{id}/{version}`, publish 끝 `/` 유무), ApiKey 헤더, 404 오류, 409 오류, 200·204 성공, publish 3xx 오류, ApiKey 없음(요청 0건), Tool 설명에 사설 피드 삭제 문구. 등록 테스트에서 Tool 8개 확정.

**선행 단위**: 단위 2

## 단위 7. README·패키지 메타데이터

**대상**: `README.md` — "NuGet Tool 등록" 절(설정 예시), "제공 Tool" 표 1행, `### NuGet 피드 (REST) — Tools.NuGet.NuGetTools` 절(8개 Tool 인자·결과). csproj `PackageTags`는 단위 1에서 반영.

**코드 초안**

```csharp
builder.Services.AddMcpServer()
    .WithNuGetTools(o =>
    {
        o.Instances.Add(new NuGetInstance { Name = "nuget.org", ServiceIndex = "https://api.nuget.org/v3/index.json" });
        o.Instances.Add(new NuGetInstance
        {
            Name = "internal", ServiceIndex = "https://pkgs.example.com/nuget/v3/index.json",
            ApiKey = "<api-key>", User = "<user>", Password = "<pat>",
        });
    });
```

명시 사항:
- push `path`는 MCP 서버 호스트 파일을 읽는다(허용 루트 제한 없음, Windows는 드라이브 경로만 허용해 UNC·장치 경로 거부). 네트워크 맵 드라이브와 UNC를 가리키는 링크·junction을 통한 원격 접근은 막지 않는다
- https 인스턴스에서 service index가 http publish 주소를 주면 거부한다
- unlist는 피드에 따라 삭제일 수 있다
- 버전 목록은 unlisted 포함
- 취약점·deprecation 정보 제공 여부는 service index의 `VulnerabilityInfo/6.7.0` 리소스로 판정한다. 제공하지 않는 피드는 `vulnerable`·`deprecated`가 null(정보 없음)이다
- 응답 본문 50MB 상한
- 리다이렉트를 따라가지 않는다(http→https 리다이렉트 피드는 https 주소로 등록)
- http ServiceIndex에 자격 증명을 쓰면 평문 전송된다
- ServiceIndex URL에 비밀(키)을 넣지 않는다(결과·오류 메시지에 노출)
- Basic은 ServiceIndex와 같은 origin에만 보낸다
- ApiKey는 service index의 PackagePublish 주소로 보낸다(service index를 신뢰한다는 전제)
- service index는 프로세스 수명 동안 캐시한다(주소가 바뀌면 재시작)
- ApiKey·Password KMS 암호화는 호스트 책임

**검증**: 문서 내 Tool 이름·인자가 코드와 일치(수동 대조), 전체 `dotnet test` 통과.

**선행 단위**: 단위 3~6

## 순서

1 → 2 → 3 → 4 → 5 → 6 → 7. 단위마다 `dotnet build`와 `dotnet test`(해당 테스트 클래스 + 전체)를 통과한 뒤 다음 단위로 간다.
