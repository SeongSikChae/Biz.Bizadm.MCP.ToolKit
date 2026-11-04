using System.Text.Json.Serialization;

namespace Biz.Bizadm.MCP.ToolKit.Models.Storage;

/// <summary>가상 디스크 장애 도메인 유형(MSFT_StoragePool.CreateVirtualDisk FaultDomainAwareness·ColumnIsolation)</summary>
[JsonConverter(typeof(JsonStringEnumConverter<VirtualDiskFaultDomainType>))]
public enum VirtualDiskFaultDomainType : ushort
{
    /// <summary>물리 디스크</summary>
    PhysicalDisk = 1,

    /// <summary>저장소 인클로저</summary>
    StorageEnclosure = 2,

    /// <summary>저장소 스케일 단위(노드)</summary>
    StorageScaleUnit = 3,

    /// <summary>저장소 섀시</summary>
    StorageChassis = 4,

    /// <summary>저장소 랙</summary>
    StorageRack = 5,
}
