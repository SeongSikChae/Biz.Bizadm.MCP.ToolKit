using System.Text.Json.Serialization;

namespace Biz.Bizadm.MCP.ToolKit.Models.Storage;

/// <summary>가상 디스크 용도(MSFT_VirtualDisk.Usage)</summary>
[JsonConverter(typeof(JsonStringEnumConverter<VirtualDiskUsage>))]
public enum VirtualDiskUsage : ushort
{
    /// <summary>알 수 없음</summary>
    Unknown = 0,

    /// <summary>기타</summary>
    Other = 1,

    /// <summary>제한 없음</summary>
    Unrestricted = 2,

    /// <summary>컴퓨터 시스템(블록 서버) 예약</summary>
    ReservedForComputerSystem = 3,

    /// <summary>복제 서비스 예약</summary>
    ReservedByReplicationServices = 4,

    /// <summary>마이그레이션 서비스 예약</summary>
    ReservedByMigrationServices = 5,

    /// <summary>로컬 복제 원본</summary>
    LocalReplicaSource = 6,

    /// <summary>원격 복제 원본</summary>
    RemoteReplicaSource = 7,

    /// <summary>로컬 복제 대상</summary>
    LocalReplicaTarget = 8,

    /// <summary>원격 복제 대상</summary>
    RemoteReplicaTarget = 9,

    /// <summary>로컬 복제 원본 또는 대상</summary>
    LocalReplicaSourceOrTarget = 10,

    /// <summary>원격 복제 원본 또는 대상</summary>
    RemoteReplicaSourceOrTarget = 11,

    /// <summary>델타 복제 대상</summary>
    DeltaReplicaTarget = 12,

    /// <summary>요소 구성 요소</summary>
    ElementComponent = 13,

    /// <summary>풀 구성원 예약</summary>
    ReservedAsPoolContributor = 14,

    /// <summary>복합 볼륨 구성원</summary>
    CompositeVolumeMember = 15,

    /// <summary>복합 가상 디스크 구성원</summary>
    CompositeVirtualDiskMember = 16,

    /// <summary>예비용 예약</summary>
    ReservedForSparing = 17,

    /// <summary>구성</summary>
    Configuration = 18,

    /// <summary>데이터</summary>
    Data = 19,

    /// <summary>저널</summary>
    Journal = 20,

    /// <summary>읽기 캐시</summary>
    ReadCache = 21,

    /// <summary>캐시 로그</summary>
    CacheLog = 22,

    /// <summary>더티 영역 추적</summary>
    DirtyRegionTracking = 23,

    /// <summary>스트라이프 상태 추적</summary>
    StripeStateTracking = 24,

    /// <summary>스냅숏</summary>
    Snapshot = 25,

    /// <summary>단순</summary>
    Simple = 26,

    /// <summary>유효 데이터 추적</summary>
    ValidDataTracking = 27,

    /// <summary>캐시 라인</summary>
    CacheLines = 28,
}
