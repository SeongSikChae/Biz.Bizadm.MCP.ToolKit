using ModelContextProtocol.Server;
using System.ComponentModel;

using Biz.Bizadm.MCP.ToolKit.Redmine;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tools.Redmine;

/// <summary>Redmine 프로젝트 조회 Tool</summary>
[McpServerToolType]
public sealed class ProjectTools
{
    private readonly RedmineClient _redmine;

    /// <summary>Redmine 클라이언트로 Tool을 만든다.</summary>
    /// <param name="redmine">Redmine REST API 클라이언트</param>
    public ProjectTools(RedmineClient redmine)
    {
        _redmine = redmine;
    }

    /// <summary>Redmine 프로젝트 목록을 조회한다.</summary>
    [McpServerTool(Name = "list_projects", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("Redmine 프로젝트 목록을 조회한다.")]
    public Task<string> ListProjects(
        [Description("페이지 크기. 기본 25, 최대 100")] int? limit = null,
        [Description("건너뛸 개수. 기본 0")] int? offset = null,
        CancellationToken cancellationToken = default)
    {
        return _redmine.GetPagedAsync("projects.json", limit, offset, cancellationToken);
    }

    /// <summary>Redmine 프로젝트 하나를 조회한다.</summary>
    [McpServerTool(Name = "get_project", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("Redmine 프로젝트 하나를 조회한다.")]
    public Task<string> GetProject(
        [Description("프로젝트 ID 또는 식별자")] string id,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new McpException("id is required.");
        return _redmine.GetAsync($"projects/{Uri.EscapeDataString(id.Trim())}.json", cancellationToken);
    }
}
