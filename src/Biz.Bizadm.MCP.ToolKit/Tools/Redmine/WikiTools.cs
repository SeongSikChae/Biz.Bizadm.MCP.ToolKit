using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.Redmine;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tools.Redmine;

/// <summary>Redmine 위키 조회·저장 Tool</summary>
[McpServerToolType]
public sealed class WikiTools
{
    private readonly RedmineClient _redmine;

    /// <summary>Redmine 클라이언트로 Tool을 만든다.</summary>
    /// <param name="redmine">Redmine REST API 클라이언트</param>
    public WikiTools(RedmineClient redmine)
    {
        _redmine = redmine;
    }

    /// <summary>프로젝트의 Redmine 위키 페이지 목록을 조회한다.</summary>
    [McpServerTool(Name = "list_wiki_pages", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("프로젝트의 Redmine 위키 페이지 목록을 조회한다.")]
    public Task<string> ListWikiPages(
        [Description("프로젝트 ID 또는 식별자")] string projectId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            throw new McpException("projectId is required.");
        return _redmine.GetAsync($"projects/{Uri.EscapeDataString(projectId.Trim())}/wiki/index.json", cancellationToken);
    }

    /// <summary>Redmine 위키 페이지 하나를 조회한다.</summary>
    [McpServerTool(Name = "get_wiki_page", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("Redmine 위키 페이지 하나를 조회한다.")]
    public Task<string> GetWikiPage(
        [Description("프로젝트 ID 또는 식별자")] string projectId,
        [Description("위키 페이지 제목")] string title,
        CancellationToken cancellationToken = default)
    {
        return _redmine.GetAsync(PagePath(projectId, title), cancellationToken);
    }

    /// <summary>Redmine 위키 페이지를 생성하거나 수정한다. 기존 페이지를 고치면 성공 본문이 비어 있다. 새 페이지를 만들 때만 페이지 JSON이 온다. 첨부만 붙일 때는 get_wiki_page의 현재 text를 그대로 보낸다.</summary>
    [McpServerTool(Name = "put_wiki_page", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
    [Description("Redmine 위키 페이지를 생성하거나 수정한다. 기존 페이지를 고치면 성공 본문이 비어 있다. 새 페이지를 만들 때만 페이지 JSON이 온다. 첨부만 붙일 때는 get_wiki_page의 현재 text를 그대로 보낸다. " + RedmineToolDescriptions.Mutating)]
    public Task<string> PutWikiPage(
        [Description("프로젝트 ID 또는 식별자")] string projectId,
        [Description("위키 페이지 제목")] string title,
        [Description("본문")] string text,
        [Description("변경 설명")] string? comments = null,
        [Description("upload_file로 받은 토큰 목록. 넘기면 위키 페이지에 첨부된다.")] IReadOnlyList<UploadReference>? uploads = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new McpException("text is required.");
        JsonObject wikiPage = new();
        RedmineJson.Set(wikiPage, "text", text);
        RedmineJson.Set(wikiPage, "comments", comments);
        // 2.2 WikiController#update는 wiki_page.uploads가 아니라 최상위 attachments만 읽는다.
        JsonObject body = new()
        {
            ["wiki_page"] = wikiPage
        };
        RedmineJson.SetUploads(body, uploads, "attachments");
        return _redmine.PutBodyAsync(PagePath(projectId, title), body, cancellationToken);
    }

    private static string PagePath(string projectId, string title)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            throw new McpException("projectId is required.");
        if (string.IsNullOrWhiteSpace(title))
            throw new McpException("title is required.");
        return $"projects/{Uri.EscapeDataString(projectId.Trim())}/wiki/{Uri.EscapeDataString(title.Trim())}.json";
    }
}
