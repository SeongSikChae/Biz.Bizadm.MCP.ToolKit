using System.Text.Json.Serialization;

namespace Biz.Bizadm.MCP.ToolKit.Models.AgentResource;

/// <summary>skill 문서 목록 항목</summary>
/// <param name="Path">skills 기준 skill 폴더 이름(경로)</param>
/// <param name="Name">front matter name, 없으면 null</param>
/// <param name="Description">front matter description, 없으면 null</param>
/// <remarks>출력 스키마가 모든 속성을 required로 두므로 nullable 속성도 null을 그대로 쓴다.</remarks>
public sealed record SkillDocumentSummary(
    string Path,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Description);

/// <summary>skill 문서 목록</summary>
/// <param name="Skills">skill 문서 목록(경로 순)</param>
public sealed record SkillDocumentListResult(IReadOnlyList<SkillDocumentSummary> Skills);

/// <summary>skill 문서(SKILL.md) 전문</summary>
/// <param name="Path">skills 기준 skill 폴더 이름(경로)</param>
/// <param name="Content">front matter를 포함한 SKILL.md 전문</param>
public sealed record SkillDocument(string Path, string Content);
