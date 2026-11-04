namespace Biz.Bizadm.MCP.ToolKit.Models.Storage;

/// <summary>가상 디스크 목록 조회 결과</summary>
/// <param name="VirtualDisks">가상 디스크 목록</param>
public sealed record VirtualDiskListResult(IReadOnlyList<VirtualDiskInfo> VirtualDisks);
