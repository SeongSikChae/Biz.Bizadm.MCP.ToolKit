---
status: done
category: github
feature: github-release-tools
phase: 개발
---

# github-release-tools 개발

선행: `0008-github-release-tools-방안.md`, `0009-github-release-tools-설계.md`

경로 기준: `src/Biz.Bizadm.MCP.ToolKit/`(src), `tests/Biz.Bizadm.MCP.ToolKit.Tests/`(tests)

재사용: `GitHubRestClient`(오류 형식·헤더·경로 세그먼트), `GitHubRecordingHandler`, `GitHubRestClientTests.Options()`, `GitHubRefTools`의 `Target`/`Required` 패턴.

## 단위 1. GitHubRestClient 확장

- **대상**: src `GitHub/GitHubRestClient.cs`, tests `GitHub/GitHubRestClientTests.cs`
- **코드 초안**:

```csharp
// 기존 JSON 호출은 절대 URI 호출로 위임한다(동작·메시지 동일)
public Task<string?> SendAsync(GitHubInstance instance, HttpMethod method, string path, JsonObject? body, CancellationToken ct)
    => SendAsync(instance, method, new Uri(new Uri(instance.ApiEndpoint), path), path,
        body is null ? null : new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"), ct);

/// <param name="displayPath">오류 메시지에 쓰는 경로</param>
public async Task<string?> SendAsync(GitHubInstance instance, HttpMethod method, Uri uri, string displayPath, HttpContent? content, CancellationToken ct)
{
    using HttpRequestMessage request = new(method, uri) { Content = content };
    // ... existing 헤더, 전송 예외 감싸기, 오류 상태 처리 (path → displayPath) ...
}

/// <summary>upload_url 템플릿에서 업로드 URI를 만든다. 호스트가 ApiEndpoint와 맞지 않으면 McpException</summary>
internal static Uri ResolveUploadUri(GitHubInstance instance, string? uploadUrlTemplate, string name, string? label)
{
    int brace = uploadUrlTemplate?.IndexOf('{') ?? -1;
    string raw = brace >= 0 ? uploadUrlTemplate![..brace] : uploadUrlTemplate ?? string.Empty;
    if (!Uri.TryCreate(raw, UriKind.Absolute, out Uri? upload))
        throw new McpException($"GitHub '{instance.Name}' release has no valid upload_url.");
    Uri api = new(instance.ApiEndpoint);
    bool sameHost = string.Equals(upload.Host, api.Host, StringComparison.OrdinalIgnoreCase)
        || (api.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) && upload.Host.Equals("uploads.github.com", StringComparison.OrdinalIgnoreCase));
    if (!sameHost || upload.Scheme != api.Scheme || upload.Port != api.Port)
        throw new McpException($"GitHub '{instance.Name}' upload_url host '{upload.Host}' does not match ApiEndpoint.");
    string query = "?name=" + Uri.EscapeDataString(name) + (string.IsNullOrEmpty(label) ? "" : "&label=" + Uri.EscapeDataString(label));
    return new Uri(upload.GetLeftPart(UriPartial.Path) + query);
}
```

- **검증**: 기존 `GitHubRestClientTests`·`GitHubRefToolsTests` 그대로 통과(메시지 불변). `ResolveUploadUri`: github.com 템플릿 → `https://uploads.github.com/repos/o/r/releases/1/assets?name=a%20b.zip&label=L`, Enterprise 같은 호스트 허용, 다른 호스트·http↔https·다른 port 거부, 템플릿 없음·상대 URI 거부, 메시지에 토큰 없음
- **선행 단위**: 없음

## 단위 2. 릴리스 생성·수정·삭제, asset 삭제, 등록

- **대상**: src `Tools/GitHub/GitHubReleaseTools.cs`(신규), `GitHubMcpServerBuilderExtensions.cs`, tests `GitHub/GitHubReleaseToolsTests.cs`(신규), `GitHub/GitHubRegistrationTests.cs`(12개)
- **코드 초안**:

```csharp
[McpServerToolType]
public sealed class GitHubReleaseTools
{
    [McpServerTool(Name = "github_create_release", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = true)]
    public async Task<string> CreateRelease(string? instance = null, string? owner = null, string? repo = null,
        string? tagName = null, string? targetCommitish = null, string? name = null, string? body = null,
        bool draft = false, bool prerelease = false, bool generateReleaseNotes = false, CancellationToken cancellationToken = default)
    {
        (GitHubInstance target, string repoPath) = Target(instance, owner, repo);
        JsonObject json = new() { ["tag_name"] = Required(tagName, nameof(tagName)), ["draft"] = draft, ["prerelease"] = prerelease, ["generate_release_notes"] = generateReleaseNotes };
        AddIfPresent(json, "target_commitish", targetCommitish);
        AddIfPresent(json, "name", name);
        AddIfPresent(json, "body", body);   // body는 trim하지 않는다
        return await SendJsonAsync(target, HttpMethod.Post, repoPath + "releases", json, cancellationToken);
    }

    [McpServerTool(Name = "github_update_release", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
    public async Task<string> UpdateRelease(/* ..., long? releaseId = null, string? tag = null, string? tagName, targetCommitish, name, body, bool? draft = null, bool? prerelease = null */)
    {
        // 준 값만 넣고, 하나도 없으면 "at least one field to update is required."
        long id = (await FindReleaseAsync(target, repoPath, releaseId, tag, ct)).Id;
        return await SendJsonAsync(target, HttpMethod.Patch, $"{repoPath}releases/{id}", json, ct);
    }

    [McpServerTool(Name = "github_delete_release", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
    public async Task<string> DeleteRelease(/* ..., long? releaseId, string? tag */)
    {
        long id = (await FindReleaseAsync(...)).Id;
        await _client.SendAsync(target, HttpMethod.Delete, $"{repoPath}releases/{id}", null, ct);
        return new JsonObject { ["deleted"] = new JsonObject { ["releaseId"] = id } }.ToJsonString();
    }

    [McpServerTool(Name = "github_delete_release_asset", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
    public async Task<string> DeleteReleaseAsset(/* ..., long? assetId */) { /* DELETE releases/assets/{id} → {"deleted":{"assetId":N}} */ }

    // releaseId(양수) 또는 tag 중 정확히 하나. releaseId면 조회하지 않고(업로드만 조회), tag면 GET releases/tags/{tag}
    private async Task<(long Id, string? UploadUrl)> FindReleaseAsync(GitHubInstance target, string repoPath, long? releaseId, string? tag, bool needUploadUrl, CancellationToken ct)
    {
        bool hasTag = !string.IsNullOrWhiteSpace(tag);
        if (releaseId.HasValue == hasTag)
            throw new McpException("exactly one of releaseId or tag is required.");
        if (releaseId is { } id)
        {
            if (id <= 0) throw new McpException("releaseId must be a positive integer.");
            if (!needUploadUrl) return (id, null);
            return Parse(await SendJsonAsync(target, HttpMethod.Get, $"{repoPath}releases/{id}", null, ct));
        }
        return Parse(await SendJsonAsync(target, HttpMethod.Get, $"{repoPath}releases/tags/{GitHubRestClient.PathSegment(tag!.Trim(), "tag")}", null, ct));
        // Parse: "id"(long)와 "upload_url"을 읽고, id가 없으면 McpException
    }
}
```

```csharp
// GitHubRestClient: 단일 세그먼트 인코딩을 tag에도 쓰도록 internal로 노출
internal static string PathSegment(string value, string name) => Segment(value, name);
// WithGitHubTools
builder.WithTools<GitHubReleaseTools>();
```

- `SendJsonAsync`의 본문이 null인 GET을 허용하도록 시그니처를 `JsonObject?`로 둔다(본문 없는 응답은 기존처럼 "returned no content" 오류).
- **검증**: 등록 12개·주석·스키마 인자(`releaseId`, `tag`, `tagName` 등), create 본문(선택 필드 생략), update는 준 필드만·빈 변경 오류, tag 지정 시 `GET releases/tags/{tag}` 후 id 사용, delete 결과 `{"deleted":{"releaseId":N}}`, asset 삭제, `releaseId`·`tag` 둘 다/없음·음수 → 요청 없음
- **선행 단위**: 1

## 단위 3. asset 업로드

- **대상**: src `Tools/GitHub/GitHubReleaseTools.cs`, tests `GitHub/GitHubReleaseToolsTests.cs`
- **코드 초안**:

```csharp
internal const int MaxAssetBytes = 10 * 1024 * 1024;

[McpServerTool(Name = "github_upload_release_asset", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = true)]
public async Task<string> UploadReleaseAsset(/* ..., long? releaseId, string? tag, string? name, string? contentBase64, string? contentType, string? label */)
{
    (GitHubInstance target, string repoPath) = Target(instance, owner, repo);
    string fileName = Required(name, nameof(name));
    byte[] data = DecodeAsset(Required(contentBase64, nameof(contentBase64)));   // 길이 선검사 → FromBase64String → 0바이트·상한 검사
    (_, string? uploadUrl) = await FindReleaseAsync(target, repoPath, releaseId, tag, needUploadUrl: true, ct);
    Uri uri = GitHubRestClient.ResolveUploadUri(target, uploadUrl, fileName, label?.Trim());
    ByteArrayContent content = new(data);
    content.Headers.ContentType = MediaTypeHeaderValue.Parse(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim());
    return await _client.SendAsync(target, HttpMethod.Post, uri, uri.AbsolutePath, content, ct)
        ?? throw new McpException($"GitHub '{target.Name}' POST {uri.AbsolutePath} returned no content.");
}

internal static byte[] DecodeAsset(string base64)
{
    // base64 4문자 = 3바이트. 공백 제외 길이로 상한을 먼저 본다
    long maxChars = ((long)MaxAssetBytes + 2) / 3 * 4;
    if (base64.Count(c => !char.IsWhiteSpace(c)) > maxChars) throw new McpException($"contentBase64 exceeds {MaxAssetBytes} bytes.");
    byte[] data;
    try { data = Convert.FromBase64String(base64); }
    catch (FormatException) { throw new McpException("contentBase64 is not valid base64."); }
    if (data.Length == 0) throw new McpException("contentBase64 is empty.");
    if (data.Length > MaxAssetBytes) throw new McpException($"contentBase64 exceeds {MaxAssetBytes} bytes.");
    return data;
}
```

- `contentType`이 잘못된 형식이면 `McpException("contentType is not a valid media type.")`.
- **검증**: releaseId 지정 → `GET releases/{id}` 후 uploads 호스트로 `POST ?name=&label=`, 본문 바이트·`Content-Type` 일치, tag 지정 → 조회 1회만, 잘못된 base64·0바이트·10 MiB 초과(경계 10,485,760 허용 / +1 거부)·잘못된 contentType → 업로드 요청 없음, upload_url 호스트 불일치 → 업로드 요청 없음
- **선행 단위**: 2

## 단위 4. ReadMe·실제 확인

- **대상**: `ReadMe.md`(제공 Tool 표, GitHub 릴리스 절)
- **검증**:
  - `dotnet build`·`dotnet test` 전체 통과
  - 테스트 도구로 실제 GitHub(`SeongSikChae/Biz.Bizadm.MCP.ToolKit`): 초안 릴리스 생성(`probe-rel-*`, target `main`) → 작은 asset 업로드 → asset 삭제 → 릴리스 수정(name) → 릴리스 삭제 → 생긴 태그가 있으면 `github_delete_ref`로 삭제. 끝에 릴리스·태그 잔여 없음 확인. 실패해도 만든 것을 정리한다
- **선행 단위**: 3

## 순서

1 → 2 → 3 → 4. 단위별 구현·검증. 이후 버전 1.4.0(별도 단위, 이슈 #1·#2 공통)으로 올리고 Release pack한다.
