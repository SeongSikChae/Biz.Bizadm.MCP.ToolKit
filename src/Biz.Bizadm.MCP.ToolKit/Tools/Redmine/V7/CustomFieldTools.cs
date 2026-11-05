using ModelContextProtocol.Server;
using System.ComponentModel;

using Biz.Bizadm.MCP.ToolKit.Redmine;

namespace Biz.Bizadm.MCP.ToolKit.Tools.Redmine.V7;

/// <summary>Redmine 7.0 사용자 정의 필드 조회 Tool</summary>
[McpServerToolType]
public sealed class CustomFieldTools
{
    private readonly RedmineClient _redmine;

    /// <summary>Redmine 클라이언트로 Tool을 만든다.</summary>
    /// <param name="redmine">Redmine REST API 클라이언트</param>
    public CustomFieldTools(RedmineClient redmine)
    {
        _redmine = redmine;
    }

    /// <summary>Redmine 사용자 정의 필드 정의 목록을 조회한다. 관리자 권한이 필요하다.</summary>
    [McpServerTool(Name = "list_custom_fields", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("Redmine 사용자 정의 필드 정의 목록(형식, 가능한 값, 필수 여부)을 조회한다. 관리자 권한이 필요하다. id는 일감 생성·수정의 customFields에 쓴다.")]
    public Task<string> ListCustomFields(CancellationToken cancellationToken = default)
    {
        return _redmine.GetAsync("custom_fields.json", cancellationToken);
    }
}
