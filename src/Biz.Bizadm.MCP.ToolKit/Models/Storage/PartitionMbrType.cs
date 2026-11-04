using System.Text.Json.Serialization;

namespace Biz.Bizadm.MCP.ToolKit.Models.Storage;

/// <summary>MBR 파티션 유형(MSFT_Disk.CreatePartition MbrType)</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PartitionMbrType>))]
public enum PartitionMbrType : ushort
{
    /// <summary>FAT12</summary>
    FAT12 = 1,

    /// <summary>FAT16</summary>
    FAT16 = 4,

    /// <summary>확장 파티션</summary>
    Extended = 5,

    /// <summary>Huge(FAT16 대용량)</summary>
    Huge = 6,

    /// <summary>IFS(NTFS, exFAT, ReFS)</summary>
    IFS = 7,

    /// <summary>FAT32</summary>
    FAT32 = 12,
}
