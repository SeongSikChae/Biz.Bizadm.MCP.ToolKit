using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Globalization;

using Biz.Bizadm.MCP.ToolKit.Redmine;

namespace Biz.Bizadm.MCP.ToolKit.Tools.Redmine;

/// <summary>Redmine 사용자 조회 Tool</summary>
[McpServerToolType]
public sealed class UserTools
{
    private readonly RedmineClient _redmine;

    /// <summary>Redmine 클라이언트로 Tool을 만든다.</summary>
    /// <param name="redmine">Redmine REST API 클라이언트</param>
    public UserTools(RedmineClient redmine)
    {
        _redmine = redmine;
    }

    /// <summary>일반 API 키로 현재 Redmine 사용자를 조회한다.</summary>
    [McpServerTool(Name = "get_current_user", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("일반 API 키로 현재 Redmine 사용자를 조회한다.")]
    public Task<string> GetCurrentUser(CancellationToken cancellationToken = default)
    {
        return _redmine.GetAsync("users/current.json", cancellationToken);
    }

    /// <summary>Redmine 사용자 목록을 조회한다. 관리자 권한이 필요하다. 활성 사용자만 반환한다.</summary>
    [McpServerTool(Name = "list_users", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("Redmine 사용자 목록을 조회한다. 관리자 권한이 필요하다. 활성 사용자만 반환한다.")]
    public Task<string> ListUsers(
        [Description("페이지 크기. 기본 25, 최대 100")] int? limit = null,
        [Description("건너뛸 개수. 기본 0")] int? offset = null,
        CancellationToken cancellationToken = default)
    {
        return _redmine.GetPagedAsync("users.json", limit, offset, cancellationToken);
    }

    /// <summary>Redmine 사용자 하나를 조회한다. 관리자가 아니면 활성 사용자가 아니거나, 본인이 아니면서 보이는 멤버십과 활동이 없으면 404이다.</summary>
    [McpServerTool(Name = "get_user", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("Redmine 사용자 하나를 조회한다. 관리자가 아니면 활성 사용자가 아니거나, 본인이 아니면서 보이는 멤버십과 활동이 없으면 404이다.")]
    public Task<string> GetUser(
        [Description("사용자 ID")] int id,
        CancellationToken cancellationToken = default)
    {
        return _redmine.GetAsync($"users/{id.ToString(CultureInfo.InvariantCulture)}.json", cancellationToken);
    }
}
