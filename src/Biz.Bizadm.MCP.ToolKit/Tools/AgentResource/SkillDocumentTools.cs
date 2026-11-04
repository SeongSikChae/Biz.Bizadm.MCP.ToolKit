using System.ComponentModel;
using Biz.Bizadm.MCP.ToolKit.AgentResource;
using Biz.Bizadm.MCP.ToolKit.Models.AgentResource;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Biz.Bizadm.MCP.ToolKit.Tools.AgentResource;

/// <summary>코딩 에이전트 skill 문서({RootPath}/skills/&lt;name&gt;/SKILL.md) Tool</summary>
/// <param name="options">문서 루트 옵션</param>
[McpServerToolType]
public sealed class SkillDocumentTools(AgentResourceOptions options)
{
    private const string FileName = "SKILL.md";

    private string SkillsRoot => Path.Combine(options.RootPath, "skills");

    /// <summary>skill 문서 목록을 조회한다.</summary>
    /// <returns>skill 문서 목록</returns>
    [McpServerTool(Name = "list_skill_documents", UseStructuredContent = true,
        ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("skills 아래 skill 패키지(skills/<name>/SKILL.md) 목록을 front matter name·description과 함께 조회한다.")]
    public SkillDocumentListResult ListSkillDocuments()
    {
        string root = SkillsRoot;
        if (!Directory.Exists(root))
            return new SkillDocumentListResult([]);

        List<SkillDocumentSummary> skills = [];
        foreach (string path in Directory.EnumerateFiles(root, FileName, SearchOption.AllDirectories))
        {
            if (!string.Equals(Path.GetFileName(path), FileName, StringComparison.OrdinalIgnoreCase))
                continue;
            string relative = Path.GetRelativePath(root, Path.GetDirectoryName(path)!).Replace('\\', '/');
            if (relative is "." or "")
                continue;
            IReadOnlyDictionary<string, string> frontmatter = Frontmatter.Parse(AgentResourceFile.Read(path));
            skills.Add(new SkillDocumentSummary(relative, frontmatter.GetValueOrDefault("name"), frontmatter.GetValueOrDefault("description")));
        }

        return new SkillDocumentListResult(skills.OrderBy(skill => skill.Path, StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>skill 문서 전문을 조회한다.</summary>
    /// <param name="name">skill 폴더 이름</param>
    /// <returns>skill 문서</returns>
    /// <exception cref="McpException">이름이 잘못됐거나 문서가 없다.</exception>
    [McpServerTool(Name = "get_skill_document", UseStructuredContent = true,
        ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("skill 이름으로 SKILL.md 전문을 조회한다. 없거나 경로가 skills 밖이면 오류를 반환한다.")]
    public SkillDocument GetSkillDocument(
        [Description("skills 아래 skill 폴더 이름(예: create-pr 또는 create-pr/SKILL.md)")] string name)
    {
        (string skill, string full) = Resolve(name);
        if (!File.Exists(full))
            throw new McpException($"Skill document not found: {skill}");
        return new SkillDocument(skill, AgentResourceFile.Read(full));
    }

    /// <summary>skill 문서를 새로 만든다.</summary>
    /// <param name="name">skill 폴더 이름</param>
    /// <param name="content">front matter를 포함한 SKILL.md 전문</param>
    /// <returns>만든 문서 경로</returns>
    /// <exception cref="McpException">이름이 잘못됐거나 이미 있다.</exception>
    [McpServerTool(Name = "create_skill_document", UseStructuredContent = true,
        ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
    [Description("skills/<name>/SKILL.md를 새로 만든다. content는 '---\\nname: ...\\ndescription: ...\\n---' front matter를 포함한 전문이며 이미 있으면 오류를 반환한다.")]
    public AgentResourceWriteResult CreateSkillDocument(
        [Description("skills 아래 skill 폴더 이름(예: create-pr)")] string name,
        [Description("front matter를 포함한 SKILL.md 전문")] string content)
    {
        (string skill, string full) = Resolve(name);
        AgentResourceFile.CreateNew(full, content ?? string.Empty, $"Skill document already exists: {skill}");
        return new AgentResourceWriteResult($"{skill}/{FileName}");
    }

    /// <summary>기존 skill 문서 전문을 바꾼다.</summary>
    /// <param name="name">skill 폴더 이름</param>
    /// <param name="content">front matter를 포함한 SKILL.md 전문</param>
    /// <returns>바꾼 문서 경로</returns>
    /// <exception cref="McpException">이름이 잘못됐거나 문서가 없다.</exception>
    [McpServerTool(Name = "update_skill_document", UseStructuredContent = true,
        ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = false)]
    [Description("기존 skills/<name>/SKILL.md 전문을 content로 바꾼다. 없으면 오류를 반환한다.")]
    public AgentResourceWriteResult UpdateSkillDocument(
        [Description("skills 아래 skill 폴더 이름(예: create-pr)")] string name,
        [Description("front matter를 포함한 SKILL.md 전문")] string content)
    {
        (string skill, string full) = Resolve(name);
        if (!File.Exists(full))
            throw new McpException($"Skill document not found: {skill}");
        AgentResourceFile.Overwrite(full, content ?? string.Empty);
        return new AgentResourceWriteResult($"{skill}/{FileName}");
    }

    /// <summary>skill 문서를 삭제하고 빈 skill 폴더도 지운다.</summary>
    /// <param name="name">skill 폴더 이름</param>
    /// <returns>삭제한 문서 경로</returns>
    /// <exception cref="McpException">이름이 잘못됐거나 문서가 없다.</exception>
    [McpServerTool(Name = "delete_skill_document", UseStructuredContent = true,
        ReadOnly = false, Idempotent = false, Destructive = true, OpenWorld = false)]
    [Description("skills/<name>/SKILL.md를 삭제하고 폴더가 비면 폴더도 지운다. 없으면 오류를 반환한다.")]
    public AgentResourceWriteResult DeleteSkillDocument(
        [Description("skills 아래 skill 폴더 이름(예: create-pr)")] string name)
    {
        (string skill, string full) = Resolve(name);
        if (!File.Exists(full))
            throw new McpException($"Skill document not found: {skill}");
        AgentResourceFile.DeleteAndPruneDirectory(full, SkillsRoot);
        return new AgentResourceWriteResult($"{skill}/{FileName}");
    }

    private (string Skill, string Full) Resolve(string name)
    {
        string relative = AgentResourcePath.Normalize(name);
        if (relative.Equals(FileName, StringComparison.OrdinalIgnoreCase)
            || relative.EndsWith("/" + FileName, StringComparison.OrdinalIgnoreCase))
        {
            relative = relative[..^FileName.Length].TrimEnd('/');
        }
        string directory = AgentResourcePath.Resolve(SkillsRoot, relative, "skills");
        return (relative, Path.Combine(directory, FileName));
    }
}
