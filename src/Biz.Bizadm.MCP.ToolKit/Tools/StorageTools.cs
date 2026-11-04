using System.ComponentModel;
using Biz.Bizadm.MCP.ToolKit.Models.Storage;
using Microsoft.Management.Infrastructure;
using Microsoft.Management.Infrastructure.Generic;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Biz.Bizadm.MCP.ToolKit.Tools;

/// <summary>Windows 저장소 공간(Storage Spaces) 조회 Tool</summary>
[McpServerToolType]
public sealed class StorageTools
{
    private const string StorageNamespace = "root/Microsoft/Windows/Storage";

    /// <summary>로컬 Windows의 저장소 풀 목록을 조회한다(Get-StoragePool).</summary>
    /// <returns>저장소 풀 목록</returns>
    /// <exception cref="McpException">Windows가 아니거나 CIM 조회에 실패했다.</exception>
    [McpServerTool(Name = "list_storage_pools", UseStructuredContent = true,
        ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("로컬 Windows의 저장소 공간(Storage Spaces) 저장소 풀 목록을 조회한다(Get-StoragePool). Windows가 아니면 오류를 반환한다.")]
    public static StoragePoolListResult ListStoragePools()
    {
        EnsureWindows();
        return new StoragePoolListResult(QueryStoragePools());
    }

    /// <summary>로컬 Windows의 저장소 풀 1개를 friendlyName 또는 uniqueId 중 하나로 조회한다.</summary>
    /// <param name="friendlyName">저장소 풀 이름(대소문자 무시)</param>
    /// <param name="uniqueId">저장소 풀 고유 ID(대소문자 무시)</param>
    /// <returns>저장소 풀 정보</returns>
    /// <exception cref="McpException">Windows가 아니거나, 입력이 정확히 하나가 아니거나, 풀이 없거나 이름이 여러 풀과 일치하거나, CIM 조회에 실패했다.</exception>
    [McpServerTool(Name = "get_storage_pool", UseStructuredContent = true,
        ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("로컬 Windows의 저장소 풀 1개를 friendlyName 또는 uniqueId 중 하나로 조회한다. 없거나 이름이 여러 풀과 일치하면 오류를 반환한다. Windows가 아니면 오류를 반환한다.")]
    public static StoragePoolInfo GetStoragePool(
        [Description("저장소 풀 이름(FriendlyName), uniqueId와 함께 쓰지 않는다")] string? friendlyName = null,
        [Description("저장소 풀 고유 ID(UniqueId), friendlyName과 함께 쓰지 않는다")] string? uniqueId = null)
    {
        EnsureWindows();
        EnsureExactlyOne(friendlyName, uniqueId);
        return SelectOne(QueryStoragePools(), p => p.FriendlyName, p => p.UniqueId,
            friendlyName, uniqueId, "Storage pool", "storage pools");
    }

    /// <summary>로컬 Windows 저장소 풀 1개에 속한 가상 디스크 목록을 조회한다(Get-VirtualDisk -StoragePool).</summary>
    /// <param name="friendlyName">저장소 풀 이름(대소문자 무시)</param>
    /// <param name="uniqueId">저장소 풀 고유 ID(대소문자 무시)</param>
    /// <returns>가상 디스크 목록</returns>
    /// <exception cref="McpException">Windows가 아니거나, 입력이 정확히 하나가 아니거나, 풀이 없거나 이름이 여러 풀과 일치하거나, CIM 조회에 실패했다.</exception>
    [McpServerTool(Name = "list_virtual_disks", UseStructuredContent = true,
        ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("로컬 Windows 저장소 풀 1개에 속한 저장소 공간(가상 디스크) 목록을 조회한다(Get-VirtualDisk -StoragePool). 풀은 friendlyName 또는 uniqueId 중 하나로 지정한다. 풀이 없거나 이름이 여러 풀과 일치하면 오류를 반환한다. Windows가 아니면 오류를 반환한다.")]
    public static VirtualDiskListResult ListVirtualDisks(
        [Description("저장소 풀 이름(FriendlyName), uniqueId와 함께 쓰지 않는다")] string? friendlyName = null,
        [Description("저장소 풀 고유 ID(UniqueId), friendlyName과 함께 쓰지 않는다")] string? uniqueId = null)
    {
        EnsureWindows();
        EnsureExactlyOne(friendlyName, uniqueId);
        try
        {
            using CimSession session = CimSession.Create(null);
            List<CimInstance> pools = session.QueryInstances(StorageNamespace, "WQL", "SELECT * FROM MSFT_StoragePool").ToList();
            try
            {
                CimInstance pool = SelectOne(pools,
                    i => (string)i.CimInstanceProperties["FriendlyName"].Value,
                    i => (string)i.CimInstanceProperties["UniqueId"].Value,
                    friendlyName, uniqueId, "Storage pool", "storage pools");
                List<VirtualDiskInfo> disks = [];
                foreach (CimInstance disk in session.EnumerateAssociatedInstances(StorageNamespace, pool,
                    "MSFT_StoragePoolToVirtualDisk", "MSFT_VirtualDisk", "StoragePool", "VirtualDisk"))
                {
                    using (disk)
                        disks.Add(ToVirtualDiskInfo(disk.CimInstanceProperties));
                }
                return new VirtualDiskListResult(disks);
            }
            finally
            {
                foreach (CimInstance pool in pools)
                    pool.Dispose();
            }
        }
        catch (Exception ex) when (ex is CimException or NullReferenceException or InvalidCastException)
        {
            throw new McpException($"Failed to query virtual disks: {ex.Message}", ex);
        }
    }

    /// <summary>로컬 Windows의 가상 디스크 1개를 friendlyName 또는 uniqueId 중 하나로 조회한다(Get-VirtualDisk).</summary>
    /// <param name="friendlyName">가상 디스크 이름(대소문자 무시)</param>
    /// <param name="uniqueId">가상 디스크 고유 ID(대소문자 무시)</param>
    /// <returns>가상 디스크 정보</returns>
    /// <exception cref="McpException">Windows가 아니거나, 입력이 정확히 하나가 아니거나, 가상 디스크가 없거나 이름이 여러 가상 디스크와 일치하거나, CIM 조회에 실패했다.</exception>
    [McpServerTool(Name = "get_virtual_disk", UseStructuredContent = true,
        ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("로컬 Windows의 저장소 공간(가상 디스크) 1개를 friendlyName 또는 uniqueId 중 하나로 조회한다(Get-VirtualDisk). 없거나 이름이 여러 가상 디스크와 일치하면 오류를 반환한다. Windows가 아니면 오류를 반환한다.")]
    public static VirtualDiskInfo GetVirtualDisk(
        [Description("가상 디스크 이름(FriendlyName), uniqueId와 함께 쓰지 않는다")] string? friendlyName = null,
        [Description("가상 디스크 고유 ID(UniqueId), friendlyName과 함께 쓰지 않는다")] string? uniqueId = null)
    {
        EnsureWindows();
        EnsureExactlyOne(friendlyName, uniqueId);
        return SelectOne(QueryVirtualDisks(), d => d.FriendlyName, d => d.UniqueId,
            friendlyName, uniqueId, "Virtual disk", "virtual disks");
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new McpException("Storage pools are only supported on Windows");
    }

    private static void EnsureExactlyOne(string? friendlyName, string? uniqueId)
    {
        if (string.IsNullOrWhiteSpace(friendlyName) == string.IsNullOrWhiteSpace(uniqueId))
            throw new McpException("Specify exactly one of friendlyName or uniqueId");
    }

    private static T SelectOne<T>(IEnumerable<T> items, Func<T, string> name, Func<T, string> id,
        string? friendlyName, string? uniqueId, string kind, string plural)
    {
        bool byName = !string.IsNullOrWhiteSpace(friendlyName);
        List<T> matches = items
            .Where(x => byName
                ? string.Equals(name(x), friendlyName, StringComparison.OrdinalIgnoreCase)
                : string.Equals(id(x), uniqueId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return matches.Count switch
        {
            0 => throw new McpException($"{kind} not found: {(byName ? friendlyName : uniqueId)}"),
            1 => matches[0],
            _ => throw new McpException($"Multiple {plural} match friendlyName '{friendlyName}'; use uniqueId"),
        };
    }

    private static List<StoragePoolInfo> QueryStoragePools()
    {
        try
        {
            using CimSession session = CimSession.Create(null);
            List<StoragePoolInfo> pools = [];
            foreach (CimInstance instance in session.QueryInstances(StorageNamespace, "WQL", "SELECT * FROM MSFT_StoragePool"))
            {
                using (instance)
                    pools.Add(ToStoragePoolInfo(instance.CimInstanceProperties));
            }
            return pools;
        }
        catch (Exception ex) when (ex is CimException or NullReferenceException or InvalidCastException)
        {
            throw new McpException($"Failed to query storage pools: {ex.Message}", ex);
        }
    }

    private static StoragePoolInfo ToStoragePoolInfo(CimKeyedCollection<CimProperty> p) => new(
        (string)p["FriendlyName"].Value,
        (string)p["UniqueId"].Value,
        (string)p["ObjectId"].Value,
        ((ushort[]?)p["OperationalStatus"].Value ?? []).Select(s => (StorageOperationalStatus)s).ToList(),
        (StorageHealthStatus)(ushort)p["HealthStatus"].Value,
        (bool)p["IsPrimordial"].Value,
        (bool)p["IsReadOnly"].Value,
        (bool)p["IsClustered"].Value,
        (ulong)p["Size"].Value,
        (ulong)p["AllocatedSize"].Value,
        (ulong?)p["LogicalSectorSize"].Value,
        (ulong?)p["PhysicalSectorSize"].Value,
        (string?)p["ResiliencySettingNameDefault"].Value,
        (StorageProvisioningType)(ushort)p["ProvisioningTypeDefault"].Value,
        (StoragePoolUsage)(ushort)p["Usage"].Value,
        (ushort)p["Version"].Value);

    private static List<VirtualDiskInfo> QueryVirtualDisks()
    {
        try
        {
            using CimSession session = CimSession.Create(null);
            List<VirtualDiskInfo> disks = [];
            foreach (CimInstance instance in session.QueryInstances(StorageNamespace, "WQL", "SELECT * FROM MSFT_VirtualDisk"))
            {
                using (instance)
                    disks.Add(ToVirtualDiskInfo(instance.CimInstanceProperties));
            }
            return disks;
        }
        catch (Exception ex) when (ex is CimException or NullReferenceException or InvalidCastException)
        {
            throw new McpException($"Failed to query virtual disks: {ex.Message}", ex);
        }
    }

    private static VirtualDiskInfo ToVirtualDiskInfo(CimKeyedCollection<CimProperty> p) => new(
        (string)p["FriendlyName"].Value,
        (string)p["UniqueId"].Value,
        (string)p["ObjectId"].Value,
        ((ushort[]?)p["OperationalStatus"].Value ?? []).Select(s => (VirtualDiskOperationalStatus)s).ToList(),
        (StorageHealthStatus)(ushort)p["HealthStatus"].Value,
        (string)p["ResiliencySettingName"].Value,
        (StorageProvisioningType)(ushort)p["ProvisioningType"].Value,
        (VirtualDiskUsage)(ushort)p["Usage"].Value,
        (ulong)p["Size"].Value,
        (ulong)p["AllocatedSize"].Value,
        (ulong)p["FootprintOnPool"].Value,
        (ushort)p["NumberOfDataCopies"].Value,
        (ushort)p["PhysicalDiskRedundancy"].Value,
        (ushort)p["NumberOfColumns"].Value,
        (ulong)p["Interleave"].Value,
        (VirtualDiskParityLayout?)(ushort?)p["ParityLayout"].Value,
        (ulong)p["LogicalSectorSize"].Value,
        (ulong)p["PhysicalSectorSize"].Value,
        (VirtualDiskMediaType)(ushort)p["MediaType"].Value,
        (VirtualDiskAccess)(ushort)p["Access"].Value,
        (VirtualDiskDetachedReason)(ushort)p["DetachedReason"].Value,
        (bool)p["IsTiered"].Value,
        (bool)p["IsSnapshot"].Value,
        (bool)p["IsManualAttach"].Value,
        (bool)p["IsDeduplicationEnabled"].Value,
        (bool)p["IsEnclosureAware"].Value,
        (ulong)p["WriteCacheSize"].Value);
}
