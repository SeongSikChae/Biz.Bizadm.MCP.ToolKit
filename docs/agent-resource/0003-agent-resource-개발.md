---
status: done
category: agent-resource
feature: agent-resource
phase: 개발
---

# agent-resource 개발

선행: `0001-agent-resource-방안.md`, `0002-agent-resource-설계.md`

공통: 공개 멤버에는 한국어 XML 문서 주석을 단다(`GenerateDocumentationFile`). Tool Description은 한국어 한 문장으로, 예외 메시지는 영어로 쓴다. 검증 명령은 저장소 루트에서 실행한다.

## 단위 1. 옵션·공통 helper

**대상**: `src/Biz.Bizadm.MCP.ToolKit/AgentResource/AgentResourceOptions.cs`, `AgentResourcePath.cs`, `Frontmatter.cs`, `AgentResourceFile.cs`, `Models/AgentResource/AgentResourceWriteResult.cs`

**코드 초안**

```csharp
// AgentResourcePath.cs
internal static class AgentResourcePath
{
    public static string Normalize(string value) => value.Trim().Replace('\\', '/').TrimStart('/');

    /// kindRoot 아래 상대 경로를 절대 경로로 바꾼다. 이탈하면 McpException.
    public static string Resolve(string kindRoot, string relative, string kindName)
    {
        if (string.IsNullOrWhiteSpace(relative)) throw new McpException($"{kindName} name is required");
        if (Path.IsPathRooted(relative) || relative.Contains(':')) throw new McpException("Absolute paths are not allowed");
        if (relative.Split('/').Any(s => s == "..")) throw new McpException($"Path escapes the {kindName} directory");
        string full = Path.GetFullPath(Path.Combine(kindRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        string rootWithSep = kindRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
            throw new McpException($"Path escapes the {kindName} directory");
        return full;
    }
}

// Frontmatter.cs
internal static class Frontmatter
{
    private static readonly Regex Pattern = new(@"^---\r?\n(.*?)\r?\n---(?:\r?\n|$)", RegexOptions.Singleline | RegexOptions.Compiled);

    public static IReadOnlyDictionary<string, string> Parse(string content) { /* 참고 서버 TryParseFrontmatter: 첫 ':' 분리, '#' 무시, 따옴표 제거, OrdinalIgnoreCase 키 */ }
    public static string ExtractBody(string content) { /* 매치 뒤 Trim, 매치가 없으면 전체 Trim */ }
    public static string Build(IEnumerable<(string Key, string Value)> fields, string body)
    {
        StringBuilder sb = new("---\n");
        foreach ((string key, string value) in fields) sb.Append(key).Append(": ").Append(OneLine(value)).Append('\n');
        sb.Append("---\n\n").Append(body.Trim().Replace("\r\n", "\n").Replace('\r', '\n')).Append('\n');
        return sb.ToString();
    }
    public static string OneLine(string v) => v.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
}

// AgentResourceFile.cs
internal static class AgentResourceFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    public static void CreateNew(string path, string content)   // 이미 있으면 IOException, 호출부에서 McpException으로 감싼다
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write);
        using StreamWriter writer = new(stream, Utf8NoBom);
        writer.Write(content);
    }
    public static void Overwrite(string path, string content) => File.WriteAllText(path, content, Utf8NoBom);
    public static string Read(string path) => File.ReadAllText(path, Encoding.UTF8);
    public static void DeleteAndPruneDirectory(string path, string kindRoot) { /* 파일 삭제, 부모 폴더가 비었고 kindRoot가 아니면 삭제 */ }
}
```

**검증**: `dotnet build Biz.Bizadm.MCP.ToolKit.slnx` 경고 0, 오류 0

**선행 단위**: 없음

## 단위 2. rule Tool, 등록 확장 메서드

**대상**: `Tools/AgentResource/RuleDocumentTools.cs`, `Models/AgentResource/RuleDocument*.cs`, `AgentResourceMcpServerBuilderExtensions.cs`, `tests/.../AgentResource/RuleDocumentToolsTests.cs`, `tests/.../AgentResource/TempRoot.cs`(테스트용 임시 루트)

**코드 초안**

```csharp
[McpServerToolType]
public sealed class RuleDocumentTools(AgentResourceOptions options)
{
    private string RulesRoot => Path.Combine(options.RootPath, "rules");

    [McpServerTool(Name = "get_rule_document", UseStructuredContent = true,
        ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("rules 아래 상대 경로로 rule 문서(.md) 전문을 조회한다. 없거나 경로가 rules 밖이면 오류를 반환한다.")]
    public RuleDocument GetRuleDocument(
        [Description("rules 기준 상대 경로(예: dotnet/naming.md, AGENTS.md), 확장자 생략 시 .md")] string name)
    {
        (string relative, string full) = ResolveRule(name);
        if (!File.Exists(full)) throw new McpException($"Rule document not found: {relative}");
        return new RuleDocument(relative, AgentResourceFile.Read(full));
    }

    private (string Relative, string Full) ResolveRule(string name)
    {
        string relative = AgentResourcePath.Normalize(name ?? string.Empty);
        if (Path.GetExtension(relative).Length == 0) relative += ".md";
        if (!relative.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) throw new McpException("Only .md rule documents are allowed");
        return (relative, AgentResourcePath.Resolve(RulesRoot, relative, "rules"));
    }
    // ... ListRuleDocuments: EnumerateFiles("*.md", AllDirectories), Frontmatter.Parse(...)["description"], Path 정렬
    // ... Create: AgentResourceFile.CreateNew, IOException → McpException("Rule document already exists: ...")
}

// AgentResourceMcpServerBuilderExtensions.cs (설계의 등록 코드, 이 단위에서는 WithTools<RuleDocumentTools>()만)
```

```csharp
// RuleDocumentToolsTests.cs
[TestMethod]
public void CreateRuleDocument_ThenGet_ReturnsContent()
{
    using TempRoot root = new();
    RuleDocumentTools tools = new(root.Options);
    tools.CreateRuleDocument("dotnet/naming", "---\ndescription: d\n---\nbody");
    Assert.AreEqual("dotnet/naming.md", tools.ListRuleDocuments().Rules.Single().Path);
    Assert.AreEqual("d", tools.ListRuleDocuments().Rules.Single().Description);
    StringAssert.Contains(tools.GetRuleDocument("dotnet/naming.md").Content, "body");
}

[TestMethod]
[DataRow("../x.md")] [DataRow("C:/x.md")] [DataRow("a/../../x.md")]
public void GetRuleDocument_EscapingPath_Throws(string name) { ... Assert.ThrowsExactly<McpException>(...) }
```

**검증**: `dotnet build`, `dotnet test --filter FullyQualifiedName~RuleDocumentToolsTests` 통과. CRUD 왕복, 중복 create, 없는 문서, 경로 이탈, `.md`가 아닌 확장자를 확인한다. 등록 테스트로 `WithAgentResourceTools`가 상대 경로나 빈 `RootPath`를 받으면 `ArgumentException`인지 확인한다.

**선행 단위**: 1

## 단위 3. skill Tool

**대상**: `Tools/AgentResource/SkillDocumentTools.cs`, `Models/AgentResource/SkillDocument*.cs`, 확장 메서드에 `WithTools<SkillDocumentTools>()` 추가, `SkillDocumentToolsTests.cs`

**코드 초안**

```csharp
private (string Name, string Full) ResolveSkill(string name)
{
    string relative = AgentResourcePath.Normalize(name ?? string.Empty);
    if (relative.EndsWith("/SKILL.md", StringComparison.OrdinalIgnoreCase) || relative.Equals("SKILL.md", StringComparison.OrdinalIgnoreCase))
        relative = relative[..^"SKILL.md".Length].TrimEnd('/');
    string directory = AgentResourcePath.Resolve(SkillsRoot, relative, "skills");
    return (relative, Path.Combine(directory, "SKILL.md"));
}
// List: EnumerateFiles(SkillsRoot, "SKILL.md", AllDirectories), 루트 바로 아래 SKILL.md는 건너뜀, name·description
// Delete: AgentResourceFile.DeleteAndPruneDirectory(full, SkillsRoot)
```

**검증**: `dotnet test --filter FullyQualifiedName~SkillDocumentToolsTests` 통과. `create-pr`와 `create-pr/SKILL.md`가 같은 문서를 가리키는지, delete 후 빈 폴더가 지워지는지, 경로 이탈과 빈 이름을 거부하는지 확인한다.

**선행 단위**: 2

## 단위 4. tool Tool

**대상**: `Tools/AgentResource/ToolDocumentTools.cs`, `Models/AgentResource/ToolDocument*.cs`, `ToolRegistration*.cs`, 확장 메서드에 `WithTools<ToolDocumentTools>()` 추가, `ToolDocumentToolsTests.cs`

**코드 초안**

```csharp
public AgentResourceWriteResult RegisterToolDocument(string name, string description, string language, string location, string usage)
{
    RequireFields(description, language, location, usage);          // "description is required" ...
    (string toolName, string full) = ResolveTool(name);              // ResolveSkill과 같은 방식, TOOL.md
    string content = Frontmatter.Build(
        [("name", toolName), ("description", description), ("language", language), ("location", location)], usage);
    try { AgentResourceFile.CreateNew(full, content); }
    catch (IOException) when (File.Exists(full)) { throw new McpException($"Tool document already exists: {toolName}"); }
    return new AgentResourceWriteResult($"{toolName}/TOOL.md");
}

public ToolRegistrationGuide GetToolDocumentRegistrationGuide() => new(
    Purpose: "...참고 서버 문구...", StorageLayout: "tools/<name>/TOOL.md", RequiredFields: [...],
    LanguageExamples: ["C#", "PowerShell", "Python"], Example: new(...),
    RelatedTools: new("register_tool_document", "update_tool_document", "delete_tool_document",
                      "list_tool_documents", "get_tool_document_usage"));
```

**검증**: `dotnet test --filter FullyQualifiedName~ToolDocumentToolsTests` 통과. register 후 list에 `name`·`description`·`language`·`location`이 보이는지, `get_tool_document_usage`의 Usage가 본문과 같은지, 줄바꿈이 든 description이 한 줄로 저장되는지, 필수 필드 공백을 거부하는지, delete가 `location` 파일을 건드리지 않는지 확인한다.

**선행 단위**: 2

## 단위 5. docs Tool

**대상**: `Tools/AgentResource/DocDocumentTools.cs`, `Models/AgentResource/DocDocument*.cs`, 확장 메서드에 `WithTools<DocDocumentTools>()` 추가, `DocDocumentToolsTests.cs`

**코드 초안**

```csharp
private static readonly Regex CategoryPattern = new(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);
private static readonly Regex FilePattern = new(@"^(\d{4})-.+\.md$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

public AgentResourceWriteResult CreateDocDocument(string category, string feature, string phase, string content, string status = "draft")
{
    string directory = CategoryDirectory(category);                  // 패턴 검증 + Resolve(DocsRoot, category, "docs")
    ValidateSegment(feature, "feature", allowHyphen: true);
    ValidateSegment(phase, "phase", allowHyphen: false);
    ValidateStatus(status);
    int next = (Directory.Exists(directory)
        ? Directory.EnumerateFiles(directory, "*.md").Select(MatchNumber).Where(n => n > 0).DefaultIfEmpty(0).Max()
        : 0) + 1;
    if (next > 9999) throw new McpException($"No document number left in category {category}");
    string fileName = $"{next:D4}-{feature}-{phase}.md";
    string text = Frontmatter.Build([("status", status), ("category", category), ("feature", feature), ("phase", phase)], content);
    try { AgentResourceFile.CreateNew(Path.Combine(directory, fileName), text); }
    catch (IOException) when (File.Exists(Path.Combine(directory, fileName))) { throw new McpException($"Docs document already exists: {category}/{fileName}"); }
    return new AgentResourceWriteResult($"{category}/{fileName}");
}

private string FindByNumber(string category, int number)
{
    if (number is < 1 or > 9999) throw new McpException("number must be between 1 and 9999");
    string directory = CategoryDirectory(category);
    string[] matches = Directory.Exists(directory) ? Directory.GetFiles(directory, $"{number:D4}-*.md") : [];
    return matches.Length switch
    {
        0 => throw new McpException($"Docs document not found: {category}/{number:D4}"),
        1 => matches[0],
        _ => throw new McpException($"Multiple docs documents match {category}/{number:D4}"),
    };
}

public AgentResourceWriteResult UpdateDocDocument(string category, int number, string content, string? status = null)
{
    string full = FindByNumber(category, number);
    IReadOnlyDictionary<string, string> fm = Frontmatter.Parse(AgentResourceFile.Read(full));
    if (status is not null) ValidateStatus(status);
    string text = Frontmatter.Build([("status", status ?? fm.GetValueOrDefault("status", "draft")), ("category", category),
        ("feature", fm.GetValueOrDefault("feature", "")), ("phase", fm.GetValueOrDefault("phase", ""))], content);
    AgentResourceFile.Overwrite(full, text);
    return new AgentResourceWriteResult($"{category}/{Path.GetFileName(full)}");
}
// List: EnumerateDirectories(DocsRoot) → 각 폴더 FilePattern 파일 → front matter status·feature·phase, category·feature 필터, (Category, Number) 정렬
```

동시 생성 잠금(회귀 수정):

```csharp
private static readonly ConcurrentDictionary<string, object> CategoryLocks = new(StringComparer.OrdinalIgnoreCase);

// CreateDocDocument: 입력 검증 뒤 번호 계산부터 CreateNew까지를 잠금 안에서 한다.
lock (CategoryLocks.GetOrAdd(directory, _ => new object()))
{
    int next = NextNumber(directory);   // 기존 최댓값 + 1
    // ... 9999 초과 검사, fileName, AgentResourceFile.CreateNew(...)
}
```

**검증**: `dotnet test --filter FullyQualifiedName~DocDocumentToolsTests` 통과. 다음을 확인한다.
- 같은 category에 feature가 서로 다른 문서 20개를 `Parallel.For`로 동시에 만들면 번호가 0001~0020으로 모두 다른지
- 빈 카테고리에서 0001과 0002가 순서대로 붙는지
- 기존 `0005-x-방안.md`가 있으면 다음 번호가 0006인지
- 생성한 파일의 front matter 4개 키
- update에서 status를 생략하면 기존 값이 유지되고, 지정하면 바뀌는지
- 목록 필터(category, feature)
- 잘못된 category(`Auth`, `a/b`)와 phase(`a-b`, 공백)를 거부하는지
- 없는 번호와 범위 밖 번호를 거부하는지
- delete 후 get이 예외인지

**선행 단위**: 2

## 단위 6. 등록·스키마 통합 테스트

**대상**: `tests/.../AgentResource/AgentResourceRegistrationTests.cs`

**코드 초안**

```csharp
[TestMethod]
public void WithAgentResourceTools_RegistersAll21Tools()
{
    ServiceCollection services = new();
    services.AddMcpServer().WithAgentResourceTools(o => o.RootPath = Path.GetTempPath());
    using ServiceProvider provider = services.BuildServiceProvider();
    List<string> names = provider.GetServices<McpServerTool>().Select(t => t.ProtocolTool.Name).ToList();
    Assert.HasCount(21, names);
    CollectionAssert.Contains(names, "create_doc_document");
}

[TestMethod]
public void WithBizadmToolKit_DoesNotRegisterAgentResourceTools() { /* "list_rule_documents" 없음 */ }

[TestMethod]
public void ToolDocumentListResult_SerializesAllRequiredProperties() { /* null 속성 포함 record 직렬화 → schema required 전부 존재 */ }
```

**검증**: `dotnet test Biz.Bizadm.MCP.ToolKit.slnx` 전체 통과, 기존 테스트 회귀 없음. 확인할 것은 Tool 21개, 힌트 표(ReadOnly·Destructive·Idempotent), nullable 속성 직렬화다.

**선행 단위**: 3, 4, 5

## 순서

1 → 2 → 3 → 4 → 5 → 6. 3·4·5는 서로 독립이지만 확장 메서드 파일을 함께 고치므로 차례대로 한다. 각 단위의 검증이 통과해야 다음 단위로 간다.
