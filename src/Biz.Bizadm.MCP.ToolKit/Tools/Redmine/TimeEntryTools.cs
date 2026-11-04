using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.Redmine;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tools.Redmine;

/// <summary>Redmine 작업 시간 조회·등록·수정 Tool</summary>
[McpServerToolType]
public sealed class TimeEntryTools
{
    private readonly RedmineClient _redmine;

    /// <summary>Redmine 클라이언트로 Tool을 만든다.</summary>
    /// <param name="redmine">Redmine REST API 클라이언트</param>
    public TimeEntryTools(RedmineClient redmine)
    {
        _redmine = redmine;
    }

    /// <summary>Redmine 시간 기록 목록을 조회한다. 사용자로는 거르지 못한다. issueId가 있으면 projectId는 적용되지 않는다.</summary>
    [McpServerTool(Name = "list_time_entries", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("Redmine 시간 기록 목록을 조회한다. 사용자로는 거르지 못한다. issueId가 있으면 projectId는 적용되지 않는다.")]
    public Task<string> ListTimeEntries(
        [Description("프로젝트 ID 또는 식별자. issueId가 있으면 적용되지 않는다.")] string? projectId = null,
        [Description("이슈 ID. 있으면 이 일감의 기록만 조회한다.")] int? issueId = null,
        [Description("시작일. yyyy-MM-dd")] string? from = null,
        [Description("종료일. yyyy-MM-dd")] string? to = null,
        [Description("페이지 크기. 기본 25, 최대 100")] int? limit = null,
        [Description("건너뛸 개수. 기본 0")] int? offset = null,
        CancellationToken cancellationToken = default)
    {
        return _redmine.GetPagedAsync(
            "time_entries.json",
            limit,
            offset,
            cancellationToken,
            ("project_id", projectId),
            ("issue_id", issueId?.ToString(CultureInfo.InvariantCulture)),
            ("from", from),
            ("to", to));
    }

    /// <summary>Redmine 시간 기록 하나를 조회한다.</summary>
    [McpServerTool(Name = "get_time_entry", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("Redmine 시간 기록 하나를 조회한다.")]
    public Task<string> GetTimeEntry(
        [Description("시간 기록 ID")] int id,
        CancellationToken cancellationToken = default)
    {
        return _redmine.GetAsync($"time_entries/{id.ToString(CultureInfo.InvariantCulture)}.json", cancellationToken);
    }

    /// <summary>Redmine 시간 기록을 생성한다.</summary>
    [McpServerTool(Name = "create_time_entry", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = true)]
    [Description("Redmine 시간 기록을 생성한다. " + RedmineToolDescriptions.Mutating)]
    public Task<string> CreateTimeEntry(
        [Description("시간")] double hours,
        [Description("이슈 ID. projectId가 없으면 필수")] int? issueId = null,
        [Description("프로젝트 ID. issueId가 없으면 필수")] int? projectId = null,
        [Description("활동 ID")] int? activityId = null,
        [Description("설명")] string? comments = null,
        [Description("작업일. yyyy-MM-dd")] string? spentOn = null,
        CancellationToken cancellationToken = default)
    {
        if (issueId is null && projectId is null)
        {
            throw new McpException("issueId 또는 projectId 중 하나가 필요합니다.");
        }

        return _redmine.PostAsync("time_entries.json", "time_entry", BuildTimeEntry(hours, issueId, projectId, activityId, comments, spentOn, includeHours: true), cancellationToken);
    }

    /// <summary>Redmine 시간 기록을 수정한다. 성공 시 본문이 비어 있다.</summary>
    [McpServerTool(Name = "update_time_entry", ReadOnly = false, Idempotent = false, Destructive = true, OpenWorld = true)]
    [Description("Redmine 시간 기록을 수정한다. 성공 시 본문이 비어 있다. " + RedmineToolDescriptions.Mutating)]
    public Task<string> UpdateTimeEntry(
        [Description("시간 기록 ID")] int id,
        [Description("시간")] double? hours = null,
        [Description("이슈 ID")] int? issueId = null,
        [Description("프로젝트 ID")] int? projectId = null,
        [Description("활동 ID")] int? activityId = null,
        [Description("설명")] string? comments = null,
        [Description("작업일. yyyy-MM-dd")] string? spentOn = null,
        CancellationToken cancellationToken = default)
    {
        JsonObject timeEntry = BuildTimeEntry(hours, issueId, projectId, activityId, comments, spentOn, includeHours: hours is not null);
        if (timeEntry.Count == 0)
        {
            throw new McpException("수정할 필드를 하나 이상 지정해야 합니다.");
        }

        return _redmine.PutAsync($"time_entries/{id.ToString(CultureInfo.InvariantCulture)}.json", "time_entry", timeEntry, cancellationToken);
    }

    private static JsonObject BuildTimeEntry(
        double? hours,
        int? issueId,
        int? projectId,
        int? activityId,
        string? comments,
        string? spentOn,
        bool includeHours)
    {
        JsonObject timeEntry = new();
        if (includeHours)
        {
            RedmineJson.Set(timeEntry, "hours", hours);
        }

        RedmineJson.Set(timeEntry, "issue_id", issueId);
        RedmineJson.Set(timeEntry, "project_id", projectId);
        RedmineJson.Set(timeEntry, "activity_id", activityId);
        RedmineJson.Set(timeEntry, "comments", comments);
        RedmineJson.Set(timeEntry, "spent_on", spentOn);
        return timeEntry;
    }
}
