using ModelContextProtocol.Server;
using System.ComponentModel;

using Biz.Bizadm.MCP.ToolKit.Redmine;

namespace Biz.Bizadm.MCP.ToolKit.Tools.Redmine;

/// <summary>Redmine 상태·트래커·우선순위·작업 분류 조회 Tool</summary>
[McpServerToolType]
public sealed class MetadataTools
{
    private readonly RedmineClient _redmine;

    /// <summary>Redmine 클라이언트로 Tool을 만든다.</summary>
    /// <param name="redmine">Redmine REST API 클라이언트</param>
    public MetadataTools(RedmineClient redmine)
    {
        _redmine = redmine;
    }

    /// <summary>Redmine 이슈 상태 목록을 조회한다.</summary>
    [McpServerTool(Name = "list_issue_statuses", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("Redmine 이슈 상태 목록을 조회한다.")]
    public Task<string> ListIssueStatuses(CancellationToken cancellationToken = default)
    {
        return _redmine.GetAsync("issue_statuses.json", cancellationToken);
    }

    /// <summary>Redmine 트래커 목록을 조회한다.</summary>
    [McpServerTool(Name = "list_trackers", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("Redmine 트래커 목록을 조회한다.")]
    public Task<string> ListTrackers(CancellationToken cancellationToken = default)
    {
        return _redmine.GetAsync("trackers.json", cancellationToken);
    }

    /// <summary>Redmine 이슈 우선순위 목록을 조회한다.</summary>
    [McpServerTool(Name = "list_issue_priorities", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("Redmine 이슈 우선순위 목록을 조회한다.")]
    public Task<string> ListIssuePriorities(CancellationToken cancellationToken = default)
    {
        return _redmine.GetAsync("enumerations/issue_priorities.json", cancellationToken);
    }

    /// <summary>Redmine 시간 기록 활동 목록을 조회한다. 시스템 공통 활동만 반환한다. 프로젝트에서 재정의한 활동 ID는 목록에 없다.</summary>
    [McpServerTool(Name = "list_time_entry_activities", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("Redmine 시간 기록 활동 목록을 조회한다. 시스템 공통 활동만 반환한다. 프로젝트에서 재정의한 활동 ID는 목록에 없다.")]
    public Task<string> ListTimeEntryActivities(CancellationToken cancellationToken = default)
    {
        return _redmine.GetAsync("enumerations/time_entry_activities.json", cancellationToken);
    }
}
