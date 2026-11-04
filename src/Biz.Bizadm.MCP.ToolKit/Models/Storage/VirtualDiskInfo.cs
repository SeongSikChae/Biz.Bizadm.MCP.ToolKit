using System.Text.Json.Serialization;

namespace Biz.Bizadm.MCP.ToolKit.Models.Storage;

/// <summary>저장소 공간(가상 디스크) 정보(MSFT_VirtualDisk)</summary>
/// <remarks>출력 스키마가 모든 속성을 required로 두므로 nullable 속성도 null을 그대로 쓴다.</remarks>
/// <param name="FriendlyName">가상 디스크 이름</param>
/// <param name="UniqueId">가상 디스크 고유 ID</param>
/// <param name="ObjectId">CIM 개체 ID</param>
/// <param name="OperationalStatus">운영 상태 목록</param>
/// <param name="HealthStatus">상태</param>
/// <param name="ResiliencySettingName">복원력 설정 이름(예: Mirror, Parity, Simple)</param>
/// <param name="ProvisioningType">프로비저닝 유형</param>
/// <param name="Usage">용도</param>
/// <param name="Size">논리 용량(byte)</param>
/// <param name="AllocatedSize">할당된 용량(byte)</param>
/// <param name="FootprintOnPool">풀에서 차지하는 용량(byte)</param>
/// <param name="NumberOfDataCopies">데이터 복사본 수</param>
/// <param name="PhysicalDiskRedundancy">데이터 손실 없이 실패할 수 있는 물리 디스크 수</param>
/// <param name="NumberOfColumns">스트라이프 컬럼(물리 디스크) 수</param>
/// <param name="Interleave">스트립 크기(byte)</param>
/// <param name="ParityLayout">패리티 배치, 패리티가 아니면 null</param>
/// <param name="LogicalSectorSize">논리 섹터 크기(byte)</param>
/// <param name="PhysicalSectorSize">물리 섹터 크기(byte)</param>
/// <param name="MediaType">미디어 유형</param>
/// <param name="Access">접근 유형</param>
/// <param name="DetachedReason">분리 사유</param>
/// <param name="IsTiered">계층 저장소 여부</param>
/// <param name="IsSnapshot">스냅숏 여부</param>
/// <param name="IsManualAttach">수동 연결 여부</param>
/// <param name="IsDeduplicationEnabled">중복 제거 사용 여부</param>
/// <param name="IsEnclosureAware">인클로저 인식 할당 여부</param>
/// <param name="WriteCacheSize">쓰기 캐시 크기(byte)</param>
public sealed record VirtualDiskInfo(
    string FriendlyName,
    string UniqueId,
    string ObjectId,
    IReadOnlyList<VirtualDiskOperationalStatus> OperationalStatus,
    StorageHealthStatus HealthStatus,
    string ResiliencySettingName,
    StorageProvisioningType ProvisioningType,
    VirtualDiskUsage Usage,
    ulong Size,
    ulong AllocatedSize,
    ulong FootprintOnPool,
    ushort NumberOfDataCopies,
    ushort PhysicalDiskRedundancy,
    ushort NumberOfColumns,
    ulong Interleave,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] VirtualDiskParityLayout? ParityLayout,
    ulong LogicalSectorSize,
    ulong PhysicalSectorSize,
    VirtualDiskMediaType MediaType,
    VirtualDiskAccess Access,
    VirtualDiskDetachedReason DetachedReason,
    bool IsTiered,
    bool IsSnapshot,
    bool IsManualAttach,
    bool IsDeduplicationEnabled,
    bool IsEnclosureAware,
    ulong WriteCacheSize);
