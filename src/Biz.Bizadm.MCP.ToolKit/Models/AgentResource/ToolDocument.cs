using System.Text.Json.Serialization;

namespace Biz.Bizadm.MCP.ToolKit.Models.AgentResource;

/// <summary>tool 문서 목록 항목</summary>
/// <param name="Path">tools 기준 tool 폴더 이름(경로)</param>
/// <param name="Name">front matter name, 없으면 null</param>
/// <param name="Description">front matter description, 없으면 null</param>
/// <param name="Language">front matter language(개발 언어), 없으면 null</param>
/// <param name="Location">front matter location(도구 위치), 없으면 null</param>
/// <remarks>출력 스키마가 모든 속성을 required로 두므로 nullable 속성도 null을 그대로 쓴다.</remarks>
public sealed record ToolDocumentSummary(
    string Path,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Description,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Language,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Location);

/// <summary>tool 문서 목록</summary>
/// <param name="Tools">tool 문서 목록(경로 순)</param>
public sealed record ToolDocumentListResult(IReadOnlyList<ToolDocumentSummary> Tools);

/// <summary>tool 사용법</summary>
/// <param name="Name">tool 폴더 이름</param>
/// <param name="Location">front matter location, 없으면 null</param>
/// <param name="Usage">TOOL.md 본문(사용법)</param>
/// <remarks>출력 스키마가 모든 속성을 required로 두므로 nullable 속성도 null을 그대로 쓴다.</remarks>
public sealed record ToolDocumentUsage(
    string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Location,
    string Usage);

/// <summary>tool 등록 필수 필드</summary>
/// <param name="Name">필드 이름</param>
/// <param name="Description">필드 설명</param>
public sealed record ToolRegistrationField(string Name, string Description);

/// <summary>tool 등록 예시</summary>
/// <param name="Name">tool 이름</param>
/// <param name="Description">설명</param>
/// <param name="Language">개발 언어</param>
/// <param name="Location">도구 위치</param>
/// <param name="Usage">사용법</param>
public sealed record ToolRegistrationExample(string Name, string Description, string Language, string Location, string Usage);

/// <summary>tool 문서 관련 Tool 이름</summary>
/// <param name="Register">등록 Tool</param>
/// <param name="Update">수정 Tool</param>
/// <param name="Delete">삭제 Tool</param>
/// <param name="List">목록 Tool</param>
/// <param name="GetUsage">사용법 조회 Tool</param>
public sealed record ToolRegistrationRelatedTools(string Register, string Update, string Delete, string List, string GetUsage);

/// <summary>tool 문서 등록 가이드</summary>
/// <param name="Purpose">목적</param>
/// <param name="StorageLayout">저장 위치</param>
/// <param name="RequiredFields">필수 필드</param>
/// <param name="LanguageExamples">language 예시</param>
/// <param name="Example">등록 예시</param>
/// <param name="RelatedTools">관련 Tool 이름</param>
public sealed record ToolRegistrationGuide(
    string Purpose,
    string StorageLayout,
    IReadOnlyList<ToolRegistrationField> RequiredFields,
    IReadOnlyList<string> LanguageExamples,
    ToolRegistrationExample Example,
    ToolRegistrationRelatedTools RelatedTools);
