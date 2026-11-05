using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.Redmine;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tools.Redmine.V7;

/// <summary>Redmine 7.0 작업 시간 조회·생성·수정·삭제 Tool</summary>
[McpServerToolType]
public sealed class TimeEntryToolsV7
{
    private readonly RedmineClient _redmine;

    /// <summary>Redmine 클라이언트로 Tool을 만든다.</summary>
    /// <param name="redmine">Redmine REST API 클라이언트</param>
    public TimeEntryToolsV7(RedmineClient redmine)
    {
        _redmine = redmine;
    }

    /// <summary>Redmine 시간 기록 목록을 조회한다.</summary>
    [McpServerTool(Name = "list_time_entries", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("Redmine 시간 기록 목록을 조회한다.")]
    public Task<string> ListTimeEntries(
        [Description("프로젝트 ID 또는 식별자")] string? projectId = null,
        [Description("이슈 ID. 있으면 이 일감의 기록만 조회한다.")] int? issueId = null,
        [Description("사용자 ID 또는 me")] string? userId = null,
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
            ("user_id", userId),
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
        [Description("대신 기록할 사용자 ID. 다른 사용자 시간 기록 권한이 필요하다. 비우면 API 키 사용자다.")] int? userId = null,
        CancellationToken cancellationToken = default)
    {
        if (issueId is null && projectId is null)
        {
            throw new McpException("issueId 또는 projectId 중 하나가 필요합니다.");
        }

        JsonObject timeEntry = TimeEntryBody.Build(hours, issueId, projectId, activityId, comments, spentOn, includeHours: true);
        RedmineJson.Set(timeEntry, "user_id", userId);
        return _redmine.PostAsync("time_entries.json", "time_entry", timeEntry, cancellationToken);
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
        JsonObject timeEntry = TimeEntryBody.Build(hours, issueId, projectId, activityId, comments, spentOn, includeHours: hours is not null);
        if (timeEntry.Count == 0)
        {
            throw new McpException("수정할 필드를 하나 이상 지정해야 합니다.");
        }

        return _redmine.PutAsync($"time_entries/{id.ToString(CultureInfo.InvariantCulture)}.json", "time_entry", timeEntry, cancellationToken);
    }

    /// <summary>Redmine 시간 기록을 삭제한다. 성공 시 본문이 비어 있다.</summary>
    [McpServerTool(Name = "delete_time_entry", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
    [Description("Redmine 시간 기록을 삭제한다. 성공 시 본문이 비어 있다. " + RedmineToolDescriptions.Mutating)]
    public Task<string> DeleteTimeEntry(
        [Description("시간 기록 ID")] int id,
        CancellationToken cancellationToken = default)
    {
        return _redmine.DeleteAsync($"time_entries/{RedminePaths.Id(id, nameof(id))}.json", cancellationToken);
    }
}
