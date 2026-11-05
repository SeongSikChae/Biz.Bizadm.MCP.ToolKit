using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.Redmine;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tools.Redmine.V7;

/// <summary>Redmine 7.0 프로젝트 구성원 조회·추가·역할 변경·제거 Tool</summary>
[McpServerToolType]
public sealed class MembershipTools
{
    private readonly RedmineClient _redmine;

    /// <summary>Redmine 클라이언트로 Tool을 만든다.</summary>
    /// <param name="redmine">Redmine REST API 클라이언트</param>
    public MembershipTools(RedmineClient redmine)
    {
        _redmine = redmine;
    }

    /// <summary>프로젝트 구성원 목록을 조회한다.</summary>
    [McpServerTool(Name = "list_memberships", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("Redmine 프로젝트 구성원(사용자·그룹과 역할) 목록을 조회한다.")]
    public Task<string> ListMemberships(
        [Description("프로젝트 ID 또는 식별자")] string projectId,
        [Description("페이지 크기. 기본 25, 최대 100")] int? limit = null,
        [Description("건너뛸 개수. 기본 0")] int? offset = null,
        CancellationToken cancellationToken = default)
    {
        return _redmine.GetPagedAsync(RedminePaths.Project(projectId, "memberships.json"), limit, offset, cancellationToken);
    }

    /// <summary>Redmine 구성원 하나를 조회한다.</summary>
    [McpServerTool(Name = "get_membership", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("Redmine 프로젝트 구성원 하나를 조회한다.")]
    public Task<string> GetMembership(
        [Description("구성원 ID")] int id,
        CancellationToken cancellationToken = default)
    {
        return _redmine.GetAsync($"memberships/{RedminePaths.Id(id, nameof(id))}.json", cancellationToken);
    }

    /// <summary>프로젝트에 사용자나 그룹을 구성원으로 추가한다.</summary>
    [McpServerTool(Name = "create_membership", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = true)]
    [Description("Redmine 프로젝트에 사용자나 그룹을 구성원으로 추가한다. 역할 ID는 list_roles로 찾는다. " + RedmineToolDescriptions.Mutating)]
    public Task<string> CreateMembership(
        [Description("프로젝트 ID 또는 식별자")] string projectId,
        [Description("사용자 ID 또는 그룹 ID")] int userId,
        [Description("역할 ID 목록. 하나 이상")] IReadOnlyList<int> roleIds,
        CancellationToken cancellationToken = default)
    {
        string path = RedminePaths.Project(projectId, "memberships.json");
        RedminePaths.EnsurePositive(userId, nameof(userId));
        JsonObject membership = new() { ["user_id"] = userId };
        SetRoleIds(membership, roleIds);
        return _redmine.PostAsync(path, "membership", membership, cancellationToken);
    }

    /// <summary>구성원의 역할을 바꾼다. 성공 시 본문이 비어 있다.</summary>
    [McpServerTool(Name = "update_membership", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
    [Description("Redmine 프로젝트 구성원의 역할을 바꾼다. roleIds로 역할 전체를 교체한다. 성공 시 본문이 비어 있다. " + RedmineToolDescriptions.Mutating)]
    public Task<string> UpdateMembership(
        [Description("구성원 ID")] int id,
        [Description("새 역할 ID 목록. 하나 이상")] IReadOnlyList<int> roleIds,
        CancellationToken cancellationToken = default)
    {
        string path = $"memberships/{RedminePaths.Id(id, nameof(id))}.json";
        JsonObject membership = new();
        SetRoleIds(membership, roleIds);
        return _redmine.PutAsync(path, "membership", membership, cancellationToken);
    }

    /// <summary>프로젝트에서 구성원을 제거한다. 성공 시 본문이 비어 있다.</summary>
    [McpServerTool(Name = "delete_membership", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
    [Description("Redmine 프로젝트에서 구성원을 제거한다. 그룹을 통해 상속된 구성원은 제거할 수 없다. 성공 시 본문이 비어 있다. " + RedmineToolDescriptions.Mutating)]
    public Task<string> DeleteMembership(
        [Description("구성원 ID")] int id,
        CancellationToken cancellationToken = default)
    {
        return _redmine.DeleteAsync($"memberships/{RedminePaths.Id(id, nameof(id))}.json", cancellationToken);
    }

    private static void SetRoleIds(JsonObject membership, IReadOnlyList<int>? roleIds)
    {
        if (roleIds is null || roleIds.Count == 0)
            throw new McpException("roleIds must contain at least one role id.");
        RedmineJson.SetIdArray(membership, "role_ids", roleIds);
    }
}
