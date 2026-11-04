using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using Biz.Bizadm.MCP.ToolKit.AgentResource;
using Biz.Bizadm.MCP.ToolKit.Models.AgentResource;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Biz.Bizadm.MCP.ToolKit.Tools.AgentResource;

/// <summary>기능 작업 산출물 docs 문서({RootPath}/docs/{category}/NNNN-{feature}-{phase}.md) Tool</summary>
/// <param name="options">문서 루트 옵션</param>
[McpServerToolType]
public sealed class DocDocumentTools(AgentResourceOptions options)
{
    private const int MaxNumber = 9999;

    private static readonly Regex CategoryPattern = new(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly Regex FilePattern = new(@"^([0-9]{4})-.+\.md$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly ConcurrentDictionary<string, object> CategoryLocks = new(StringComparer.OrdinalIgnoreCase);

    private string DocsRoot => Path.Combine(options.RootPath, "docs");

    /// <summary>docs 문서 목록을 조회한다.</summary>
    /// <param name="category">카테고리 필터</param>
    /// <param name="feature">기능 필터</param>
    /// <returns>docs 문서 목록</returns>
    [McpServerTool(Name = "list_doc_documents", UseStructuredContent = true,
        ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("docs/{category}/NNNN-{feature}-{phase}.md 문서 목록을 front matter status·feature·phase와 함께 조회한다. category·feature로 거를 수 있다(대소문자 무시).")]
    public DocDocumentListResult ListDocDocuments(
        [Description("카테고리 필터(폴더 이름), 생략하면 전체")] string? category = null,
        [Description("기능 필터(front matter feature), 생략하면 전체")] string? feature = null)
    {
        string root = DocsRoot;
        if (!Directory.Exists(root))
            return new DocDocumentListResult([]);

        List<DocDocumentSummary> docs = [];
        foreach (string directory in Directory.EnumerateDirectories(root))
        {
            string folder = Path.GetFileName(directory);
            if (!string.IsNullOrWhiteSpace(category) && !string.Equals(folder, category.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (string path in Directory.EnumerateFiles(directory, "*.md"))
            {
                int number = MatchNumber(path);
                if (number <= 0)
                    continue;
                IReadOnlyDictionary<string, string> frontmatter = Frontmatter.Parse(AgentResourceFile.Read(path));
                string? docFeature = frontmatter.GetValueOrDefault("feature");
                if (!string.IsNullOrWhiteSpace(feature) && !string.Equals(docFeature, feature.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;
                docs.Add(new DocDocumentSummary(
                    $"{folder}/{Path.GetFileName(path)}",
                    folder,
                    number,
                    frontmatter.GetValueOrDefault("status"),
                    docFeature,
                    frontmatter.GetValueOrDefault("phase")));
            }
        }

        return new DocDocumentListResult(docs
            .OrderBy(doc => doc.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(doc => doc.Number)
            .ToList());
    }

    /// <summary>docs 문서 전문을 조회한다.</summary>
    /// <param name="category">카테고리</param>
    /// <param name="number">순번</param>
    /// <returns>docs 문서</returns>
    /// <exception cref="McpException">입력이 잘못됐거나 문서가 없거나 같은 번호 문서가 여럿이다.</exception>
    [McpServerTool(Name = "get_doc_document", UseStructuredContent = true,
        ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("category와 4자리 순번으로 docs 문서 전문을 조회한다. 없거나 같은 번호 문서가 여럿이면 오류를 반환한다.")]
    public DocDocument GetDocDocument(
        [Description("카테고리(소문자 영문·숫자·하이픈, 예: agent-resource)")] string category,
        [Description("카테고리 안 순번(1~9999)")] int number)
    {
        (string normalized, string full) = FindByNumber(category, number);
        return new DocDocument($"{normalized}/{Path.GetFileName(full)}", normalized, number, AgentResourceFile.Read(full));
    }

    /// <summary>docs 문서를 카테고리 안 다음 순번으로 만든다.</summary>
    /// <param name="category">카테고리</param>
    /// <param name="feature">기능 식별자</param>
    /// <param name="phase">단계</param>
    /// <param name="content">본문</param>
    /// <param name="status">상태</param>
    /// <returns>만든 문서 경로</returns>
    /// <exception cref="McpException">입력이 잘못됐거나 순번이 남지 않았거나 같은 파일이 있다.</exception>
    [McpServerTool(Name = "create_doc_document", UseStructuredContent = true,
        ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
    [Description("docs/{category}/에 다음 4자리 순번으로 NNNN-{feature}-{phase}.md를 만든다. front matter(status·category·feature·phase)는 Tool이 만들고 content는 본문만 받는다.")]
    public AgentResourceWriteResult CreateDocDocument(
        [Description("카테고리(소문자 영문·숫자·하이픈, 예: agent-resource)")] string category,
        [Description("기능 식별자(공백·/ 없음, 예: storage-pool)")] string feature,
        [Description("단계(공백·/·- 없음, 예: 방안, 설계, 개발)")] string phase,
        [Description("front matter를 뺀 본문(Markdown)")] string content,
        [Description("front matter status, 기본 draft")] string status = "draft")
    {
        (string normalized, string directory) = ResolveCategory(category);
        feature = RequireSegment(feature, "feature", allowHyphen: true);
        phase = RequireSegment(phase, "phase", allowHyphen: false);
        status = RequireStatus(status);

        // 번호 계산과 생성을 category별 잠금으로 묶어 같은 프로세스 안의 동시 생성이 같은 번호를 쓰지 않게 한다.
        lock (CategoryLocks.GetOrAdd(directory, _ => new object()))
        {
            int next = (Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "*.md").Select(MatchNumber).DefaultIfEmpty(0).Max()
                : 0) + 1;
            if (next > MaxNumber)
                throw new McpException($"No document number left in category {normalized}");

            string fileName = $"{next.ToString("D4", CultureInfo.InvariantCulture)}-{feature}-{phase}.md";
            string full = Path.Combine(directory, fileName);
            string text = Frontmatter.Build([("status", status), ("category", normalized), ("feature", feature), ("phase", phase)], content);
            AgentResourceFile.CreateNew(full, text, $"Docs document already exists: {normalized}/{fileName}");
            return new AgentResourceWriteResult($"{normalized}/{fileName}");
        }
    }

    /// <summary>docs 문서 본문과 상태를 바꾼다.</summary>
    /// <param name="category">카테고리</param>
    /// <param name="number">순번</param>
    /// <param name="content">새 본문</param>
    /// <param name="status">새 상태, null이면 유지</param>
    /// <returns>바꾼 문서 경로</returns>
    /// <exception cref="McpException">입력이 잘못됐거나 문서가 없거나 같은 번호 문서가 여럿이다.</exception>
    [McpServerTool(Name = "update_doc_document", UseStructuredContent = true,
        ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = false)]
    [Description("category·순번으로 찾은 docs 문서의 본문을 content로 바꾼다. feature·phase는 유지하고 status는 생략하면 유지한다. front matter의 다른 키는 남지 않는다.")]
    public AgentResourceWriteResult UpdateDocDocument(
        [Description("카테고리(소문자 영문·숫자·하이픈, 예: agent-resource)")] string category,
        [Description("카테고리 안 순번(1~9999)")] int number,
        [Description("front matter를 뺀 새 본문(Markdown)")] string content,
        [Description("새 front matter status(예: done), 생략하면 유지")] string? status = null)
    {
        (string normalized, string full) = FindByNumber(category, number);
        if (status is not null)
            status = RequireStatus(status);
        IReadOnlyDictionary<string, string> frontmatter = Frontmatter.Parse(AgentResourceFile.Read(full));
        string text = Frontmatter.Build(
        [
            ("status", status ?? frontmatter.GetValueOrDefault("status", "draft")),
            ("category", normalized),
            ("feature", frontmatter.GetValueOrDefault("feature", string.Empty)),
            ("phase", frontmatter.GetValueOrDefault("phase", string.Empty)),
        ], content);
        AgentResourceFile.Overwrite(full, text);
        return new AgentResourceWriteResult($"{normalized}/{Path.GetFileName(full)}");
    }

    /// <summary>docs 문서를 삭제한다.</summary>
    /// <param name="category">카테고리</param>
    /// <param name="number">순번</param>
    /// <returns>삭제한 문서 경로</returns>
    /// <exception cref="McpException">입력이 잘못됐거나 문서가 없거나 같은 번호 문서가 여럿이다.</exception>
    [McpServerTool(Name = "delete_doc_document", UseStructuredContent = true,
        ReadOnly = false, Idempotent = false, Destructive = true, OpenWorld = false)]
    [Description("category·순번으로 찾은 docs 문서를 삭제한다. 없거나 같은 번호 문서가 여럿이면 오류를 반환한다.")]
    public AgentResourceWriteResult DeleteDocDocument(
        [Description("카테고리(소문자 영문·숫자·하이픈, 예: agent-resource)")] string category,
        [Description("카테고리 안 순번(1~9999)")] int number)
    {
        (string normalized, string full) = FindByNumber(category, number);
        AgentResourceFile.Delete(full);
        return new AgentResourceWriteResult($"{normalized}/{Path.GetFileName(full)}");
    }

    private (string Category, string Directory) ResolveCategory(string category)
    {
        string normalized = (category ?? string.Empty).Trim();
        if (!CategoryPattern.IsMatch(normalized))
            throw new McpException("category must contain only lowercase letters, digits and single hyphens");
        return (normalized, AgentResourcePath.Resolve(DocsRoot, normalized, "docs"));
    }

    private (string Category, string Full) FindByNumber(string category, int number)
    {
        (string normalized, string directory) = ResolveCategory(category);
        if (number is < 1 or > MaxNumber)
            throw new McpException($"number must be between 1 and {MaxNumber}");
        string prefix = number.ToString("D4", CultureInfo.InvariantCulture);
        string[] matches = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, $"{prefix}-*.md").Where(path => MatchNumber(path) == number).ToArray()
            : [];
        return matches.Length switch
        {
            0 => throw new McpException($"Docs document not found: {normalized}/{prefix}"),
            1 => (normalized, matches[0]),
            _ => throw new McpException($"Multiple docs documents match {normalized}/{prefix}"),
        };
    }

    private static int MatchNumber(string path)
    {
        Match match = FilePattern.Match(Path.GetFileName(path));
        return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
    }

    private static string RequireSegment(string value, string field, bool allowHyphen)
    {
        string trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0
            || trimmed.Any(char.IsWhiteSpace)
            || trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || trimmed.IndexOfAny(['/', '\\', ':']) >= 0
            || trimmed.Contains("..", StringComparison.Ordinal)
            || (!allowHyphen && trimmed.Contains('-')))
        {
            throw new McpException(allowHyphen
                ? $"{field} must be non-empty without whitespace, '/', '\\', ':' or '..'"
                : $"{field} must be non-empty without whitespace, '/', '\\', ':', '..' or '-'");
        }
        return trimmed;
    }

    private static string RequireStatus(string status)
    {
        string trimmed = (status ?? string.Empty).Trim();
        if (trimmed.Length == 0 || trimmed.Contains('\n') || trimmed.Contains('\r'))
            throw new McpException("status must be a non-empty single line");
        return trimmed;
    }
}
