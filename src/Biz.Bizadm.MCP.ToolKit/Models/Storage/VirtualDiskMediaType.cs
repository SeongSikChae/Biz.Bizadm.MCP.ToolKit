using System.Text.Json.Serialization;

namespace Biz.Bizadm.MCP.ToolKit.Models.Storage;

/// <summary>가상 디스크 미디어 유형(MSFT_VirtualDisk.MediaType)</summary>
[JsonConverter(typeof(JsonStringEnumConverter<VirtualDiskMediaType>))]
public enum VirtualDiskMediaType : ushort
{
    /// <summary>지정 안 됨</summary>
    Unspecified = 0,

    /// <summary>HDD</summary>
    HDD = 3,

    /// <summary>SSD</summary>
    SSD = 4,

    /// <summary>SCM(Storage Class Memory)</summary>
    SCM = 5,
}
