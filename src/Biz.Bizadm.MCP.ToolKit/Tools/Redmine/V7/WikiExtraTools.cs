using ModelContextProtocol.Server;
using System.ComponentModel;

using Biz.Bizadm.MCP.ToolKit.Redmine;

namespace Biz.Bizadm.MCP.ToolKit.Tools.Redmine.V7;

/// <summary>Redmine 7.0 위키 버전 조회·페이지 삭제 Tool</summary>
[McpServerToolType]
public sealed class WikiExtraTools
{
    private readonly RedmineClient _redmine;

    /// <summary>Redmine 클라이언트로 Tool을 만든다.</summary>
    /// <param name="redmine">Redmine REST API 클라이언트</param>
    public WikiExtraTools(RedmineClient redmine)
    {
        _redmine = redmine;
    }

    /// <summary>Redmine 위키 페이지의 특정 버전을 조회한다.</summary>
    [McpServerTool(Name = "get_wiki_page_version", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("Redmine 위키 페이지의 특정 버전(이전 내용)을 조회한다. 현재 버전 번호는 get_wiki_page의 version이다.")]
    public Task<string> GetWikiPageVersion(
        [Description("프로젝트 ID 또는 식별자")] string projectId,
        [Description("위키 페이지 제목")] string title,
        [Description("버전 번호. 1부터")] int version,
        CancellationToken cancellationToken = default)
    {
        string path = RedminePaths.Project(
            projectId,
            $"wiki/{RedminePaths.Segment(title, nameof(title))}/{RedminePaths.Id(version, nameof(version))}.json");
        return _redmine.GetAsync(path, cancellationToken);
    }

    /// <summary>Redmine 위키 페이지를 삭제한다. 하위 페이지는 삭제되지 않고 최상위 페이지가 된다.</summary>
    [McpServerTool(Name = "delete_wiki_page", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
    [Description("Redmine 위키 페이지를 삭제한다. 하위 페이지는 삭제되지 않고 최상위 페이지가 된다. 성공 시 본문이 비어 있다. " + RedmineToolDescriptions.Mutating)]
    public Task<string> DeleteWikiPage(
        [Description("프로젝트 ID 또는 식별자")] string projectId,
        [Description("위키 페이지 제목")] string title,
        CancellationToken cancellationToken = default)
    {
        string path = RedminePaths.Project(projectId, $"wiki/{RedminePaths.Segment(title, nameof(title))}.json");
        return _redmine.DeleteAsync(path, cancellationToken);
    }
}
