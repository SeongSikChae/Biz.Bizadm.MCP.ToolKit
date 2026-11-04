using System.Text.Json.Serialization;

namespace Biz.Bizadm.MCP.ToolKit.Models.Storage;

/// <summary>가상 디스크 분리 사유(MSFT_VirtualDisk.DetachedReason)</summary>
[JsonConverter(typeof(JsonStringEnumConverter<VirtualDiskDetachedReason>))]
public enum VirtualDiskDetachedReason : ushort
{
    /// <summary>알 수 없음</summary>
    Unknown = 0,

    /// <summary>없음</summary>
    None = 1,

    /// <summary>정책에 따름</summary>
    ByPolicy = 2,

    /// <summary>디스크 과반 비정상</summary>
    MajorityDisksUnhealthy = 3,

    /// <summary>불완전</summary>
    Incomplete = 4,

    /// <summary>시간 초과</summary>
    Timeout = 5,
}
