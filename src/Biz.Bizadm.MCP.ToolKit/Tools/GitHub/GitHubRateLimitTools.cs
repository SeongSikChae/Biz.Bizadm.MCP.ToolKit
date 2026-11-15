using System.ComponentModel;

using Biz.Bizadm.MCP.ToolKit.GitHub;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Biz.Bizadm.MCP.ToolKit.Tools.GitHub;

/// <summary>GitHub REST API로 Token 사용자의 rate limit 상태를 조회하는 Tool. GitHub MCP Server가 노출하지 않는 기능이다.</summary>
[McpServerToolType]
public sealed class GitHubRateLimitTools
{
    private readonly GitHubRestClient _client;

    /// <summary>DI에 등록한 GitHub REST client로 Tool을 만든다. <see cref="Microsoft.Extensions.DependencyInjection.GitHubMcpServerBuilderExtensions.WithGitHubTools"/>로 등록해야 한다.</summary>
    /// <param name="services">서비스 공급자</param>
    public GitHubRateLimitTools(IServiceProvider services)
        : this(services.GetRequiredService<GitHubRestClient>())
    {
    }

    internal GitHubRateLimitTools(GitHubRestClient client)
    {
        _client = client;
    }

    /// <summary>인스턴스 Token 사용자의 rate limit 상태를 조회한다.</summary>
    [McpServerTool(Name = "github_get_rate_limit", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("지정한 GitHub 인스턴스의 Token으로 GET rate_limit을 호출해 resources(core, search, graphql 등)별 limit·used·remaining·reset(UTC epoch 초)을 담은 GitHub 응답을 그대로 반환한다. 이 호출은 GitHub 기본 한도에 포함되지 않는다.")]
    public Task<string> GetRateLimit(
        [Description("GitHub 인스턴스 이름(필수). github_list_instances로 확인")] string? instance = null,
        CancellationToken cancellationToken = default)
        => _client.GetRateLimitAsync(_client.Resolve(instance), cancellationToken);
}
