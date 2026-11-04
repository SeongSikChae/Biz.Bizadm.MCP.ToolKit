using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.Redmine;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tools.Redmine;

/// <summary>Redmine 일감 조회·생성·수정 Tool</summary>
[McpServerToolType]
public sealed class IssueTools
{
    internal const string UploadsDescription = "upload_file로 받은 토큰 목록. 넘기면 일감에 첨부된다.";

    private readonly RedmineClient _redmine;

    /// <summary>Redmine 클라이언트로 Tool을 만든다.</summary>
    /// <param name="redmine">Redmine REST API 클라이언트</param>
    public IssueTools(RedmineClient redmine)
    {
        _redmine = redmine;
    }

    /// <summary>Redmine 이슈 목록을 조회한다.</summary>
    [McpServerTool(Name = "list_issues", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("Redmine 이슈 목록을 조회한다.")]
    public Task<string> ListIssues(
        [Description("프로젝트 ID 또는 식별자")] string? projectId = null,
        [Description("상태 ID, open, closed, *. 비우면 열린 일감만 반환한다. 전체는 *이다.")] string? statusId = null,
        [Description("담당자 ID 또는 me")] string? assignedToId = null,
        [Description("트래커 ID")] int? trackerId = null,
        [Description("페이지 크기. 기본 25, 최대 100")] int? limit = null,
        [Description("건너뛸 개수. 기본 0")] int? offset = null,
        CancellationToken cancellationToken = default)
    {
        return _redmine.GetPagedAsync(
            "issues.json",
            limit,
            offset,
            cancellationToken,
            ("project_id", projectId),
            ("status_id", statusId),
            ("assigned_to_id", assignedToId),
            ("tracker_id", trackerId?.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>Redmine 이슈 하나를 조회한다.</summary>
    [McpServerTool(Name = "get_issue", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("Redmine 이슈 하나를 조회한다.")]
    public Task<string> GetIssue(
        [Description("이슈 ID")] int id,
        [Description("함께 받을 연관 데이터. 기본값은 journals이다. children, attachments, relations, changesets를 받을 수 있다. watchers는 2.2 응답에 없다.")] string? include = "journals",
        CancellationToken cancellationToken = default)
    {
        return _redmine.GetAsync(
            $"issues/{id.ToString(CultureInfo.InvariantCulture)}.json",
            cancellationToken,
            ("include", string.IsNullOrWhiteSpace(include) ? "journals" : include));
    }

    /// <summary>Redmine 이슈를 생성한다.</summary>
    [McpServerTool(Name = "create_issue", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = true)]
    [Description("Redmine 이슈를 생성한다. " + RedmineToolDescriptions.Mutating)]
    public Task<string> CreateIssue(
        [Description("프로젝트 ID 또는 식별자")] string projectId,
        [Description("제목")] string subject,
        [Description("설명")] string? description = null,
        [Description("트래커 ID")] int? trackerId = null,
        [Description("상태 ID")] int? statusId = null,
        [Description("우선순위 ID")] int? priorityId = null,
        [Description("담당자 ID")] int? assignedToId = null,
        [Description("부모 이슈 ID")] int? parentIssueId = null,
        [Description("시작일. yyyy-MM-dd")] string? startDate = null,
        [Description("완료 기한. yyyy-MM-dd")] string? dueDate = null,
        [Description("예상 시간")] double? estimatedHours = null,
        [Description("진척도. 0부터 100")] int? doneRatio = null,
        [Description(UploadsDescription)] IReadOnlyList<UploadReference>? uploads = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            throw new McpException("projectId is required.");
        if (string.IsNullOrWhiteSpace(subject))
            throw new McpException("subject is required.");
        JsonObject issue = BuildIssue(
            projectId,
            subject,
            description,
            trackerId,
            statusId,
            priorityId,
            assignedToId,
            parentIssueId,
            startDate,
            dueDate,
            estimatedHours,
            doneRatio,
            notes: null,
            uploads);
        return _redmine.PostAsync("issues.json", "issue", issue, cancellationToken);
    }

    /// <summary>부모 일감 아래에 하위 일감을 생성한다. projectId를 비우면 부모 일감의 프로젝트를 쓴다.</summary>
    [McpServerTool(Name = "create_subtask", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = true)]
    [Description("부모 일감 아래에 하위 일감을 생성한다. projectId를 비우면 부모 일감의 프로젝트를 쓴다. " + RedmineToolDescriptions.Mutating)]
    public async Task<string> CreateSubtask(
        [Description("부모 일감 ID")] int parentIssueId,
        [Description("제목")] string subject,
        [Description("프로젝트 ID 또는 식별자. 비우면 부모 일감의 프로젝트")] string? projectId = null,
        [Description("설명")] string? description = null,
        [Description("트래커 ID")] int? trackerId = null,
        [Description("상태 ID")] int? statusId = null,
        [Description("우선순위 ID")] int? priorityId = null,
        [Description("담당자 ID")] int? assignedToId = null,
        [Description("시작일. yyyy-MM-dd")] string? startDate = null,
        [Description("완료 기한. yyyy-MM-dd")] string? dueDate = null,
        [Description("예상 시간")] double? estimatedHours = null,
        [Description("진척도. 0부터 100")] int? doneRatio = null,
        [Description(UploadsDescription)] IReadOnlyList<UploadReference>? uploads = null,
        CancellationToken cancellationToken = default)
    {
        if (parentIssueId < 1)
        {
            throw new McpException("parentIssueId must be positive.");
        }

        if (string.IsNullOrWhiteSpace(subject))
            throw new McpException("subject is required.");
        string resolvedProjectId = string.IsNullOrWhiteSpace(projectId)
            ? await GetParentProjectIdAsync(parentIssueId, cancellationToken).ConfigureAwait(false)
            : projectId;
        JsonObject issue = BuildIssue(
            resolvedProjectId,
            subject,
            description,
            trackerId,
            statusId,
            priorityId,
            assignedToId,
            parentIssueId,
            startDate,
            dueDate,
            estimatedHours,
            doneRatio,
            notes: null,
            uploads);
        return await _redmine.PostAsync("issues.json", "issue", issue, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Redmine 이슈를 수정하거나 코멘트를 추가한다. 성공 시 본문이 비어 있다.</summary>
    [McpServerTool(Name = "update_issue", ReadOnly = false, Idempotent = false, Destructive = true, OpenWorld = true)]
    [Description("Redmine 이슈를 수정하거나 코멘트를 추가한다. 성공 시 본문이 비어 있다. " + RedmineToolDescriptions.Mutating)]
    public Task<string> UpdateIssue(
        [Description("이슈 ID")] int id,
        [Description("제목")] string? subject = null,
        [Description("설명")] string? description = null,
        [Description("트래커 ID")] int? trackerId = null,
        [Description("상태 ID")] int? statusId = null,
        [Description("우선순위 ID")] int? priorityId = null,
        [Description("담당자 ID")] int? assignedToId = null,
        [Description("부모 이슈 ID")] int? parentIssueId = null,
        [Description("시작일. yyyy-MM-dd")] string? startDate = null,
        [Description("완료 기한. yyyy-MM-dd")] string? dueDate = null,
        [Description("예상 시간")] double? estimatedHours = null,
        [Description("진척도. 0부터 100")] int? doneRatio = null,
        [Description("코멘트")] string? notes = null,
        [Description(UploadsDescription)] IReadOnlyList<UploadReference>? uploads = null,
        CancellationToken cancellationToken = default)
    {
        JsonObject issue = BuildIssue(
            projectId: null,
            subject,
            description,
            trackerId,
            statusId,
            priorityId,
            assignedToId,
            parentIssueId,
            startDate,
            dueDate,
            estimatedHours,
            doneRatio,
            notes,
            uploads);
        if (issue.Count == 0)
        {
            throw new McpException("수정할 필드를 하나 이상 지정해야 합니다.");
        }

        return _redmine.PutAsync($"issues/{id.ToString(CultureInfo.InvariantCulture)}.json", "issue", issue, cancellationToken);
    }

    private static JsonObject BuildIssue(
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

    private async Task<string> GetParentProjectIdAsync(int parentIssueId, CancellationToken cancellationToken)
    {
        string parentJson = await _redmine.GetAsync(
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
