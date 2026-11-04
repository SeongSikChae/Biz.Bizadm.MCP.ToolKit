using System.Text.Json.Serialization;

namespace Biz.Bizadm.MCP.ToolKit.Models.Storage;

/// <summary>가상 디스크 접근 유형(MSFT_VirtualDisk.Access)</summary>
[JsonConverter(typeof(JsonStringEnumConverter<VirtualDiskAccess>))]
public enum VirtualDiskAccess : ushort
{
    /// <summary>알 수 없음</summary>
    Unknown = 0,

    /// <summary>읽기 가능</summary>
    Readable = 1,

    /// <summary>쓰기 가능</summary>
    Writeable = 2,

    /// <summary>읽기·쓰기</summary>
    ReadWrite = 3,

    /// <summary>한 번 쓰기</summary>
    WriteOnce = 4,
}
