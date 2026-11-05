using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.Redmine;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tools.Redmine.V7;

/// <summary>Redmine 7.0 일감 조회·생성·수정·삭제·관찰자 Tool</summary>
[McpServerToolType]
public sealed class IssueToolsV7
{
    private const string CustomFieldsDescription = "사용자 정의 필드 값 목록. 항목마다 value(단일)와 values(다중) 중 하나만 넣는다.";

    private readonly RedmineClient _redmine;

    /// <summary>Redmine 클라이언트로 Tool을 만든다.</summary>
    /// <param name="redmine">Redmine REST API 클라이언트</param>
    public IssueToolsV7(RedmineClient redmine)
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
        [Description("하위 프로젝트 ID 또는 식별자. !*이면 하위 프로젝트 일감을 뺀다.")] string? subprojectId = null,
        [Description("부모 일감 ID")] int? parentId = null,
        [Description("일감 ID 쉼표 목록. 예: 1,2,3")] string? issueIds = null,
        [Description("작성자 ID 또는 me")] string? authorId = null,
        [Description("일감 범주 ID")] int? categoryId = null,
        [Description("대상 버전 ID")] string? fixedVersionId = null,
        [Description("생성일 조건. 예: >=2026-01-01, <=2026-01-31, ><2026-01-01|2026-01-31")] string? createdOn = null,
        [Description("수정일 조건. createdOn과 같은 형식")] string? updatedOn = null,
        [Description("저장된 질의 ID. list_queries로 찾는다.")] int? queryId = null,
        [Description("정렬. 예: updated_on:desc,id")] string? sort = null,
        [Description("함께 받을 연관 데이터. attachments, relations 쉼표 목록")] string? include = null,
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
            ("tracker_id", ToQuery(trackerId)),
            ("subproject_id", subprojectId),
            ("parent_id", ToQuery(parentId)),
            ("issue_id", issueIds),
            ("author_id", authorId),
            ("category_id", ToQuery(categoryId)),
            ("fixed_version_id", fixedVersionId),
            ("created_on", createdOn),
            ("updated_on", updatedOn),
            ("query_id", ToQuery(queryId)),
            ("sort", sort),
            ("include", include));
    }

    /// <summary>Redmine 이슈 하나를 조회한다.</summary>
    [McpServerTool(Name = "get_issue", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("Redmine 이슈 하나를 조회한다.")]
    public Task<string> GetIssue(
        [Description("이슈 ID")] int id,
        [Description("함께 받을 연관 데이터. 기본값은 journals이다. children, attachments, relations, changesets, journals, watchers, allowed_statuses를 쉼표로 받을 수 있다.")] string? include = "journals",
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
        [Description(IssueTools.UploadsDescription)] IReadOnlyList<UploadReference>? uploads = null,
        [Description("비공개 일감 여부")] bool? isPrivate = null,
        [Description("일감 범주 ID")] int? categoryId = null,
        [Description("대상 버전 ID")] int? fixedVersionId = null,
        [Description(CustomFieldsDescription)] IReadOnlyList<CustomFieldValue>? customFields = null,
        [Description("관찰자 사용자 ID 목록")] IReadOnlyList<int>? watcherUserIds = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            throw new McpException("projectId is required.");
        if (string.IsNullOrWhiteSpace(subject))
            throw new McpException("subject is required.");
        JsonObject issue = IssueBody.Build(
            projectId, subject, description, trackerId, statusId, priorityId, assignedToId,
            parentIssueId, startDate, dueDate, estimatedHours, doneRatio, notes: null, uploads);
        ApplyV7(issue, isPrivate, categoryId, fixedVersionId, customFields, watcherUserIds, privateNotes: null);
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
        [Description(IssueTools.UploadsDescription)] IReadOnlyList<UploadReference>? uploads = null,
        [Description("비공개 일감 여부")] bool? isPrivate = null,
        [Description("일감 범주 ID")] int? categoryId = null,
        [Description("대상 버전 ID")] int? fixedVersionId = null,
        [Description(CustomFieldsDescription)] IReadOnlyList<CustomFieldValue>? customFields = null,
        [Description("관찰자 사용자 ID 목록")] IReadOnlyList<int>? watcherUserIds = null,
        CancellationToken cancellationToken = default)
    {
        RedminePaths.EnsurePositive(parentIssueId, nameof(parentIssueId));
        if (string.IsNullOrWhiteSpace(subject))
            throw new McpException("subject is required.");
        string resolvedProjectId = string.IsNullOrWhiteSpace(projectId)
            ? await IssueBody.GetParentProjectIdAsync(_redmine, parentIssueId, cancellationToken).ConfigureAwait(false)
            : projectId;
        JsonObject issue = IssueBody.Build(
            resolvedProjectId, subject, description, trackerId, statusId, priorityId, assignedToId,
            parentIssueId, startDate, dueDate, estimatedHours, doneRatio, notes: null, uploads);
        ApplyV7(issue, isPrivate, categoryId, fixedVersionId, customFields, watcherUserIds, privateNotes: null);
        return await _redmine.PostAsync("issues.json", "issue", issue, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Redmine 이슈를 수정하거나 코멘트를 추가한다. 성공 시 본문이 비어 있다.</summary>
    [McpServerTool(Name = "update_issue", ReadOnly = false, Idempotent = false, Destructive = true, OpenWorld = true)]
    [Description("Redmine 이슈를 수정하거나 코멘트를 추가한다. 기존 코멘트는 update_journal로 고친다. 성공 시 본문이 비어 있다. " + RedmineToolDescriptions.Mutating)]
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
        [Description(IssueTools.UploadsDescription)] IReadOnlyList<UploadReference>? uploads = null,
        [Description("비공개 일감 여부")] bool? isPrivate = null,
        [Description("일감 범주 ID")] int? categoryId = null,
        [Description("대상 버전 ID")] int? fixedVersionId = null,
        [Description(CustomFieldsDescription)] IReadOnlyList<CustomFieldValue>? customFields = null,
        [Description("notes를 비공개 코멘트로 남길지 여부")] bool? privateNotes = null,
        CancellationToken cancellationToken = default)
    {
        JsonObject issue = IssueBody.Build(
            projectId: null, subject, description, trackerId, statusId, priorityId, assignedToId,
            parentIssueId, startDate, dueDate, estimatedHours, doneRatio, notes, uploads);
        ApplyV7(issue, isPrivate, categoryId, fixedVersionId, customFields, watcherUserIds: null, privateNotes);
        if (issue.Count == 0)
        {
            throw new McpException("수정할 필드를 하나 이상 지정해야 합니다.");
        }

        return _redmine.PutAsync($"issues/{id.ToString(CultureInfo.InvariantCulture)}.json", "issue", issue, cancellationToken);
    }

    /// <summary>Redmine 이슈를 삭제한다. 하위 일감과 작업 시간 처리는 서버 설정을 따른다. 성공 시 본문이 비어 있다.</summary>
    [McpServerTool(Name = "delete_issue", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
    [Description("Redmine 이슈를 삭제한다. 되돌릴 수 없다. 성공 시 본문이 비어 있다. " + RedmineToolDescriptions.Mutating)]
    public Task<string> DeleteIssue(
        [Description("이슈 ID")] int id,
        CancellationToken cancellationToken = default)
    {
        return _redmine.DeleteAsync($"issues/{RedminePaths.Id(id, nameof(id))}.json", cancellationToken);
    }

    /// <summary>Redmine 이슈에 관찰자를 추가한다. 성공 시 본문이 비어 있다.</summary>
    [McpServerTool(Name = "add_issue_watcher", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = true)]
    [Description("Redmine 이슈에 관찰자를 추가한다. 성공 시 본문이 비어 있다. " + RedmineToolDescriptions.Mutating)]
    public Task<string> AddIssueWatcher(
        [Description("이슈 ID")] int issueId,
        [Description("관찰자로 추가할 사용자 ID")] int userId,
        CancellationToken cancellationToken = default)
    {
        string path = $"issues/{RedminePaths.Id(issueId, nameof(issueId))}/watchers.json";
        RedminePaths.EnsurePositive(userId, nameof(userId));
        return _redmine.PostBodyAsync(path, new JsonObject { ["user_id"] = userId }, cancellationToken);
    }

    /// <summary>Redmine 이슈에서 관찰자를 뺀다. 성공 시 본문이 비어 있다.</summary>
    [McpServerTool(Name = "remove_issue_watcher", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
    [Description("Redmine 이슈에서 관찰자를 뺀다. 성공 시 본문이 비어 있다. " + RedmineToolDescriptions.Mutating)]
    public Task<string> RemoveIssueWatcher(
        [Description("이슈 ID")] int issueId,
        [Description("뺄 사용자 ID")] int userId,
        CancellationToken cancellationToken = default)
    {
        return _redmine.DeleteAsync(
            $"issues/{RedminePaths.Id(issueId, nameof(issueId))}/watchers/{RedminePaths.Id(userId, nameof(userId))}.json",
            cancellationToken);
    }

    private static void ApplyV7(
        JsonObject issue,
        bool? isPrivate,
        int? categoryId,
        int? fixedVersionId,
        IReadOnlyList<CustomFieldValue>? customFields,
        IReadOnlyList<int>? watcherUserIds,
        bool? privateNotes)
    {
        RedmineJson.Set(issue, "is_private", isPrivate);
        RedmineJson.Set(issue, "category_id", categoryId);
        RedmineJson.Set(issue, "fixed_version_id", fixedVersionId);
        RedmineJson.SetCustomFields(issue, customFields);
        RedmineJson.SetIdArray(issue, "watcher_user_ids", watcherUserIds);
        RedmineJson.Set(issue, "private_notes", privateNotes);
    }

    private static string? ToQuery(int? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture);
    }
}
