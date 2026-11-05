using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.Redmine;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tools.Redmine.V7;

/// <summary>Redmine 7.0 프로젝트 버전(로드맵) 조회·생성·수정·삭제 Tool</summary>
[McpServerToolType]
public sealed class VersionTools
{
    private readonly RedmineClient _redmine;

    /// <summary>Redmine 클라이언트로 Tool을 만든다.</summary>
    /// <param name="redmine">Redmine REST API 클라이언트</param>
    public VersionTools(RedmineClient redmine)
    {
        _redmine = redmine;
    }

    /// <summary>프로젝트의 버전 목록을 조회한다. 공유된 버전도 포함된다.</summary>
    [McpServerTool(Name = "list_versions", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("프로젝트의 Redmine 버전 목록을 조회한다. 다른 프로젝트에서 공유된 버전도 포함된다.")]
    public Task<string> ListVersions(
        [Description("프로젝트 ID 또는 식별자")] string projectId,
        CancellationToken cancellationToken = default)
    {
        return _redmine.GetAsync(RedminePaths.Project(projectId, "versions.json"), cancellationToken);
    }

    /// <summary>Redmine 버전 하나를 조회한다.</summary>
    [McpServerTool(Name = "get_version", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("Redmine 버전 하나를 조회한다.")]
    public Task<string> GetVersion(
        [Description("버전 ID")] int id,
        CancellationToken cancellationToken = default)
    {
        return _redmine.GetAsync($"versions/{RedminePaths.Id(id, nameof(id))}.json", cancellationToken);
    }

    /// <summary>프로젝트에 버전을 생성한다.</summary>
    [McpServerTool(Name = "create_version", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = true)]
    [Description("프로젝트에 Redmine 버전을 생성한다. " + RedmineToolDescriptions.Mutating)]
    public Task<string> CreateVersion(
        [Description("프로젝트 ID 또는 식별자")] string projectId,
        [Description("버전 이름")] string name,
        [Description("상태. open, locked, closed 중 하나")] string? status = null,
        [Description("공유 범위. none, descendants, hierarchy, tree, system 중 하나")] string? sharing = null,
        [Description("완료 기한. yyyy-MM-dd")] string? dueDate = null,
        [Description("설명")] string? description = null,
        [Description("연결할 위키 페이지 제목")] string? wikiPageTitle = null,
        CancellationToken cancellationToken = default)
    {
        string path = RedminePaths.Project(projectId, "versions.json");
        if (string.IsNullOrWhiteSpace(name))
            throw new McpException("name is required.");
        JsonObject version = BuildVersion(name, status, sharing, dueDate, description, wikiPageTitle);
        return _redmine.PostAsync(path, "version", version, cancellationToken);
    }

    /// <summary>Redmine 버전을 수정한다. 성공 시 본문이 비어 있다.</summary>
    [McpServerTool(Name = "update_version", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
    [Description("Redmine 버전을 수정한다. 성공 시 본문이 비어 있다. " + RedmineToolDescriptions.Mutating)]
    public Task<string> UpdateVersion(
        [Description("버전 ID")] int id,
        [Description("버전 이름")] string? name = null,
        [Description("상태. open, locked, closed 중 하나")] string? status = null,
        [Description("공유 범위. none, descendants, hierarchy, tree, system 중 하나")] string? sharing = null,
        [Description("완료 기한. yyyy-MM-dd")] string? dueDate = null,
        [Description("설명")] string? description = null,
        [Description("연결할 위키 페이지 제목")] string? wikiPageTitle = null,
        CancellationToken cancellationToken = default)
    {
        string path = $"versions/{RedminePaths.Id(id, nameof(id))}.json";
        JsonObject version = BuildVersion(name, status, sharing, dueDate, description, wikiPageTitle);
        if (version.Count == 0)
            throw new McpException("수정할 필드를 하나 이상 지정해야 합니다.");
        return _redmine.PutAsync(path, "version", version, cancellationToken);
    }

    /// <summary>Redmine 버전을 삭제한다. 일감이 연결된 버전은 삭제되지 않는다. 성공 시 본문이 비어 있다.</summary>
    [McpServerTool(Name = "delete_version", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
    [Description("Redmine 버전을 삭제한다. 일감이 연결된 버전은 서버가 거부한다. 성공 시 본문이 비어 있다. " + RedmineToolDescriptions.Mutating)]
    public Task<string> DeleteVersion(
        [Description("버전 ID")] int id,
        CancellationToken cancellationToken = default)
    {
        return _redmine.DeleteAsync($"versions/{RedminePaths.Id(id, nameof(id))}.json", cancellationToken);
    }

    private static JsonObject BuildVersion(
        string? name,
        string? status,
        string? sharing,
        string? dueDate,
        string? description,
        string? wikiPageTitle)
    {
        JsonObject version = new();
        RedmineJson.Set(version, "name", name);
        RedmineJson.Set(version, "status", status);
        RedmineJson.Set(version, "sharing", sharing);
        RedmineJson.Set(version, "due_date", dueDate);
        RedmineJson.Set(version, "description", description);
        RedmineJson.Set(version, "wiki_page_title", wikiPageTitle);
        return version;
    }
}
