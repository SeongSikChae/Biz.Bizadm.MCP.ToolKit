using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.GitHub;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Biz.Bizadm.MCP.ToolKit.Tools.GitHub;

/// <summary>GitHub REST API로 태그·ref를 만들고, 옮기고, 지우는 Tool. GitHub MCP Server가 노출하지 않는 기능이다.</summary>
[McpServerToolType]
public sealed class GitHubRefTools
{
    private const string InstanceDescription = "GitHub 인스턴스 이름(필수). github_list_instances로 확인";
    private const string OwnerDescription = "저장소 소유자(필수, 사용자 또는 조직)";
    private const string RepoDescription = "저장소 이름(필수)";

    private readonly GitHubRestClient _client;

    /// <summary>DI에 등록한 GitHub REST client로 Tool을 만든다. <see cref="Microsoft.Extensions.DependencyInjection.GitHubMcpServerBuilderExtensions.WithGitHubTools"/>로 등록해야 한다.</summary>
    /// <param name="services">서비스 공급자</param>
    public GitHubRefTools(IServiceProvider services)
        : this(services.GetRequiredService<GitHubRestClient>())
    {
    }

    internal GitHubRefTools(GitHubRestClient client)
    {
        _client = client;
    }

    /// <summary>커밋을 가리키는 가벼운 태그를 만든다.</summary>
    [McpServerTool(Name = "github_create_lightweight_tag", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = true)]
    [Description("지정한 GitHub 인스턴스의 저장소에 커밋을 가리키는 가벼운 태그(refs/tags/{tag})를 만들고 GitHub 응답(ref)을 그대로 반환한다. Contents 쓰기 권한이 필요하다.")]
    public async Task<string> CreateLightweightTag(
        [Description(InstanceDescription)] string? instance = null,
        [Description(OwnerDescription)] string? owner = null,
        [Description(RepoDescription)] string? repo = null,
        [Description("태그 이름(필수, 예: v1.0.0). refs/tags/는 붙이지 않는다")] string? tag = null,
        [Description("태그가 가리킬 커밋 sha(필수)")] string? sha = null,
        CancellationToken cancellationToken = default)
    {
        (GitHubInstance target, string repoPath) = Target(instance, owner, repo);
        string name = Required(tag, nameof(tag));
        string commit = Required(sha, nameof(sha));
        return await SendJsonAsync(target, HttpMethod.Post, repoPath + "git/refs",
            new JsonObject { ["ref"] = $"refs/tags/{name}", ["sha"] = commit }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>태그 객체(메시지·tagger)를 만들고 그 객체를 가리키는 태그 ref를 만든다.</summary>
    [McpServerTool(Name = "github_create_annotated_tag", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = true)]
    [Description("지정한 GitHub 인스턴스의 저장소에 커밋을 가리키는 주석 태그를 만든다. 태그 객체(git/tags)를 만든 뒤 refs/tags/{tag}를 만들고 {tag, ref} 두 GitHub 응답을 반환한다. Contents 쓰기 권한이 필요하다.")]
    public async Task<string> CreateAnnotatedTag(
        [Description(InstanceDescription)] string? instance = null,
        [Description(OwnerDescription)] string? owner = null,
        [Description(RepoDescription)] string? repo = null,
        [Description("태그 이름(필수, 예: v1.0.0). refs/tags/는 붙이지 않는다")] string? tag = null,
        [Description("태그가 가리킬 커밋 sha(필수). 커밋이 아닌 객체는 지원하지 않는다")] string? sha = null,
        [Description("태그 메시지(필수)")] string? message = null,
        [Description("tagger 이름(선택). taggerEmail과 함께 주며, 둘 다 생략하면 토큰 사용자로 채워진다")] string? taggerName = null,
        [Description("tagger 이메일(선택). taggerName과 함께 준다")] string? taggerEmail = null,
        CancellationToken cancellationToken = default)
    {
        (GitHubInstance target, string repoPath) = Target(instance, owner, repo);
        string name = Required(tag, nameof(tag));
        string commit = Required(sha, nameof(sha));
        string text = Required(message, nameof(message));
        bool hasName = !string.IsNullOrWhiteSpace(taggerName);
        bool hasEmail = !string.IsNullOrWhiteSpace(taggerEmail);
        if (hasName != hasEmail)
            throw new McpException("taggerName and taggerEmail must be given together.");

        JsonObject tagBody = new() { ["tag"] = name, ["message"] = text, ["object"] = commit, ["type"] = "commit" };
        if (hasName)
            tagBody["tagger"] = new JsonObject { ["name"] = taggerName!.Trim(), ["email"] = taggerEmail!.Trim() };
        string tagJson = await SendJsonAsync(target, HttpMethod.Post, repoPath + "git/tags", tagBody, cancellationToken).ConfigureAwait(false);
        string objectSha = TagObjectSha(target, tagJson);

        string refJson;
        try
        {
            refJson = await SendJsonAsync(target, HttpMethod.Post, repoPath + "git/refs",
                new JsonObject { ["ref"] = $"refs/tags/{name}", ["sha"] = objectSha }, cancellationToken).ConfigureAwait(false);
        }
        catch (McpException ex)
        {
            // 태그 객체는 참조 없이 남는다. 다시 시도하거나 정리할 수 있게 sha를 알린다
            throw new McpException($"{ex.Message} (tag object {objectSha} was created without a ref)", ex);
        }
        return new JsonObject { ["tag"] = JsonNode.Parse(tagJson), ["ref"] = JsonNode.Parse(refJson) }.ToJsonString();
    }

    /// <summary>ref가 가리키는 커밋을 바꾼다.</summary>
    [McpServerTool(Name = "github_update_ref", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
    [Description("지정한 GitHub 인스턴스의 저장소에서 ref(태그 tags/{tag}, 브랜치 heads/{branch})가 가리키는 sha를 바꾸고 GitHub 응답(ref)을 그대로 반환한다. 빨리 감기가 아니면 force가 필요하다. Contents 쓰기 권한이 필요하다.")]
    public async Task<string> UpdateRef(
        [Description(InstanceDescription)] string? instance = null,
        [Description(OwnerDescription)] string? owner = null,
        [Description(RepoDescription)] string? repo = null,
        [Description("바꿀 ref(필수). 예: tags/v1.0.0, heads/main. 앞의 refs/는 있어도 된다")] string? @ref = null,
        [Description("새 sha(필수)")] string? sha = null,
        [Description("true면 빨리 감기가 아니어도 강제로 바꾼다")] bool force = false,
        CancellationToken cancellationToken = default)
    {
        (GitHubInstance target, string repoPath) = Target(instance, owner, repo);
        string reference = Required(@ref, "ref");
        string commit = Required(sha, nameof(sha));
        return await SendJsonAsync(target, HttpMethod.Patch, repoPath + "git/refs/" + GitHubRestClient.RefPath(reference),
            new JsonObject { ["sha"] = commit, ["force"] = force }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>ref를 지운다. 태그 삭제도 이 Tool로 한다.</summary>
    [McpServerTool(Name = "github_delete_ref", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
    [Description("지정한 GitHub 인스턴스의 저장소에서 ref(태그 tags/{tag}, 브랜치 heads/{branch})를 지우고 {deleted}를 반환한다. 태그 삭제도 이 Tool을 쓴다(주석 태그도 ref만 지운다). Contents 쓰기 권한이 필요하다.")]
    public async Task<string> DeleteRef(
        [Description(InstanceDescription)] string? instance = null,
        [Description(OwnerDescription)] string? owner = null,
        [Description(RepoDescription)] string? repo = null,
        [Description("지울 ref(필수). 예: tags/v1.0.0, heads/feature. 앞의 refs/는 있어도 된다")] string? @ref = null,
        CancellationToken cancellationToken = default)
    {
        (GitHubInstance target, string repoPath) = Target(instance, owner, repo);
        string reference = Required(@ref, "ref");
        await _client.SendAsync(target, HttpMethod.Delete, repoPath + "git/refs/" + GitHubRestClient.RefPath(reference), null, cancellationToken).ConfigureAwait(false);
        return new JsonObject { ["deleted"] = "refs/" + GitHubRestClient.NormalizeRef(reference) }.ToJsonString();
    }

    private (GitHubInstance Target, string RepoPath) Target(string? instance, string? owner, string? repo)
    {
        GitHubInstance target = _client.Resolve(instance);
        return (target, GitHubRestClient.RepoPath(Required(owner, nameof(owner)), Required(repo, nameof(repo))));
    }

    private async Task<string> SendJsonAsync(GitHubInstance target, HttpMethod method, string path, JsonObject body, CancellationToken cancellationToken)
        => await _client.SendAsync(target, method, path, body, cancellationToken).ConfigureAwait(false)
            ?? throw new McpException($"GitHub '{target.Name}' {method} {path} returned no content.");

    private static string TagObjectSha(GitHubInstance target, string tagJson)
    {
        try
        {
            if (JsonNode.Parse(tagJson)?["sha"] is JsonValue value && value.TryGetValue(out string? sha) && !string.IsNullOrEmpty(sha))
                return sha;
        }
        catch (JsonException)
        {
            // 아래에서 같은 오류로 처리한다
        }
        throw new McpException($"GitHub '{target.Name}' git/tags response has no sha.");
    }

    private static string Required(string? value, string name)
        => string.IsNullOrWhiteSpace(value) ? throw new McpException($"{name} is required.") : value.Trim();
}
