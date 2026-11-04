using System.Text.Json.Serialization;

namespace Biz.Bizadm.MCP.ToolKit.Models.Storage;

/// <summary>가상 디스크 패리티 배치(MSFT_VirtualDisk.ParityLayout)</summary>
[JsonConverter(typeof(JsonStringEnumConverter<VirtualDiskParityLayout>))]
public enum VirtualDiskParityLayout : ushort
{
    /// <summary>비회전 패리티</summary>
    NonRotatedParity = 1,

    /// <summary>회전 패리티</summary>
    RotatedParity = 2,
}
