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
        bool byName = !string.IsNullOrWhiteSpace(friendlyName);
        bool byId = !string.IsNullOrWhiteSpace(uniqueId);
        if (byName == byId)
            throw new McpException("Specify exactly one of friendlyName or uniqueId");

        List<StoragePoolInfo> matches = QueryStoragePools()
            .Where(p => byName
                ? string.Equals(p.FriendlyName, friendlyName, StringComparison.OrdinalIgnoreCase)
                : string.Equals(p.UniqueId, uniqueId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return matches.Count switch
        {
            0 => throw new McpException($"Storage pool not found: {(byName ? friendlyName : uniqueId)}"),
            1 => matches[0],
            _ => throw new McpException($"Multiple storage pools match friendlyName '{friendlyName}'; use uniqueId"),
        };
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new McpException("Storage pools are only supported on Windows");
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
}
