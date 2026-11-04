using System.Text.Json.Serialization;

namespace Biz.Bizadm.MCP.ToolKit.Models.Storage;

/// <summary>저장소 프로비저닝 유형(MSFT_StoragePool.ProvisioningTypeDefault)</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StorageProvisioningType>))]
public enum StorageProvisioningType : ushort
{
    /// <summary>알 수 없음</summary>
    Unknown = 0,

    /// <summary>씬 프로비저닝, 사용할 때 공간 할당</summary>
    Thin = 1,

    /// <summary>고정 프로비저닝, 생성 시 공간 할당</summary>
    Fixed = 2,
}
