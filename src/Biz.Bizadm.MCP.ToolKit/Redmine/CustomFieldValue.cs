using System.ComponentModel;

namespace Biz.Bizadm.MCP.ToolKit.Redmine;

/// <summary>Redmine 사용자 정의 필드 값. Value(단일)와 Values(다중) 중 하나만 지정한다.</summary>
/// <param name="Id">사용자 정의 필드 ID</param>
/// <param name="Value">단일 값</param>
/// <param name="Values">다중 값</param>
public sealed record CustomFieldValue(
    [property: Description("사용자 정의 필드 ID")] int Id,
    [property: Description("단일 값. Values와 함께 쓰지 않는다.")] string? Value = null,
    [property: Description("다중 값. Value와 함께 쓰지 않는다.")] IReadOnlyList<string>? Values = null);
