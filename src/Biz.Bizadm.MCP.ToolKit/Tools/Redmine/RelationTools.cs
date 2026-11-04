using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.Redmine;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tools.Redmine;

/// <summary>Redmine 일감 관계 조회·등록·삭제 Tool</summary>
[McpServerToolType]
public sealed class RelationTools
{
    internal static readonly string[] RelationTypes =
        ["relates", "duplicates", "duplicated", "blocks", "blocked", "precedes", "follows"];

    private readonly RedmineClient _redmine;

    /// <summary>Redmine 클라이언트로 Tool을 만든다.</summary>
    /// <param name="redmine">Redmine REST API 클라이언트</param>
    public RelationTools(RedmineClient redmine)
    {
        _redmine = redmine;
    }

    /// <summary>Redmine 일감의 연결된 일감(관계) 목록을 조회한다.</summary>
    [McpServerTool(Name = "list_issue_relations", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("Redmine 일감의 연결된 일감(관계) 목록을 조회한다.")]
    public Task<string> ListIssueRelations(
        [Description("일감 ID")] int issueId,
        CancellationToken cancellationToken = default)
    {
        EnsurePositive(issueId, nameof(issueId));
        return _redmine.GetAsync(
            $"issues/{issueId.ToString(CultureInfo.InvariantCulture)}/relations.json",
            cancellationToken);
    }

    /// <summary>두 Redmine 일감을 연결된 일감(관계)으로 등록한다. 관계는 수정할 수 없으므로 바꾸려면 삭제 후 다시 만든다.</summary>
    [McpServerTool(Name = "create_issue_relation", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = true)]
    [Description("두 Redmine 일감을 연결된 일감(관계)으로 등록한다. 관계는 수정할 수 없으므로 바꾸려면 삭제 후 다시 만든다. " + RedmineToolDescriptions.Mutating)]
    public Task<string> CreateIssueRelation(
        [Description("기준 일감 ID")] int issueId,
        [Description("연결할 대상 일감 ID")] int issueToId,
        [Description("관계 종류. relates, duplicates, duplicated, blocks, blocked, precedes, follows 중 하나이다. copied_to는 2.2에 없다.")] string relationType,
        [Description("지연 일수. precedes, follows일 때만 쓸 수 있다.")] int? delay = null,
        CancellationToken cancellationToken = default)
    {
        EnsurePositive(issueId, nameof(issueId));
        EnsurePositive(issueToId, nameof(issueToId));
        string type = relationType?.Trim() ?? string.Empty;
        if (!RelationTypes.Contains(type, StringComparer.Ordinal))
        {
            throw new McpException(
                $"relationType must be one of: {string.Join(", ", RelationTypes)}.");
        }

        if (delay is not null && type is not ("precedes" or "follows"))
        {
            throw new McpException("delay is only allowed for precedes or follows.");
        }

        JsonObject relation = new();
        RedmineJson.Set(relation, "issue_to_id", issueToId);
        RedmineJson.Set(relation, "relation_type", type);
        RedmineJson.Set(relation, "delay", delay);
        return _redmine.PostAsync(
            $"issues/{issueId.ToString(CultureInfo.InvariantCulture)}/relations.json",
            "relation",
            relation,
            cancellationToken);
    }

    /// <summary>Redmine 일감 관계를 삭제한다. 관계 ID는 list_issue_relations 또는 get_issue(include=relations)로 확인한다. 성공 시 본문이 비어 있다.</summary>
    [McpServerTool(Name = "delete_issue_relation", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
    [Description("Redmine 일감 관계를 삭제한다. 관계 ID는 list_issue_relations 또는 get_issue(include=relations)로 확인한다. 성공 시 본문이 비어 있다. " + RedmineToolDescriptions.Mutating)]
    public Task<string> DeleteIssueRelation(
        [Description("관계 ID")] int relationId,
        CancellationToken cancellationToken = default)
    {
        EnsurePositive(relationId, nameof(relationId));
        return _redmine.DeleteAsync(
            $"relations/{relationId.ToString(CultureInfo.InvariantCulture)}.json",
            cancellationToken);
    }

    private static void EnsurePositive(int value, string name)
    {
        if (value < 1)
        {
            throw new McpException($"{name} must be positive.");
        }
    }
}
