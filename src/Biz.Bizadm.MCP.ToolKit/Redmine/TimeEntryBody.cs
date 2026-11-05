using System.Text.Json.Nodes;

namespace Biz.Bizadm.MCP.ToolKit.Redmine;

/// <summary>작업 시간 생성·수정 Body 작성 helper(2.2·7.0 공용)</summary>
internal static class TimeEntryBody
{
    public static JsonObject Build(
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
