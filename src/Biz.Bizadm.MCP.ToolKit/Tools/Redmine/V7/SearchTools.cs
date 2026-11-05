using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Globalization;

using Biz.Bizadm.MCP.ToolKit.Redmine;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tools.Redmine.V7;

/// <summary>Redmine 7.0 검색·저장된 질의 조회 Tool</summary>
[McpServerToolType]
public sealed class SearchTools
{
    private readonly RedmineClient _redmine;

    /// <summary>Redmine 클라이언트로 Tool을 만든다.</summary>
    /// <param name="redmine">Redmine REST API 클라이언트</param>
    public SearchTools(RedmineClient redmine)
    {
        _redmine = redmine;
    }

    /// <summary>Redmine 전체 또는 프로젝트 안에서 검색한다.</summary>
    [McpServerTool(Name = "search", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("Redmine 전체 또는 프로젝트 안에서 검색한다.")]
    public Task<string> Search(
        [Description("검색어")] string query,
        [Description("프로젝트 ID 또는 식별자. 비우면 전체")] string? projectId = null,
        [Description("범위. all, my_projects, subprojects 중 하나")] string? scope = null,
        [Description("모든 단어 포함 여부. 기본 true. false면 한 단어라도 포함")] bool? allWords = null,
        [Description("제목만 검색할지 여부. 기본 false")] bool? titlesOnly = null,
        [Description("검색 대상 쉼표 목록. issues, news, documents, changesets, wiki_pages, messages, projects. 비우면 전체")] string? types = null,
        [Description("페이지 크기. 기본 25, 최대 100")] int? limit = null,
        [Description("건너뛸 개수. 기본 0")] int? offset = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new McpException("query is required.");

        string path = string.IsNullOrWhiteSpace(projectId) ? "search.json" : RedminePaths.Project(projectId, "search.json");
        List<(string Key, string? Value)> parameters =
        [
            ("q", query.Trim()),
            ("scope", scope),
            ("titles_only", titlesOnly == true ? "1" : null),
        ];
        if (!string.IsNullOrWhiteSpace(types))
        {
            foreach (string type in types.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                parameters.Add((type, "1"));
        }
        parameters.Add(("limit", RedmineClient.NormalizeLimit(limit).ToString(CultureInfo.InvariantCulture)));
        parameters.Add(("offset", RedmineClient.NormalizeOffset(offset).ToString(CultureInfo.InvariantCulture)));

        string uri = RedmineClient.BuildRelativeUri(path, parameters);
        // 7.0.3 SearchController는 all_words 값이 있으면 present?로 판정해 "0"도 true다. false는 빈 값으로만 보낼 수 있다.
        if (allWords == false)
            uri += "&all_words=";
        return _redmine.GetAsync(uri, cancellationToken);
    }

    /// <summary>저장된 일감 질의 목록을 조회한다.</summary>
    [McpServerTool(Name = "list_queries", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("Redmine에 저장된 일감 질의(공개 질의와 내 질의) 목록을 조회한다. id는 list_issues의 queryId로 쓴다.")]
    public Task<string> ListQueries(
        [Description("페이지 크기. 기본 25, 최대 100")] int? limit = null,
        [Description("건너뛸 개수. 기본 0")] int? offset = null,
        CancellationToken cancellationToken = default)
    {
        return _redmine.GetPagedAsync("queries.json", limit, offset, cancellationToken);
    }
}
