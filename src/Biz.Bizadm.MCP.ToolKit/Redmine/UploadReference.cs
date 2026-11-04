using System.ComponentModel;

namespace Biz.Bizadm.MCP.ToolKit.Redmine;

/// <summary>일감·위키에 첨부할 업로드 참조</summary>
/// <param name="Token">upload_file이 반환한 토큰</param>
/// <param name="Filename">첨부 파일명</param>
/// <param name="ContentType">Content-Type</param>
/// <param name="Description">첨부 설명</param>
public sealed record UploadReference(
    [property: Description("upload_file이 반환한 토큰")] string Token,
    [property: Description("첨부 파일명")] string Filename,
    [property: Description("Content-Type")] string? ContentType = null,
    [property: Description("첨부 설명")] string? Description = null);
