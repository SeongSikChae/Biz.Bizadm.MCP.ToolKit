using ModelContextProtocol.Server;
using System.ComponentModel;

using Biz.Bizadm.MCP.ToolKit.Redmine;

namespace Biz.Bizadm.MCP.ToolKit.Tools.Redmine.V7;

/// <summary>Redmine 7.0 프로젝트 파일·뉴스 조회 Tool</summary>
[McpServerToolType]
public sealed class ProjectContentTools
{
    private readonly RedmineClient _redmine;

    /// <summary>Redmine 클라이언트로 Tool을 만든다.</summary>
    /// <param name="redmine">Redmine REST API 클라이언트</param>
    public ProjectContentTools(RedmineClient redmine)
    {
        _redmine = redmine;
    }

    /// <summary>프로젝트 파일 목록을 조회한다.</summary>
    [McpServerTool(Name = "list_project_files", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("Redmine 프로젝트의 파일 목록을 조회한다. 내려받기는 download_attachment에 content_url을 넘긴다.")]
    public Task<string> ListProjectFiles(
        [Description("프로젝트 ID 또는 식별자")] string projectId,
        CancellationToken cancellationToken = default)
    {
        return _redmine.GetAsync(RedminePaths.Project(projectId, "files.json"), cancellationToken);
    }

    /// <summary>뉴스 목록을 조회한다.</summary>
    [McpServerTool(Name = "list_news", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("Redmine 뉴스 목록을 조회한다. projectId를 비우면 볼 수 있는 모든 프로젝트의 뉴스다.")]
    public Task<string> ListNews(
        [Description("프로젝트 ID 또는 식별자. 비우면 전체")] string? projectId = null,
        [Description("페이지 크기. 기본 25, 최대 100")] int? limit = null,
        [Description("건너뛸 개수. 기본 0")] int? offset = null,
        CancellationToken cancellationToken = default)
    {
        string path = string.IsNullOrWhiteSpace(projectId) ? "news.json" : RedminePaths.Project(projectId, "news.json");
        return _redmine.GetPagedAsync(path, limit, offset, cancellationToken);
    }
}
