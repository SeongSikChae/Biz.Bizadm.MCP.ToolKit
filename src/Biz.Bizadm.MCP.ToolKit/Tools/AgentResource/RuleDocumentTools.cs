using System.ComponentModel;
using Biz.Bizadm.MCP.ToolKit.AgentResource;
using Biz.Bizadm.MCP.ToolKit.Models.AgentResource;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Biz.Bizadm.MCP.ToolKit.Tools.AgentResource;

/// <summary>코딩 에이전트 rule 문서({RootPath}/rules/**.md) Tool</summary>
/// <param name="options">문서 루트 옵션</param>
[McpServerToolType]
public sealed class RuleDocumentTools(AgentResourceOptions options)
{
    private const string Extension = ".md";

    private string RulesRoot => Path.Combine(options.RootPath, "rules");

    /// <summary>rule 문서 목록을 조회한다.</summary>
    /// <returns>rule 문서 목록</returns>
    [McpServerTool(Name = "list_rule_documents", UseStructuredContent = true,
        ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("rules 아래 AGENTS.md 형식 rule 문서(.md) 목록을 front matter description과 함께 조회한다.")]
    public RuleDocumentListResult ListRuleDocuments()
    {
        string root = RulesRoot;
        if (!Directory.Exists(root))
            return new RuleDocumentListResult([]);

        List<RuleDocumentSummary> rules = Directory
            .EnumerateFiles(root, "*" + Extension, SearchOption.AllDirectories)
            .Where(path => path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
            .Select(path => new RuleDocumentSummary(
                Path.GetRelativePath(root, path).Replace('\\', '/'),
                Frontmatter.Parse(AgentResourceFile.Read(path)).GetValueOrDefault("description")))
            .OrderBy(rule => rule.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new RuleDocumentListResult(rules);
    }

    /// <summary>rule 문서 전문을 조회한다.</summary>
    /// <param name="name">rules 기준 상대 경로</param>
    /// <returns>rule 문서</returns>
    /// <exception cref="McpException">경로가 잘못됐거나 문서가 없다.</exception>
    [McpServerTool(Name = "get_rule_document", UseStructuredContent = true,
        ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("rules 기준 상대 경로로 rule 문서(.md) 전문을 조회한다. 없거나 경로가 rules 밖이면 오류를 반환한다.")]
    public RuleDocument GetRuleDocument(
        [Description("rules 기준 상대 경로(예: dotnet/naming.md, AGENTS.md), 확장자를 생략하면 .md")] string name)
    {
        (string relative, string full) = Resolve(name);
        if (!File.Exists(full))
            throw new McpException($"Rule document not found: {relative}");
        return new RuleDocument(relative, AgentResourceFile.Read(full));
    }

    /// <summary>rule 문서를 새로 만든다.</summary>
    /// <param name="name">rules 기준 상대 경로</param>
    /// <param name="content">front matter를 포함한 파일 전문</param>
    /// <returns>만든 문서 경로</returns>
    /// <exception cref="McpException">경로가 잘못됐거나 이미 있다.</exception>
    [McpServerTool(Name = "create_rule_document", UseStructuredContent = true,
        ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
    [Description("rules 아래에 rule 문서(.md)를 새로 만든다. content는 '---\\ndescription: ...\\n---' front matter를 포함한 전문이며 이미 있으면 오류를 반환한다.")]
    public AgentResourceWriteResult CreateRuleDocument(
        [Description("rules 기준 상대 경로(예: dotnet/naming.md), 확장자를 생략하면 .md")] string name,
        [Description("front matter를 포함한 문서 전문")] string content)
    {
        (string relative, string full) = Resolve(name);
        AgentResourceFile.CreateNew(full, content ?? string.Empty, $"Rule document already exists: {relative}");
        return new AgentResourceWriteResult(relative);
    }

    /// <summary>기존 rule 문서 전문을 바꾼다.</summary>
    /// <param name="name">rules 기준 상대 경로</param>
    /// <param name="content">front matter를 포함한 파일 전문</param>
    /// <returns>바꾼 문서 경로</returns>
    /// <exception cref="McpException">경로가 잘못됐거나 문서가 없다.</exception>
    [McpServerTool(Name = "update_rule_document", UseStructuredContent = true,
        ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = false)]
    [Description("기존 rule 문서(.md) 전문을 content로 바꾼다. 없으면 오류를 반환한다.")]
    public AgentResourceWriteResult UpdateRuleDocument(
        [Description("rules 기준 상대 경로(예: dotnet/naming.md), 확장자를 생략하면 .md")] string name,
        [Description("front matter를 포함한 문서 전문")] string content)
    {
        (string relative, string full) = Resolve(name);
        if (!File.Exists(full))
            throw new McpException($"Rule document not found: {relative}");
        AgentResourceFile.Overwrite(full, content ?? string.Empty);
        return new AgentResourceWriteResult(relative);
    }

    /// <summary>rule 문서를 삭제한다.</summary>
    /// <param name="name">rules 기준 상대 경로</param>
    /// <returns>삭제한 문서 경로</returns>
    /// <exception cref="McpException">경로가 잘못됐거나 문서가 없다.</exception>
    [McpServerTool(Name = "delete_rule_document", UseStructuredContent = true,
        ReadOnly = false, Idempotent = false, Destructive = true, OpenWorld = false)]
    [Description("rule 문서(.md)를 삭제한다. 없으면 오류를 반환한다.")]
    public AgentResourceWriteResult DeleteRuleDocument(
        [Description("rules 기준 상대 경로(예: dotnet/naming.md), 확장자를 생략하면 .md")] string name)
    {
        (string relative, string full) = Resolve(name);
        if (!File.Exists(full))
            throw new McpException($"Rule document not found: {relative}");
        AgentResourceFile.Delete(full);
        return new AgentResourceWriteResult(relative);
    }

    private (string Relative, string Full) Resolve(string name)
    {
        string relative = AgentResourcePath.Normalize(name);
        if (relative.Length > 0 && Path.GetExtension(relative).Length == 0)
            relative += Extension;
        if (relative.Length > 0 && !relative.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
            throw new McpException("Only .md rule documents are allowed");
        return (relative, AgentResourcePath.Resolve(RulesRoot, relative, "rules"));
    }
}
