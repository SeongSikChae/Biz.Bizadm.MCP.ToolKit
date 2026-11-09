using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.GitHub;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Biz.Bizadm.MCP.ToolKit.Tools.GitHub;

/// <summary>GitHub REST API로 릴리스를 만들고, 고치고, 지우며 asset을 올리고 지우는 Tool. GitHub MCP Server가 노출하지 않는 기능이다.</summary>
[McpServerToolType]
public sealed class GitHubReleaseTools
{
    private const string InstanceDescription = "GitHub 인스턴스 이름(필수). github_list_instances로 확인";
    private const string OwnerDescription = "저장소 소유자(필수, 사용자 또는 조직)";
    private const string RepoDescription = "저장소 이름(필수)";
    private const string ReleaseIdDescription = "릴리스 id(정수). tag와 둘 중 하나만 준다. 초안(draft) 릴리스는 releaseId로만 지정할 수 있다";
    private const string TagDescription = "릴리스의 태그 이름. releaseId와 둘 중 하나만 준다. 초안(draft) 릴리스는 찾지 못한다";

    private readonly GitHubRestClient _client;

    /// <summary>DI에 등록한 GitHub REST client로 Tool을 만든다. <see cref="Microsoft.Extensions.DependencyInjection.GitHubMcpServerBuilderExtensions.WithGitHubTools"/>로 등록해야 한다.</summary>
    /// <param name="services">서비스 공급자</param>
    public GitHubReleaseTools(IServiceProvider services)
        : this(services.GetRequiredService<GitHubRestClient>())
    {
    }

    internal GitHubReleaseTools(GitHubRestClient client)
    {
        _client = client;
    }

    /// <summary>릴리스를 만든다.</summary>
    [McpServerTool(Name = "github_create_release", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = true)]
    [Description("지정한 GitHub 인스턴스의 저장소에 릴리스를 만들고 GitHub 응답(릴리스)을 그대로 반환한다. tagName의 태그가 없으면 targetCommitish 기준으로 만든다(초안은 공개할 때 만든다). Contents 쓰기 권한이 필요하다.")]
    public async Task<string> CreateRelease(
        [Description(InstanceDescription)] string? instance = null,
        [Description(OwnerDescription)] string? owner = null,
        [Description(RepoDescription)] string? repo = null,
        [Description("릴리스 태그 이름(필수, 예: v1.4.0)")] string? tagName = null,
        [Description("태그가 없을 때 만들 기준(브랜치 이름 또는 커밋 sha). 생략하면 기본 브랜치")] string? targetCommitish = null,
        [Description("릴리스 제목")] string? name = null,
        [Description("릴리스 본문(Markdown)")] string? body = null,
        [Description("true면 초안")] bool draft = false,
        [Description("true면 사전 릴리스")] bool prerelease = false,
        [Description("true면 GitHub가 릴리스 노트를 자동 생성")] bool generateReleaseNotes = false,
        CancellationToken cancellationToken = default)
    {
        (GitHubInstance target, string repoPath) = Target(instance, owner, repo);
        JsonObject json = new() { ["tag_name"] = Required(tagName, nameof(tagName)) };
        AddIfPresent(json, "target_commitish", targetCommitish);
        AddIfPresent(json, "name", name);
        if (body is not null)
            json["body"] = body;
        json["draft"] = draft;
        json["prerelease"] = prerelease;
        json["generate_release_notes"] = generateReleaseNotes;
        return await SendJsonAsync(target, HttpMethod.Post, repoPath + "releases", json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>릴리스를 고친다. 준 값만 바꾼다.</summary>
    [McpServerTool(Name = "github_update_release", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
    [Description("지정한 GitHub 인스턴스의 저장소에서 릴리스를 고치고 GitHub 응답(릴리스)을 그대로 반환한다. 준 값만 바꾸며 하나는 있어야 한다. draft=false로 초안을 공개한다. Contents 쓰기 권한이 필요하다.")]
    public async Task<string> UpdateRelease(
        [Description(InstanceDescription)] string? instance = null,
        [Description(OwnerDescription)] string? owner = null,
        [Description(RepoDescription)] string? repo = null,
        [Description(ReleaseIdDescription)] long? releaseId = null,
        [Description(TagDescription)] string? tag = null,
        [Description("새 태그 이름")] string? tagName = null,
        [Description("새 기준(브랜치 이름 또는 커밋 sha)")] string? targetCommitish = null,
        [Description("새 제목")] string? name = null,
        [Description("새 본문(Markdown)")] string? body = null,
        [Description("초안 여부")] bool? draft = null,
        [Description("사전 릴리스 여부")] bool? prerelease = null,
        CancellationToken cancellationToken = default)
    {
        (GitHubInstance target, string repoPath) = Target(instance, owner, repo);
        JsonObject json = [];
        AddIfPresent(json, "tag_name", tagName);
        AddIfPresent(json, "target_commitish", targetCommitish);
        AddIfPresent(json, "name", name);
        if (body is not null)
            json["body"] = body;
        if (draft is { } d)
            json["draft"] = d;
        if (prerelease is { } p)
            json["prerelease"] = p;
        ReleaseKey key = Key(releaseId, tag);
        if (json.Count == 0)
            throw new McpException("at least one field to update is required.");
        (long id, _) = await FindReleaseAsync(target, repoPath, key, needUploadUrl: false, cancellationToken).ConfigureAwait(false);
        return await SendJsonAsync(target, HttpMethod.Patch, $"{repoPath}releases/{id}", json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>릴리스를 지운다. 태그는 남는다.</summary>
    [McpServerTool(Name = "github_delete_release", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
    [Description("지정한 GitHub 인스턴스의 저장소에서 릴리스를 지우고 {deleted:{releaseId}}를 반환한다. 태그는 남으며 github_delete_ref로 지운다. Contents 쓰기 권한이 필요하다.")]
    public async Task<string> DeleteRelease(
        [Description(InstanceDescription)] string? instance = null,
        [Description(OwnerDescription)] string? owner = null,
        [Description(RepoDescription)] string? repo = null,
        [Description(ReleaseIdDescription)] long? releaseId = null,
        [Description(TagDescription)] string? tag = null,
        CancellationToken cancellationToken = default)
    {
        (GitHubInstance target, string repoPath) = Target(instance, owner, repo);
        ReleaseKey key = Key(releaseId, tag);
        (long id, _) = await FindReleaseAsync(target, repoPath, key, needUploadUrl: false, cancellationToken).ConfigureAwait(false);
        await _client.SendAsync(target, HttpMethod.Delete, $"{repoPath}releases/{id}", null, cancellationToken).ConfigureAwait(false);
        return new JsonObject { ["deleted"] = new JsonObject { ["releaseId"] = id } }.ToJsonString();
    }

    /// <summary>asset 크기 상한(디코딩 후 바이트)</summary>
    internal const int MaxAssetBytes = 10 * 1024 * 1024;

    /// <summary>릴리스에 asset을 올린다.</summary>
    [McpServerTool(Name = "github_upload_release_asset", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = true)]
    [Description("지정한 GitHub 인스턴스의 저장소 릴리스에 asset을 올리고 GitHub 응답(asset)을 그대로 반환한다. 파일 내용은 base64로 주며 디코딩 후 10 MiB(10485760 바이트)까지다. 같은 이름의 asset이 있으면 GitHub가 거부한다. Contents 쓰기 권한이 필요하다.")]
    public async Task<string> UploadReleaseAsset(
        [Description(InstanceDescription)] string? instance = null,
        [Description(OwnerDescription)] string? owner = null,
        [Description(RepoDescription)] string? repo = null,
        [Description(ReleaseIdDescription)] long? releaseId = null,
        [Description(TagDescription)] string? tag = null,
        [Description("asset 파일 이름(필수, 예: app-1.4.0.zip)")] string? name = null,
        [Description("파일 내용 base64(필수). 디코딩 후 10 MiB 이하")] string? contentBase64 = null,
        [Description("MIME 형식. 생략하면 application/octet-stream")] string? contentType = null,
        [Description("asset 표시 이름(선택)")] string? label = null,
        CancellationToken cancellationToken = default)
    {
        (GitHubInstance target, string repoPath) = Target(instance, owner, repo);
        ReleaseKey key = Key(releaseId, tag);
        string fileName = Required(name, nameof(name));
        MediaTypeHeaderValue mediaType = ParseContentType(contentType);
        byte[] data = DecodeAsset(Required(contentBase64, nameof(contentBase64)));

        (_, string? uploadUrl) = await FindReleaseAsync(target, repoPath, key, needUploadUrl: true, cancellationToken).ConfigureAwait(false);
        Uri uri = GitHubRestClient.ResolveUploadUri(target, uploadUrl, fileName, string.IsNullOrWhiteSpace(label) ? null : label.Trim());
        ByteArrayContent content = new(data);
        content.Headers.ContentType = mediaType;
        return await _client.SendAsync(target, HttpMethod.Post, uri, uri.AbsolutePath, content, cancellationToken).ConfigureAwait(false)
            ?? throw new McpException($"GitHub '{target.Name}' POST {uri.AbsolutePath} returned no content.");
    }

    internal static MediaTypeHeaderValue ParseContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return new MediaTypeHeaderValue("application/octet-stream");
        return MediaTypeHeaderValue.TryParse(contentType.Trim(), out MediaTypeHeaderValue? parsed) && parsed.MediaType?.Contains('/') == true
            ? parsed
            : throw new McpException("contentType is not a valid media type.");
    }

    /// <summary>base64를 디코딩한다. 디코딩 전에 길이로 상한을 먼저 확인해 큰 입력을 디코딩하지 않는다.</summary>
    internal static byte[] DecodeAsset(string base64)
    {
        long maxChars = ((long)MaxAssetBytes + 2) / 3 * 4;
        if (base64.Count(c => !char.IsWhiteSpace(c)) > maxChars)
            throw new McpException($"contentBase64 exceeds {MaxAssetBytes} bytes.");
        byte[] data;
        try
        {
            data = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            throw new McpException("contentBase64 is not valid base64.");
        }
        if (data.Length == 0)
            throw new McpException("contentBase64 is empty.");
        if (data.Length > MaxAssetBytes)
            throw new McpException($"contentBase64 exceeds {MaxAssetBytes} bytes.");
        return data;
    }

    /// <summary>릴리스 asset을 지운다.</summary>
    [McpServerTool(Name = "github_delete_release_asset", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
    [Description("지정한 GitHub 인스턴스의 저장소에서 릴리스 asset을 지우고 {deleted:{assetId}}를 반환한다. Contents 쓰기 권한이 필요하다.")]
    public async Task<string> DeleteReleaseAsset(
        [Description(InstanceDescription)] string? instance = null,
        [Description(OwnerDescription)] string? owner = null,
        [Description(RepoDescription)] string? repo = null,
        [Description("asset id(정수, 필수). 릴리스 응답의 assets[].id")] long? assetId = null,
        CancellationToken cancellationToken = default)
    {
        (GitHubInstance target, string repoPath) = Target(instance, owner, repo);
        long id = assetId is { } value && value > 0 ? value : throw new McpException("assetId must be a positive integer.");
        await _client.SendAsync(target, HttpMethod.Delete, $"{repoPath}releases/assets/{id}", null, cancellationToken).ConfigureAwait(false);
        return new JsonObject { ["deleted"] = new JsonObject { ["assetId"] = id } }.ToJsonString();
    }

    private readonly record struct ReleaseKey(long? Id, string? Tag);

    /// <summary>releaseId(양수) 또는 tag 중 정확히 하나</summary>
    private static ReleaseKey Key(long? releaseId, string? tag)
    {
        bool hasTag = !string.IsNullOrWhiteSpace(tag);
        if (releaseId.HasValue == hasTag)
            throw new McpException("exactly one of releaseId or tag is required.");
        if (releaseId is <= 0)
            throw new McpException("releaseId must be a positive integer.");
        // tag는 release/1.0처럼 /를 가질 수 있으므로 git/refs와 같이 /는 구분자로 두고 세그먼트마다 인코딩한다
        return hasTag
            ? new ReleaseKey(null, string.Join('/', tag!.Trim().Split('/').Select(s => GitHubRestClient.PathSegment(s, "tag"))))
            : new ReleaseKey(releaseId, null);
    }

    /// <summary>릴리스 id와 upload_url. releaseId로 지정하고 upload_url이 필요 없으면 조회하지 않는다.</summary>
    private async Task<(long Id, string? UploadUrl)> FindReleaseAsync(GitHubInstance target, string repoPath, ReleaseKey key, bool needUploadUrl, CancellationToken cancellationToken)
    {
        if (key.Id is { } id && !needUploadUrl)
            return (id, null);
        string path = key.Id is { } known ? $"{repoPath}releases/{known}" : $"{repoPath}releases/tags/{key.Tag}";
        string json = await SendJsonAsync(target, HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);
        try
        {
            if (JsonNode.Parse(json) is JsonObject release && release["id"] is JsonValue value && value.TryGetValue(out long found))
            {
                // 문자열이 아니면 null로 두고 ResolveUploadUri가 "no valid upload_url"로 알린다
                string? uploadUrl = release["upload_url"] is JsonValue url && url.TryGetValue(out string? text) ? text : null;
                return (found, uploadUrl);
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            // 아래에서 같은 오류로 처리한다
        }
        throw new McpException($"GitHub '{target.Name}' GET {path} response has no release id.");
    }

    private (GitHubInstance Target, string RepoPath) Target(string? instance, string? owner, string? repo)
    {
        GitHubInstance target = _client.Resolve(instance);
        return (target, GitHubRestClient.RepoPath(Required(owner, nameof(owner)), Required(repo, nameof(repo))));
    }

    private async Task<string> SendJsonAsync(GitHubInstance target, HttpMethod method, string path, JsonObject? body, CancellationToken cancellationToken)
        => await _client.SendAsync(target, method, path, body, cancellationToken).ConfigureAwait(false)
            ?? throw new McpException($"GitHub '{target.Name}' {method} {path} returned no content.");

    private static void AddIfPresent(JsonObject json, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            json[key] = value.Trim();
    }

    private static string Required(string? value, string name)
        => string.IsNullOrWhiteSpace(value) ? throw new McpException($"{name} is required.") : value.Trim();
}
