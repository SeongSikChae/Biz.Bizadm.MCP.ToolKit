using System.Text.Json.Serialization;

namespace Biz.Bizadm.MCP.ToolKit.Models.AgentResource;

/// <summary>rule 문서 목록 항목</summary>
/// <param name="Path">rules 기준 상대 경로</param>
/// <param name="Description">front matter description, 없으면 null</param>
/// <remarks>출력 스키마가 모든 속성을 required로 두므로 nullable 속성도 null을 그대로 쓴다.</remarks>
public sealed record RuleDocumentSummary(
    string Path,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Description);

/// <summary>rule 문서 목록</summary>
/// <param name="Rules">rule 문서 목록(경로 순)</param>
public sealed record RuleDocumentListResult(IReadOnlyList<RuleDocumentSummary> Rules);

/// <summary>rule 문서 전문</summary>
/// <param name="Path">rules 기준 상대 경로</param>
/// <param name="Content">front matter를 포함한 파일 전문</param>
public sealed record RuleDocument(string Path, string Content);
