using System.ComponentModel;
using System.Text.Json;

using Biz.Bizadm.MCP.ToolKit.Relay;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Biz.Bizadm.MCP.ToolKit.Tools.GitHub;

/// <summary>등록한 GitHub MCP Server들을 중계하는 게이트웨이 Tool</summary>
[McpServerToolType]
public sealed class GitHubTools
{
    internal const string PoolKey = "github";

    private readonly McpRelayPool _pool;

    /// <summary>DI에 등록한 GitHub client 풀로 Tool을 만든다. <see cref="Microsoft.Extensions.DependencyInjection.GitHubMcpServerBuilderExtensions.WithGitHubTools"/>로 등록해야 한다.</summary>
    /// <param name="services">서비스 공급자</param>
    public GitHubTools(IServiceProvider services)
        : this(services.GetRequiredKeyedService<McpRelayPool>(PoolKey))
    {
    }

    internal GitHubTools(McpRelayPool pool)
    {
        _pool = pool;
    }

    /// <summary>등록한 GitHub 인스턴스 목록을 조회한다.</summary>
    [McpServerTool(Name = "github_list_instances", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("등록한 GitHub 인스턴스의 이름, 용도 설명, MCP Endpoint를 조회한다. 다른 github_* Tool의 instance 인자로 이름을 쓴다.")]
    public string ListInstances() => McpRelayTools.ListInstances(_pool);

    /// <summary>지정한 GitHub 인스턴스의 MCP Tool 목록을 조회한다.</summary>
    [McpServerTool(Name = "github_list_tools", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("지정한 GitHub 인스턴스의 MCP Tool 목록(이름, 설명, 입력 스키마, 주석)을 조회한다. 결과는 인스턴스별로 캐시된다.")]
    public Task<string> ListTools(
        [Description("GitHub 인스턴스 이름(필수). github_list_instances로 확인")] string? instance = null,
        [Description("true면 캐시를 무시하고 다시 조회")] bool refresh = false,
        CancellationToken cancellationToken = default)
        => McpRelayTools.ListToolsAsync(_pool, instance, refresh, cancellationToken);

    /// <summary>지정한 GitHub 인스턴스의 MCP Tool을 호출한다.</summary>
    [McpServerTool(Name = "github_call_tool", ReadOnly = false, Idempotent = false, Destructive = true, OpenWorld = true)]
    [Description("지정한 GitHub 인스턴스의 MCP Tool을 호출하고 결과를 그대로 반환한다. Tool 이름과 인자는 github_list_tools로 확인한다.")]
    public Task<CallToolResult> CallTool(
        [Description("GitHub 인스턴스 이름(필수). github_list_instances로 확인")] string? instance = null,
        [Description("호출할 GitHub MCP Tool 이름(필수). github_list_tools로 확인")] string? tool = null,
        [Description("Tool 인자. JSON 객체 또는 JSON 객체 문자열")] JsonElement? arguments = null,
        CancellationToken cancellationToken = default)
        => McpRelayTools.CallToolAsync(_pool, instance, tool, arguments, cancellationToken);
}
