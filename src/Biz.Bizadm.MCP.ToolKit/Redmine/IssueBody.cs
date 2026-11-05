using System.Globalization;
using System.Text.Json.Nodes;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Redmine;

/// <summary>일감 생성·수정 Body 작성 helper(2.2·7.0 공용)</summary>
internal static class IssueBody
{
    public static JsonObject Build(
        string? projectId,
        string? subject,
        string? description,
        int? trackerId,
        int? statusId,
        int? priorityId,
        int? assignedToId,
        int? parentIssueId,
        string? startDate,
        string? dueDate,
        double? estimatedHours,
        int? doneRatio,
        string? notes,
        IReadOnlyList<UploadReference>? uploads)
    {
        JsonObject issue = new();
        RedmineJson.SetId(issue, "project_id", projectId);
        RedmineJson.Set(issue, "subject", subject);
        RedmineJson.Set(issue, "description", description);
        RedmineJson.Set(issue, "tracker_id", trackerId);
        RedmineJson.Set(issue, "status_id", statusId);
        RedmineJson.Set(issue, "priority_id", priorityId);
        RedmineJson.Set(issue, "assigned_to_id", assignedToId);
        RedmineJson.Set(issue, "parent_issue_id", parentIssueId);
        RedmineJson.Set(issue, "start_date", startDate);
        RedmineJson.Set(issue, "due_date", dueDate);
        RedmineJson.Set(issue, "estimated_hours", estimatedHours);
        RedmineJson.Set(issue, "done_ratio", doneRatio);
        RedmineJson.Set(issue, "notes", notes);
        RedmineJson.SetUploads(issue, uploads);
        return issue;
    }

    public static async Task<string> GetParentProjectIdAsync(RedmineClient redmine, int parentIssueId, CancellationToken cancellationToken)
    {
        string parentJson = await redmine.GetAsync(
            $"issues/{parentIssueId.ToString(CultureInfo.InvariantCulture)}.json",
            cancellationToken).ConfigureAwait(false);
        JsonNode? projectId = JsonNode.Parse(parentJson)?["issue"]?["project"]?["id"];
        if (projectId is null)
        {
            throw new McpException($"Parent issue {parentIssueId} response did not contain project.id.");
        }

        return projectId.GetValue<int>().ToString(CultureInfo.InvariantCulture);
    }
}
