namespace Biz.Bizadm.MCP.ToolKit.Models.Storage;

/// <summary>저장소 풀 목록 조회 결과</summary>
/// <param name="StoragePools">저장소 풀 목록</param>
public sealed record StoragePoolListResult(IReadOnlyList<StoragePoolInfo> StoragePools);
