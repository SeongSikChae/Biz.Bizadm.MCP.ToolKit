using System.Text.Json.Serialization;

namespace Biz.Bizadm.MCP.ToolKit.Models.Storage;

/// <summary>저장소 개체 상태(MSFT_StoragePool.HealthStatus)</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StorageHealthStatus>))]
public enum StorageHealthStatus : ushort
{
    /// <summary>정상</summary>
    Healthy = 0,

    /// <summary>경고</summary>
    Warning = 1,

    /// <summary>비정상</summary>
    Unhealthy = 2,

    /// <summary>알 수 없음</summary>
    Unknown = 5,
}
