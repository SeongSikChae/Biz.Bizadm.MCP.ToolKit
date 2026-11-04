using System.Text.Json.Serialization;

namespace Biz.Bizadm.MCP.ToolKit.Models.Storage;

/// <summary>GPT 파티션 유형(MSFT_Disk.CreatePartition GptType)</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PartitionGptType>))]
public enum PartitionGptType
{
    /// <summary>EFI 시스템 파티션 {c12a7328-f81f-11d2-ba4b-00a0c93ec93b}</summary>
    SystemPartition,

    /// <summary>Microsoft 예약 파티션 {e3c9e316-0b5c-4db8-817d-f92df00215ae}</summary>
    MicrosoftReserved,

    /// <summary>기본 데이터 파티션 {ebd0a0a2-b9e5-4433-87c0-68b6b72699c7}</summary>
    BasicData,

    /// <summary>Microsoft 복구 파티션 {de94bba4-06d1-4d40-a16a-bfd50179d6ac}</summary>
    MicrosoftRecovery,
}
