using System.Text.Json.Serialization;

namespace Biz.Bizadm.MCP.ToolKit.Models.Storage;

/// <summary>저장소 풀 정보(MSFT_StoragePool)</summary>
/// <remarks>출력 스키마가 모든 속성을 required로 두므로 nullable 속성도 null을 그대로 쓴다.</remarks>
/// <param name="FriendlyName">저장소 풀 이름</param>
/// <param name="UniqueId">저장소 풀 고유 ID</param>
/// <param name="ObjectId">CIM 개체 ID</param>
/// <param name="OperationalStatus">운영 상태 목록</param>
/// <param name="HealthStatus">상태</param>
/// <param name="IsPrimordial">Primordial 풀 여부(풀에 추가 가능한 디스크 모음)</param>
/// <param name="IsReadOnly">읽기 전용 여부</param>
/// <param name="IsClustered">클러스터 풀 여부</param>
/// <param name="Size">전체 용량(byte)</param>
/// <param name="AllocatedSize">할당된 용량(byte)</param>
/// <param name="LogicalSectorSize">논리 섹터 크기(byte), Primordial 풀은 null</param>
/// <param name="PhysicalSectorSize">물리 섹터 크기(byte), Primordial 풀은 null</param>
/// <param name="ResiliencySettingNameDefault">기본 복원력 설정 이름(예: Mirror, Parity, Simple)</param>
/// <param name="ProvisioningTypeDefault">기본 프로비저닝 유형</param>
/// <param name="Usage">용도</param>
/// <param name="Version">풀 버전(CIM 원값)</param>
public sealed record StoragePoolInfo(
    string FriendlyName,
    string UniqueId,
    string ObjectId,
    IReadOnlyList<StorageOperationalStatus> OperationalStatus,
    StorageHealthStatus HealthStatus,
    bool IsPrimordial,
    bool IsReadOnly,
    bool IsClustered,
    ulong Size,
    ulong AllocatedSize,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] ulong? LogicalSectorSize,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] ulong? PhysicalSectorSize,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ResiliencySettingNameDefault,
    StorageProvisioningType ProvisioningTypeDefault,
    StoragePoolUsage Usage,
    ushort Version);
