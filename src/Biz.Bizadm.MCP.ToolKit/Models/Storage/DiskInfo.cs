namespace Biz.Bizadm.MCP.ToolKit.Models.Storage;

/// <summary>디스크 정보(MSFT_Disk)</summary>
/// <param name="Number">디스크 번호</param>
/// <param name="FriendlyName">디스크 이름</param>
/// <param name="UniqueId">디스크 고유 ID</param>
/// <param name="PartitionStyle">파티션 스타일</param>
/// <param name="Size">전체 크기(byte)</param>
/// <param name="AllocatedSize">파티션에 할당된 크기(byte)</param>
/// <param name="LargestFreeExtent">가장 큰 빈 공간(byte)</param>
/// <param name="NumberOfPartitions">파티션 수</param>
/// <param name="LogicalSectorSize">논리 섹터 크기(byte)</param>
/// <param name="PhysicalSectorSize">물리 섹터 크기(byte)</param>
/// <param name="IsOffline">오프라인 여부</param>
/// <param name="IsReadOnly">읽기 전용 여부</param>
/// <param name="HealthStatus">상태</param>
public sealed record DiskInfo(
    uint Number,
    string FriendlyName,
    string UniqueId,
    DiskPartitionStyle PartitionStyle,
    ulong Size,
    ulong AllocatedSize,
    ulong LargestFreeExtent,
    uint NumberOfPartitions,
    uint LogicalSectorSize,
    uint PhysicalSectorSize,
    bool IsOffline,
    bool IsReadOnly,
    StorageHealthStatus HealthStatus);
