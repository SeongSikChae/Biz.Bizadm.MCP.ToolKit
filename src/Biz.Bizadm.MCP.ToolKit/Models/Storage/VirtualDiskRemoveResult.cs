namespace Biz.Bizadm.MCP.ToolKit.Models.Storage;

/// <summary>삭제한 가상 디스크</summary>
/// <param name="FriendlyName">삭제한 가상 디스크 이름</param>
/// <param name="UniqueId">삭제한 가상 디스크 고유 ID</param>
public sealed record VirtualDiskRemoveResult(string FriendlyName, string UniqueId);
