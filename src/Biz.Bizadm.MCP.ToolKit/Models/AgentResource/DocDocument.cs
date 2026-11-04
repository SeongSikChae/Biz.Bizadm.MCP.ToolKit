using System.Text.Json.Serialization;

namespace Biz.Bizadm.MCP.ToolKit.Models.AgentResource;

/// <summary>docs 문서 목록 항목</summary>
/// <param name="Path">docs 기준 상대 경로({category}/NNNN-{feature}-{phase}.md)</param>
/// <param name="Category">카테고리(폴더 이름)</param>
/// <param name="Number">카테고리 안 4자리 순번</param>
/// <param name="Status">front matter status, 없으면 null</param>
/// <param name="Feature">front matter feature, 없으면 null</param>
/// <param name="Phase">front matter phase, 없으면 null</param>
/// <remarks>출력 스키마가 모든 속성을 required로 두므로 nullable 속성도 null을 그대로 쓴다.</remarks>
public sealed record DocDocumentSummary(
    string Path,
    string Category,
    int Number,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Feature,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Phase);

/// <summary>docs 문서 목록</summary>
/// <param name="Docs">docs 문서 목록(카테고리, 순번 순)</param>
public sealed record DocDocumentListResult(IReadOnlyList<DocDocumentSummary> Docs);

/// <summary>docs 문서 전문</summary>
/// <param name="Path">docs 기준 상대 경로</param>
/// <param name="Category">카테고리</param>
/// <param name="Number">순번</param>
/// <param name="Content">front matter를 포함한 파일 전문</param>
public sealed record DocDocument(string Path, string Category, int Number, string Content);
