using System.Text.Json.Serialization;

namespace Biz.Bizadm.MCP.ToolKit.Models.Storage;

/// <summary>볼륨 파일 시스템(MSFT_Volume.Format FileSystem), 이름 문자열로 CIM에 넘긴다.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<VolumeFileSystem>))]
public enum VolumeFileSystem
{
    /// <summary>FAT</summary>
    FAT,

    /// <summary>FAT32</summary>
    FAT32,

    /// <summary>exFAT</summary>
    exFAT,

    /// <summary>NTFS</summary>
    NTFS,

    /// <summary>ReFS</summary>
    ReFS,
}
