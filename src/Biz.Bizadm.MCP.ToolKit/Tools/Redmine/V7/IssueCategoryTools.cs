using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.Redmine;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tools.Redmine.V7;

/// <summary>Redmine 7.0 일감 범주 조회·생성·수정·삭제 Tool</summary>
[McpServerToolType]
public sealed class IssueCategoryTools
{
    private readonly RedmineClient _redmine;

    /// <summary>Redmine 클라이언트로 Tool을 만든다.</summary>
    /// <param name="redmine">Redmine REST API 클라이언트</param>
    public IssueCategoryTools(RedmineClient redmine)
    {
        _redmine = redmine;
    }

    /// <summary>프로젝트의 일감 범주 목록을 조회한다.</summary>
    [McpServerTool(Name = "list_issue_categories", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("프로젝트의 Redmine 일감 범주 목록을 조회한다.")]
    public Task<string> ListIssueCategories(
        [Description("프로젝트 ID 또는 식별자")] string projectId,
        CancellationToken cancellationToken = default)
    {
        return _redmine.GetAsync(RedminePaths.Project(projectId, "issue_categories.json"), cancellationToken);
    }

    /// <summary>Redmine 일감 범주 하나를 조회한다.</summary>
    [McpServerTool(Name = "get_issue_category", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("Redmine 일감 범주 하나를 조회한다.")]
    public Task<string> GetIssueCategory(
        [Description("일감 범주 ID")] int id,
        CancellationToken cancellationToken = default)
    {
        return _redmine.GetAsync($"issue_categories/{RedminePaths.Id(id, nameof(id))}.json", cancellationToken);
    }

    /// <summary>프로젝트에 일감 범주를 생성한다.</summary>
    [McpServerTool(Name = "create_issue_category", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = true)]
    [Description("프로젝트에 Redmine 일감 범주를 생성한다. " + RedmineToolDescriptions.Mutating)]
    public Task<string> CreateIssueCategory(
        [Description("프로젝트 ID 또는 식별자")] string projectId,
        [Description("범주 이름")] string name,
        [Description("이 범주 일감의 기본 담당자 ID")] int? assignedToId = null,
        CancellationToken cancellationToken = default)
    {
        string path = RedminePaths.Project(projectId, "issue_categories.json");
        if (string.IsNullOrWhiteSpace(name))
            throw new McpException("name is required.");
        JsonObject category = new();
        RedmineJson.Set(category, "name", name);
        RedmineJson.Set(category, "assigned_to_id", assignedToId);
        return _redmine.PostAsync(path, "issue_category", category, cancellationToken);
    }

    /// <summary>Redmine 일감 범주를 수정한다. 성공 시 본문이 비어 있다.</summary>
    [McpServerTool(Name = "update_issue_category", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
    [Description("Redmine 일감 범주를 수정한다. 성공 시 본문이 비어 있다. " + RedmineToolDescriptions.Mutating)]
    public Task<string> UpdateIssueCategory(
        [Description("일감 범주 ID")] int id,
        [Description("범주 이름")] string? name = null,
        [Description("이 범주 일감의 기본 담당자 ID")] int? assignedToId = null,
        CancellationToken cancellationToken = default)
    {
        string path = $"issue_categories/{RedminePaths.Id(id, nameof(id))}.json";
        JsonObject category = new();
        RedmineJson.Set(category, "name", name);
        RedmineJson.Set(category, "assigned_to_id", assignedToId);
        if (category.Count == 0)
            throw new McpException("수정할 필드를 하나 이상 지정해야 합니다.");
        return _redmine.PutAsync(path, "issue_category", category, cancellationToken);
    }

    /// <summary>Redmine 일감 범주를 삭제한다. 성공 시 본문이 비어 있다.</summary>
    [McpServerTool(Name = "delete_issue_category", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
    [Description("Redmine 일감 범주를 삭제한다. reassignToId를 주면 이 범주의 일감을 그 범주로 옮기고, 비우면 일감의 범주를 비운다. 성공 시 본문이 비어 있다. " + RedmineToolDescriptions.Mutating)]
    public Task<string> DeleteIssueCategory(
        [Description("일감 범주 ID")] int id,
        [Description("일감을 옮길 범주 ID")] int? reassignToId = null,
        CancellationToken cancellationToken = default)
    {
        string path = RedmineClient.BuildRelativeUri(
            $"issue_categories/{RedminePaths.Id(id, nameof(id))}.json",
            [("reassign_to_id", reassignToId?.ToString(CultureInfo.InvariantCulture))]);
        return _redmine.DeleteAsync(path, cancellationToken);
    }
}
