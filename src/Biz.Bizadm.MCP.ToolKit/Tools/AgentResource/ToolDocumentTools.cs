using System.ComponentModel;
using Biz.Bizadm.MCP.ToolKit.AgentResource;
using Biz.Bizadm.MCP.ToolKit.Models.AgentResource;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Biz.Bizadm.MCP.ToolKit.Tools.AgentResource;

/// <summary>코딩 에이전트 tool 레지스트리 문서({RootPath}/tools/&lt;name&gt;/TOOL.md) Tool</summary>
/// <param name="options">문서 루트 옵션</param>
[McpServerToolType]
public sealed class ToolDocumentTools(AgentResourceOptions options)
{
    private const string FileName = "TOOL.md";

    private string ToolsRoot => Path.Combine(options.RootPath, "tools");

    /// <summary>tool 문서 목록을 조회한다.</summary>
    /// <returns>tool 문서 목록</returns>
    [McpServerTool(Name = "list_tool_documents", UseStructuredContent = true,
        ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("tools 아래 tool 레지스트리 문서(tools/<name>/TOOL.md) 목록을 description·language·location과 함께 조회한다.")]
    public ToolDocumentListResult ListToolDocuments()
    {
        string root = ToolsRoot;
        if (!Directory.Exists(root))
            return new ToolDocumentListResult([]);

        List<ToolDocumentSummary> tools = [];
        foreach (string path in Directory.EnumerateFiles(root, FileName, SearchOption.AllDirectories))
        {
            if (!string.Equals(Path.GetFileName(path), FileName, StringComparison.OrdinalIgnoreCase))
                continue;
            string relative = Path.GetRelativePath(root, Path.GetDirectoryName(path)!).Replace('\\', '/');
            if (relative is "." or "")
                continue;
            IReadOnlyDictionary<string, string> frontmatter = Frontmatter.Parse(AgentResourceFile.Read(path));
            tools.Add(new ToolDocumentSummary(
                relative,
                frontmatter.GetValueOrDefault("name"),
                frontmatter.GetValueOrDefault("description"),
                frontmatter.GetValueOrDefault("language"),
                frontmatter.GetValueOrDefault("location")));
        }

        return new ToolDocumentListResult(tools.OrderBy(tool => tool.Path, StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>tool 사용법을 조회한다.</summary>
    /// <param name="name">tool 폴더 이름</param>
    /// <returns>tool 이름·위치·사용법</returns>
    /// <exception cref="McpException">이름이 잘못됐거나 문서가 없다.</exception>
    [McpServerTool(Name = "get_tool_document_usage", UseStructuredContent = true,
        ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("등록된 tool의 위치(location)와 사용법(TOOL.md 본문)을 이름으로 조회한다. 없으면 오류를 반환한다.")]
    public ToolDocumentUsage GetToolDocumentUsage(
        [Description("tools 아래 tool 폴더 이름(예: xml-parser 또는 xml-parser/TOOL.md)")] string name)
    {
        (string tool, string full) = Resolve(name);
        if (!File.Exists(full))
            throw new McpException($"Tool document not found: {tool}");
        string content = AgentResourceFile.Read(full);
        return new ToolDocumentUsage(tool, Frontmatter.Parse(content).GetValueOrDefault("location"), Frontmatter.ExtractBody(content));
    }

    /// <summary>tool 문서 등록 가이드를 조회한다.</summary>
    /// <returns>등록 가이드</returns>
    [McpServerTool(Name = "get_tool_document_registration_guide", UseStructuredContent = true,
        ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("tool 문서 등록에 필요한 필드·저장 위치·예시를 조회한다. register_tool_document 호출 전에 사용한다.")]
    public ToolRegistrationGuide GetToolDocumentRegistrationGuide() => new(
        Purpose: "Register AGENTS.md-style metadata for an already-developed reusable utility or script used by coding agents. This does not create or copy the tool binary/script itself.",
        StorageLayout: "tools/<name>/TOOL.md",
        RequiredFields:
        [
            new("name", "Tool directory name under tools (e.g. xml-parser). Becomes tools/<name>/TOOL.md."),
            new("description", "Short summary shown in the tool list (when to use this tool)."),
            new("language", "How the tool was developed. Free-form string (e.g. C#, PowerShell, Python)."),
            new("location", "Path or reference to the tool artifact. Existence is not validated."),
            new("usage", "Instructions describing how to invoke or use the tool. Stored as the TOOL.md body."),
        ],
        LanguageExamples: ["C#", "PowerShell", "Python"],
        Example: new("xml-parser", "Parses XML into domain objects", "C#", "tools/xml-parser/XmlParser.dll", "dotnet XmlParser.dll --input sample.xml"),
        RelatedTools: new("register_tool_document", "update_tool_document", "delete_tool_document", "list_tool_documents", "get_tool_document_usage"));

    /// <summary>tool 문서를 새로 등록한다.</summary>
    /// <param name="name">tool 폴더 이름</param>
    /// <param name="description">tool 설명</param>
    /// <param name="language">개발 언어</param>
    /// <param name="location">도구 위치</param>
    /// <param name="usage">사용법(TOOL.md 본문)</param>
    /// <returns>등록한 문서 경로</returns>
    /// <exception cref="McpException">이름·필드가 잘못됐거나 이미 있다.</exception>
    [McpServerTool(Name = "register_tool_document", UseStructuredContent = true,
        ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
    [Description("이미 만든 도구의 메타데이터를 tools/<name>/TOOL.md로 등록한다. 도구 파일 자체는 만들거나 복사하지 않으며 이미 있으면 오류를 반환한다.")]
    public AgentResourceWriteResult RegisterToolDocument(
        [Description("tools 아래 tool 폴더 이름(예: xml-parser)")] string name,
        [Description("목록에 보일 짧은 설명(언제 쓰는지)")] string description,
        [Description("개발 언어(예: C#, PowerShell, Python)")] string language,
        [Description("도구 파일 경로 또는 참조, 존재 여부는 확인하지 않는다")] string location,
        [Description("도구 사용법, TOOL.md 본문으로 저장한다")] string usage)
    {
        RequireFields(description, language, location, usage);
        (string tool, string full) = Resolve(name);
        AgentResourceFile.CreateNew(full, BuildContent(tool, description, language, location, usage), $"Tool document already exists: {tool}");
        return new AgentResourceWriteResult($"{tool}/{FileName}");
    }

    /// <summary>기존 tool 문서를 바꾼다.</summary>
    /// <param name="name">tool 폴더 이름</param>
    /// <param name="description">tool 설명</param>
    /// <param name="language">개발 언어</param>
    /// <param name="location">도구 위치</param>
    /// <param name="usage">사용법(TOOL.md 본문)</param>
    /// <returns>바꾼 문서 경로</returns>
    /// <exception cref="McpException">이름·필드가 잘못됐거나 문서가 없다.</exception>
    [McpServerTool(Name = "update_tool_document", UseStructuredContent = true,
        ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = false)]
    [Description("등록된 tools/<name>/TOOL.md의 description·language·location·usage를 모두 바꾼다. 없으면 오류를 반환한다.")]
    public AgentResourceWriteResult UpdateToolDocument(
        [Description("tools 아래 tool 폴더 이름(예: xml-parser)")] string name,
        [Description("목록에 보일 짧은 설명(언제 쓰는지)")] string description,
        [Description("개발 언어(예: C#, PowerShell, Python)")] string language,
        [Description("도구 파일 경로 또는 참조, 존재 여부는 확인하지 않는다")] string location,
        [Description("도구 사용법, TOOL.md 본문으로 저장한다")] string usage)
    {
        RequireFields(description, language, location, usage);
        (string tool, string full) = Resolve(name);
        if (!File.Exists(full))
            throw new McpException($"Tool document not found: {tool}");
        AgentResourceFile.Overwrite(full, BuildContent(tool, description, language, location, usage));
        return new AgentResourceWriteResult($"{tool}/{FileName}");
    }

    /// <summary>tool 문서를 삭제하고 빈 tool 폴더도 지운다.</summary>
    /// <param name="name">tool 폴더 이름</param>
    /// <returns>삭제한 문서 경로</returns>
    /// <exception cref="McpException">이름이 잘못됐거나 문서가 없다.</exception>
    [McpServerTool(Name = "delete_tool_document", UseStructuredContent = true,
        ReadOnly = false, Idempotent = false, Destructive = true, OpenWorld = false)]
    [Description("tools/<name>/TOOL.md를 삭제하고 폴더가 비면 폴더도 지운다. location이 가리키는 도구 파일은 건드리지 않으며 없으면 오류를 반환한다.")]
    public AgentResourceWriteResult DeleteToolDocument(
        [Description("tools 아래 tool 폴더 이름(예: xml-parser)")] string name)
    {
        (string tool, string full) = Resolve(name);
        if (!File.Exists(full))
            throw new McpException($"Tool document not found: {tool}");
        AgentResourceFile.DeleteAndPruneDirectory(full, ToolsRoot);
        return new AgentResourceWriteResult($"{tool}/{FileName}");
    }

    private (string Tool, string Full) Resolve(string name)
    {
        string relative = AgentResourcePath.Normalize(name);
        if (relative.Equals(FileName, StringComparison.OrdinalIgnoreCase)
            || relative.EndsWith("/" + FileName, StringComparison.OrdinalIgnoreCase))
        {
            relative = relative[..^FileName.Length].TrimEnd('/');
        }
        string directory = AgentResourcePath.Resolve(ToolsRoot, relative, "tools");
        return (relative, Path.Combine(directory, FileName));
    }

    private static void RequireFields(string description, string language, string location, string usage)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new McpException("description is required");
        if (string.IsNullOrWhiteSpace(language))
            throw new McpException("language is required");
        if (string.IsNullOrWhiteSpace(location))
            throw new McpException("location is required");
        if (string.IsNullOrWhiteSpace(usage))
            throw new McpException("usage is required");
    }

    private static string BuildContent(string tool, string description, string language, string location, string usage) =>
        Frontmatter.Build([("name", tool), ("description", description), ("language", language), ("location", location)], usage);
}
