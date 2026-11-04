using System.Text.Json.Serialization;

namespace Biz.Bizadm.MCP.ToolKit.Models.Storage;

/// <summary>디스크 파티션 스타일(MSFT_Disk.PartitionStyle)</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DiskPartitionStyle>))]
public enum DiskPartitionStyle : ushort
{
    /// <summary>초기화되지 않음</summary>
    Raw = 0,

    /// <summary>MBR(Master Boot Record)</summary>
    MBR = 1,

    /// <summary>GPT(GUID Partition Table)</summary>
    GPT = 2,
}
