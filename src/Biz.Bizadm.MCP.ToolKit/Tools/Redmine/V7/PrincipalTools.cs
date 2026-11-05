using ModelContextProtocol.Server;
using System.ComponentModel;

using Biz.Bizadm.MCP.ToolKit.Redmine;

namespace Biz.Bizadm.MCP.ToolKit.Tools.Redmine.V7;

/// <summary>Redmine 7.0 역할·그룹·내 계정 조회 Tool</summary>
[McpServerToolType]
public sealed class PrincipalTools
{
    private readonly RedmineClient _redmine;

    /// <summary>Redmine 클라이언트로 Tool을 만든다.</summary>
    /// <param name="redmine">Redmine REST API 클라이언트</param>
    public PrincipalTools(RedmineClient redmine)
    {
        _redmine = redmine;
    }

    /// <summary>Redmine 역할 목록을 조회한다.</summary>
    [McpServerTool(Name = "list_roles", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("Redmine 역할 목록을 조회한다. id는 구성원 추가·수정의 roleIds로 쓴다.")]
    public Task<string> ListRoles(CancellationToken cancellationToken = default)
    {
        return _redmine.GetAsync("roles.json", cancellationToken);
    }

    /// <summary>Redmine 역할 하나와 권한 목록을 조회한다.</summary>
    [McpServerTool(Name = "get_role", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("Redmine 역할 하나와 그 권한 목록을 조회한다.")]
    public Task<string> GetRole(
        [Description("역할 ID")] int id,
        CancellationToken cancellationToken = default)
    {
        return _redmine.GetAsync($"roles/{RedminePaths.Id(id, nameof(id))}.json", cancellationToken);
    }

    /// <summary>Redmine 그룹 목록을 조회한다. 관리자 권한이 필요하다.</summary>
    [McpServerTool(Name = "list_groups", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("Redmine 그룹 목록을 조회한다. 관리자 권한이 필요하다.")]
    public Task<string> ListGroups(CancellationToken cancellationToken = default)
    {
        return _redmine.GetAsync("groups.json", cancellationToken);
    }

    /// <summary>Redmine 그룹 하나를 구성 사용자·멤버십과 함께 조회한다. 관리자 권한이 필요하다.</summary>
    [McpServerTool(Name = "get_group", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("Redmine 그룹 하나를 조회한다. 관리자 권한이 필요하다.")]
    public Task<string> GetGroup(
        [Description("그룹 ID")] int id,
        CancellationToken cancellationToken = default)
    {
        return _redmine.GetAsync(
            $"groups/{RedminePaths.Id(id, nameof(id))}.json",
            cancellationToken,
            ("include", "users,memberships"));
    }

    /// <summary>API 키 사용자의 계정 정보를 조회한다.</summary>
    [McpServerTool(Name = "get_my_account", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("API 키 사용자의 계정 정보(로그인, 메일, 사용자 정의 필드 등)를 조회한다.")]
    public async Task<string> GetMyAccount(CancellationToken cancellationToken = default)
    {
        string json = await _redmine.GetAsync("my/account.json", cancellationToken).ConfigureAwait(false);
        return RedmineJson.RemoveUserApiKey(json);
    }
}
