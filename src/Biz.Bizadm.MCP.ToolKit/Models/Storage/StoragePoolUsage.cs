using System.Text.Json.Serialization;

namespace Biz.Bizadm.MCP.ToolKit.Models.Storage;

/// <summary>저장소 풀 용도(MSFT_StoragePool.Usage)</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StoragePoolUsage>))]
public enum StoragePoolUsage : ushort
{
    /// <summary>알 수 없음</summary>
    Unknown = 0,

    /// <summary>기타</summary>
    Other = 1,

    /// <summary>제한 없음</summary>
    Unrestricted = 2,

    /// <summary>ComputerSystem(블록 서버) 예약</summary>
    ReservedForComputerSystem = 3,

    /// <summary>델타 복제본 컨테이너 예약</summary>
    ReservedAsDeltaReplicaContainer = 4,

    /// <summary>마이그레이션 서비스 예약</summary>
    ReservedForMigrationServices = 5,

    /// <summary>로컬 복제 서비스 예약</summary>
    ReservedForLocalReplicationServices = 6,

    /// <summary>원격 복제 서비스 예약</summary>
    ReservedForRemoteReplicationServices = 7,

    /// <summary>스페어 예약</summary>
    ReservedForSparing = 8,
}
