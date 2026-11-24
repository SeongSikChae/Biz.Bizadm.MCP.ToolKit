using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.GitHub;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Biz.Bizadm.MCP.ToolKit.Tools.GitHub;

/// <summary>GitHub REST API로 마일스톤을 조회하고 만들고, 고치고, 지우는 Tool. GitHub MCP Server가 노출하지 않는 기능이다.</summary>
[McpServerToolType]
public sealed class GitHubMilestoneTools
{
    private const string InstanceDescription = "GitHub 인스턴스 이름(필수). github_list_instances로 확인";
    private const string OwnerDescription = "저장소 소유자(필수, 사용자 또는 조직)";
    private const string RepoDescription = "저장소 이름(필수)";
    private const string NumberDescription = "마일스톤 번호(필수, 양수). 응답의 number이며 id가 아니다";
    private const string PermissionText = "저장소 권한 Issues·Pull requests가 필요하다(읽기 또는 쓰기, 둘 다 필요한지는 미확인).";

    private static readonly string[] States = ["open", "closed", "all"];
    private static readonly string[] WriteStates = ["open", "closed"];
    private static readonly string[] Sorts = ["due_on", "completeness"];
    private static readonly string[] Directions = ["asc", "desc"];

    private readonly GitHubRestClient _client;

    /// <summary>DI에 등록한 GitHub REST client로 Tool을 만든다. <see cref="Microsoft.Extensions.DependencyInjection.GitHubMcpServerBuilderExtensions.WithGitHubTools"/>로 등록해야 한다.</summary>
    /// <param name="services">서비스 공급자</param>
    public GitHubMilestoneTools(IServiceProvider services)
        : this(services.GetRequiredService<GitHubRestClient>())
    {
    }

    internal GitHubMilestoneTools(GitHubRestClient client)
    {
        _client = client;
    }

    /// <summary>마일스톤 목록을 조회한다.</summary>
    [McpServerTool(Name = "github_list_milestones", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("지정한 GitHub 인스턴스의 저장소 마일스톤 목록(GET milestones)을 GitHub 응답(마일스톤 배열) 그대로 반환한다. number·title·state·description·due_on·open_issues·closed_issues 등이 담긴다. 한 페이지씩 조회하며 page로 넘긴다. " + PermissionText)]
    public async Task<string> ListMilestones(
        [Description(InstanceDescription)] string? instance = null,
        [Description(OwnerDescription)] string? owner = null,
        [Description(RepoDescription)] string? repo = null,
        [Description("open / closed / all(소문자). 생략하면 GitHub 기본(open)")] string? state = null,
        [Description("due_on / completeness(소문자). 생략하면 GitHub 기본(due_on)")] string? sort = null,
        [Description("asc / desc(소문자). 생략하면 GitHub 기본(asc)")] string? direction = null,
        [Description("페이지당 개수(1~100). 생략하면 GitHub 기본(30)")] int? perPage = null,
        [Description("페이지 번호(1 이상). 생략하면 1")] int? page = null,
        CancellationToken cancellationToken = default)
    {
        (GitHubInstance target, string repoPath) = Target(instance, owner, repo);
        string? stateValue = Choice(state, nameof(state), States);
        string? sortValue = Choice(sort, nameof(sort), Sorts);
        string? directionValue = Choice(direction, nameof(direction), Directions);
        if (perPage is { } pp && (pp < 1 || pp > 100))
            throw new McpException("perPage must be between 1 and 100.");
        if (page is <= 0)
            throw new McpException("page must be a positive integer.");

        List<string> query = [];
        if (stateValue is not null)
            query.Add("state=" + stateValue);
        if (sortValue is not null)
            query.Add("sort=" + sortValue);
        if (directionValue is not null)
            query.Add("direction=" + directionValue);
        if (perPage is { } p)
            query.Add("per_page=" + p.ToString(CultureInfo.InvariantCulture));
        if (page is { } pg)
            query.Add("page=" + pg.ToString(CultureInfo.InvariantCulture));

        string path = repoPath + "milestones";
        Uri uri = new(new Uri(target.ApiEndpoint), query.Count == 0 ? path : path + "?" + string.Join('&', query));
        // path 인자는 오류 메시지에도 쓰이므로 쿼리를 붙이지 않는다
        return await _client.SendAsync(target, HttpMethod.Get, uri, path, null, cancellationToken).ConfigureAwait(false)
            ?? throw new McpException($"GitHub '{target.Name}' GET {path} returned no content.");
    }

    /// <summary>마일스톤 하나를 조회한다.</summary>
    [McpServerTool(Name = "github_get_milestone", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("지정한 GitHub 인스턴스의 저장소에서 마일스톤 하나(GET milestones/{number})를 GitHub 응답 그대로 반환한다. milestoneNumber는 응답의 number이며 id가 아니다. " + PermissionText)]
    public async Task<string> GetMilestone(
        [Description(InstanceDescription)] string? instance = null,
        [Description(OwnerDescription)] string? owner = null,
        [Description(RepoDescription)] string? repo = null,
        [Description(NumberDescription)] long? milestoneNumber = null,
        CancellationToken cancellationToken = default)
    {
        (GitHubInstance target, string repoPath) = Target(instance, owner, repo);
        long number = Number(milestoneNumber);
        return await SendJsonAsync(target, HttpMethod.Get, $"{repoPath}milestones/{number}", null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>마일스톤을 만든다.</summary>
    [McpServerTool(Name = "github_create_milestone", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = true)]
    [Description("지정한 GitHub 인스턴스의 저장소에 마일스톤을 만들고(POST milestones) GitHub 응답(마일스톤)을 그대로 반환한다. dueOn은 YYYY-MM-DD이며 UTC 정오(T12:00:00Z)로 보낸다. 쓰기 시 " + PermissionText)]
    public async Task<string> CreateMilestone(
        [Description(InstanceDescription)] string? instance = null,
        [Description(OwnerDescription)] string? owner = null,
        [Description(RepoDescription)] string? repo = null,
        [Description("마일스톤 제목(필수)")] string? title = null,
        [Description("open / closed(소문자). 생략하면 GitHub 기본(open)")] string? state = null,
        [Description("설명")] string? description = null,
        [Description("마감일 YYYY-MM-DD")] string? dueOn = null,
        CancellationToken cancellationToken = default)
    {
        (GitHubInstance target, string repoPath) = Target(instance, owner, repo);
        string titleValue = Required(title, nameof(title));
        string? stateValue = Choice(state, nameof(state), WriteStates);
        string? dueValue = DueOn(dueOn);

        JsonObject json = new() { ["title"] = titleValue };
        if (stateValue is not null)
            json["state"] = stateValue;
        if (!string.IsNullOrWhiteSpace(description))
            json["description"] = description.Trim();
        if (dueValue is not null)
            json["due_on"] = dueValue;
        return await SendJsonAsync(target, HttpMethod.Post, repoPath + "milestones", json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>마일스톤을 고친다. 준 값만 바꾼다.</summary>
    [McpServerTool(Name = "github_update_milestone", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
    [Description("지정한 GitHub 인스턴스의 저장소에서 마일스톤을 고치고(PATCH milestones/{number}) GitHub 응답(마일스톤)을 그대로 반환한다. 준 값만 바꾸며 하나는 있어야 한다. state=closed로 닫고 open으로 다시 연다. clearDescription·clearDueOn=true로 설명·마감일을 지운다. milestoneNumber는 응답의 number이며 id가 아니다. dueOn은 YYYY-MM-DD이며 UTC 정오로 보낸다. 쓰기 시 " + PermissionText)]
    public async Task<string> UpdateMilestone(
        [Description(InstanceDescription)] string? instance = null,
        [Description(OwnerDescription)] string? owner = null,
        [Description(RepoDescription)] string? repo = null,
        [Description(NumberDescription)] long? milestoneNumber = null,
        [Description("새 제목")] string? title = null,
        [Description("open(다시 열기) / closed(닫기)(소문자)")] string? state = null,
        [Description("새 설명. clearDescription과 함께 줄 수 없다")] string? description = null,
        [Description("새 마감일 YYYY-MM-DD. clearDueOn과 함께 줄 수 없다")] string? dueOn = null,
        [Description("true면 설명을 지운다")] bool clearDescription = false,
        [Description("true면 마감일을 지운다")] bool clearDueOn = false,
        CancellationToken cancellationToken = default)
    {
        (GitHubInstance target, string repoPath) = Target(instance, owner, repo);
        long number = Number(milestoneNumber);
        string? stateValue = Choice(state, nameof(state), WriteStates);
        string? dueValue = DueOn(dueOn);
        bool hasDescription = !string.IsNullOrWhiteSpace(description);
        if (hasDescription && clearDescription)
            throw new McpException("description and clearDescription cannot be used together.");
        if (dueValue is not null && clearDueOn)
            throw new McpException("dueOn and clearDueOn cannot be used together.");

        JsonObject json = [];
        if (!string.IsNullOrWhiteSpace(title))
            json["title"] = title.Trim();
        if (stateValue is not null)
            json["state"] = stateValue;
        if (hasDescription)
            json["description"] = description!.Trim();
        else if (clearDescription)
            json["description"] = null;
        if (dueValue is not null)
            json["due_on"] = dueValue;
        else if (clearDueOn)
            json["due_on"] = null;
        if (json.Count == 0)
            throw new McpException("at least one field to update is required.");
        return await SendJsonAsync(target, HttpMethod.Patch, $"{repoPath}milestones/{number}", json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>마일스톤을 지운다.</summary>
    [McpServerTool(Name = "github_delete_milestone", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
    [Description("지정한 GitHub 인스턴스의 저장소에서 마일스톤을 지우고(DELETE milestones/{number}) {deleted:{milestoneNumber}}를 반환한다. milestoneNumber는 응답의 number이며 id가 아니다. 쓰기 시 " + PermissionText)]
    public async Task<string> DeleteMilestone(
        [Description(InstanceDescription)] string? instance = null,
        [Description(OwnerDescription)] string? owner = null,
        [Description(RepoDescription)] string? repo = null,
        [Description(NumberDescription)] long? milestoneNumber = null,
        CancellationToken cancellationToken = default)
    {
        (GitHubInstance target, string repoPath) = Target(instance, owner, repo);
        long number = Number(milestoneNumber);
        await _client.SendAsync(target, HttpMethod.Delete, $"{repoPath}milestones/{number}", null, cancellationToken).ConfigureAwait(false);
        return new JsonObject { ["deleted"] = new JsonObject { ["milestoneNumber"] = number } }.ToJsonString();
    }

    private (GitHubInstance Target, string RepoPath) Target(string? instance, string? owner, string? repo)
    {
        GitHubInstance target = _client.Resolve(instance);
        return (target, GitHubRestClient.RepoPath(Required(owner, nameof(owner)), Required(repo, nameof(repo))));
    }

    private static long Number(long? milestoneNumber)
        => milestoneNumber is { } value && value > 0 ? value : throw new McpException("milestoneNumber must be a positive integer.");

    /// <summary>공백을 뗀 값이 허용 값(소문자)과 정확히 같아야 한다(D01_02). 값이 없으면 null</summary>
    private static string? Choice(string? value, string name, string[] allowed)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        string trimmed = value.Trim();
        return allowed.Contains(trimmed, StringComparer.Ordinal)
            ? trimmed
            : throw new McpException($"{name} must be one of: {string.Join(", ", allowed)}.");
    }

    /// <summary>YYYY-MM-DD를 검사해 UTC 정오 시각 문자열로 바꾼다. 값이 없으면 null</summary>
    private static string? DueOn(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        string trimmed = value.Trim();
        return DateOnly.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? trimmed + "T12:00:00Z"
            : throw new McpException("dueOn must be a date in YYYY-MM-DD format.");
    }

    private async Task<string> SendJsonAsync(GitHubInstance target, HttpMethod method, string path, JsonObject? body, CancellationToken cancellationToken)
        => await _client.SendAsync(target, method, path, body, cancellationToken).ConfigureAwait(false)
            ?? throw new McpException($"GitHub '{target.Name}' {method} {path} returned no content.");

    private static string Required(string? value, string name)
        => string.IsNullOrWhiteSpace(value) ? throw new McpException($"{name} is required.") : value.Trim();
}
