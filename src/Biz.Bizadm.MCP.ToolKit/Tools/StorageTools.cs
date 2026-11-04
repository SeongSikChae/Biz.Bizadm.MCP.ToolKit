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
    private const uint JobStarted = 4096;
    private const ushort JobCompleted = 7;
    private const ushort JobTerminated = 8;
    private const ushort JobKilled = 9;
    private const ushort JobException = 10;

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
                CimInstance pool = SelectByName(pools, friendlyName, uniqueId, "Storage pool", "storage pools");
                return new VirtualDiskListResult(EnumeratePoolDisks(session, pool));
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

    /// <summary>로컬 Windows 저장소 풀 1개에 가상 디스크를 만든다(New-VirtualDisk).</summary>
    /// <param name="friendlyName">새 가상 디스크 이름</param>
    /// <param name="storagePoolFriendlyName">저장소 풀 이름(대소문자 무시)</param>
    /// <param name="storagePoolUniqueId">저장소 풀 고유 ID(대소문자 무시)</param>
    /// <param name="size">크기(바이트)</param>
    /// <param name="useMaximumSize">풀의 최대 크기 사용</param>
    /// <param name="resiliencySettingName">복원력 설정 이름</param>
    /// <param name="provisioningType">프로비저닝 유형</param>
    /// <param name="usage">용도</param>
    /// <param name="otherUsageDescription">기타 용도 설명</param>
    /// <param name="allocationUnitSize">할당 단위 크기(바이트)</param>
    /// <param name="mediaType">미디어 유형</param>
    /// <param name="isEnclosureAware">인클로저 인식 여부</param>
    /// <param name="faultDomainAwareness">장애 도메인 인식 단위</param>
    /// <param name="columnIsolation">컬럼 격리 단위</param>
    /// <param name="physicalDiskRedundancy">허용 물리 디스크 장애 수</param>
    /// <param name="numberOfDataCopies">데이터 복사본 수</param>
    /// <param name="numberOfColumns">컬럼 수</param>
    /// <param name="autoNumberOfColumns">컬럼 수 자동 결정</param>
    /// <param name="numberOfGroups">그룹 수</param>
    /// <param name="interleave">인터리브(바이트)</param>
    /// <param name="writeCacheSize">쓰기 캐시 크기(바이트)</param>
    /// <param name="autoWriteCacheSize">쓰기 캐시 크기 자동 결정</param>
    /// <param name="readCacheSize">읽기 캐시 크기(바이트)</param>
    /// <param name="timeoutSeconds">저장소 job 대기 제한시간(초)</param>
    /// <param name="cancellationToken">요청 취소 토큰</param>
    /// <returns>만든 가상 디스크 정보</returns>
    /// <exception cref="McpException">Windows가 아니거나, 입력이 잘못되었거나, 풀이 없거나 이름이 여러 풀과 일치하거나, CIM 호출에 실패했다.</exception>
    [McpServerTool(Name = "create_virtual_disk", UseStructuredContent = true,
        ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
    [Description("로컬 Windows 저장소 풀 1개에 저장소 공간(가상 디스크)을 만든다(New-VirtualDisk). 풀은 storagePoolFriendlyName 또는 storagePoolUniqueId 중 하나로 지정하고, size 또는 useMaximumSize 중 하나를 지정한다. 지정하지 않은 선택 인자는 풀 기본값을 따른다. 풀이 없거나 이름이 여러 풀과 일치하면 오류를 반환한다. Windows가 아니면 오류를 반환한다.")]
    public static VirtualDiskInfo CreateVirtualDisk(
        [Description("새 가상 디스크 이름(FriendlyName)")] string friendlyName,
        [Description("저장소 풀 이름(StoragePoolFriendlyName), storagePoolUniqueId와 함께 쓰지 않는다")] string? storagePoolFriendlyName = null,
        [Description("저장소 풀 고유 ID(StoragePoolUniqueId), storagePoolFriendlyName과 함께 쓰지 않는다")] string? storagePoolUniqueId = null,
        [Description("크기(바이트, Size), useMaximumSize와 함께 쓰지 않는다")] ulong? size = null,
        [Description("풀의 최대 크기 사용(UseMaximumSize), size와 함께 쓰지 않는다")] bool useMaximumSize = false,
        [Description("복원력 설정 이름(ResiliencySettingName), 예: Simple, Mirror, Parity")] string? resiliencySettingName = null,
        [Description("프로비저닝 유형(ProvisioningType)")] StorageProvisioningType? provisioningType = null,
        [Description("용도(Usage)")] VirtualDiskUsage? usage = null,
        [Description("기타 용도 설명(OtherUsageDescription), usage와 함께 지정한다")] string? otherUsageDescription = null,
        [Description("할당 단위 크기(바이트, AllocationUnitSize)")] ulong? allocationUnitSize = null,
        [Description("미디어 유형(MediaType)")] VirtualDiskMediaType? mediaType = null,
        [Description("인클로저 인식 여부(IsEnclosureAware)")] bool? isEnclosureAware = null,
        [Description("장애 도메인 인식 단위(FaultDomainAwareness)")] VirtualDiskFaultDomainType? faultDomainAwareness = null,
        [Description("컬럼 격리 단위(ColumnIsolation)")] VirtualDiskFaultDomainType? columnIsolation = null,
        [Description("허용 물리 디스크 장애 수(PhysicalDiskRedundancy)")] ushort? physicalDiskRedundancy = null,
        [Description("데이터 복사본 수(NumberOfDataCopies)")] ushort? numberOfDataCopies = null,
        [Description("컬럼 수(NumberOfColumns), autoNumberOfColumns와 함께 쓰지 않는다")] ushort? numberOfColumns = null,
        [Description("컬럼 수 자동 결정(AutoNumberOfColumns), numberOfColumns와 함께 쓰지 않는다")] bool autoNumberOfColumns = false,
        [Description("그룹 수(NumberOfGroups)")] ushort? numberOfGroups = null,
        [Description("인터리브(바이트, Interleave)")] ulong? interleave = null,
        [Description("쓰기 캐시 크기(바이트, WriteCacheSize), autoWriteCacheSize와 함께 쓰지 않는다")] ulong? writeCacheSize = null,
        [Description("쓰기 캐시 크기 자동 결정(AutoWriteCacheSize), writeCacheSize와 함께 쓰지 않는다")] bool autoWriteCacheSize = false,
        [Description("읽기 캐시 크기(바이트, ReadCacheSize)")] ulong? readCacheSize = null,
        [Description("저장소 job으로 진행될 때 완료를 기다리는 제한시간(초), 초과해도 job은 계속 진행된다")] int timeoutSeconds = 300,
        CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        if (string.IsNullOrWhiteSpace(storagePoolFriendlyName) == string.IsNullOrWhiteSpace(storagePoolUniqueId))
            throw new McpException("Specify exactly one of storagePoolFriendlyName or storagePoolUniqueId");
        if (string.IsNullOrWhiteSpace(friendlyName))
            throw new McpException("friendlyName is required");
        if (size.HasValue == useMaximumSize)
            throw new McpException("Specify exactly one of size or useMaximumSize");
        if (numberOfColumns.HasValue && autoNumberOfColumns)
            throw new McpException("Specify only one of numberOfColumns or autoNumberOfColumns");
        if (writeCacheSize.HasValue && autoWriteCacheSize)
            throw new McpException("Specify only one of writeCacheSize or autoWriteCacheSize");
        if (otherUsageDescription is not null && usage is null)
            throw new McpException("otherUsageDescription requires usage");
        EnsureTimeout(timeoutSeconds);

        try
        {
            using CimMethodParametersCollection args = new();
            args.Add(InParam("FriendlyName", friendlyName, CimType.String));
            if (size is { } s)
                args.Add(InParam("Size", s, CimType.UInt64));
            if (useMaximumSize)
                args.Add(InParam("UseMaximumSize", true, CimType.Boolean));
            if (resiliencySettingName is not null)
                args.Add(InParam("ResiliencySettingName", resiliencySettingName, CimType.String));
            if (provisioningType is { } pt)
                args.Add(InParam("ProvisioningType", (ushort)pt, CimType.UInt16));
            if (usage is { } u)
                args.Add(InParam("Usage", (ushort)u, CimType.UInt16));
            if (otherUsageDescription is not null)
                args.Add(InParam("OtherUsageDescription", otherUsageDescription, CimType.String));
            if (allocationUnitSize is { } aus)
                args.Add(InParam("AllocationUnitSize", aus, CimType.UInt64));
            if (mediaType is { } mt)
                args.Add(InParam("MediaType", (ushort)mt, CimType.UInt16));
            if (isEnclosureAware is { } iea)
                args.Add(InParam("IsEnclosureAware", iea, CimType.Boolean));
            if (faultDomainAwareness is { } fda)
                args.Add(InParam("FaultDomainAwareness", (ushort)fda, CimType.UInt16));
            if (columnIsolation is { } ci)
                args.Add(InParam("ColumnIsolation", (ushort)ci, CimType.UInt16));
            if (physicalDiskRedundancy is { } pdr)
                args.Add(InParam("PhysicalDiskRedundancy", pdr, CimType.UInt16));
            if (numberOfDataCopies is { } ndc)
                args.Add(InParam("NumberOfDataCopies", ndc, CimType.UInt16));
            if (numberOfColumns is { } nc)
                args.Add(InParam("NumberOfColumns", nc, CimType.UInt16));
            if (autoNumberOfColumns)
                args.Add(InParam("AutoNumberOfColumns", true, CimType.Boolean));
            if (numberOfGroups is { } ng)
                args.Add(InParam("NumberOfGroups", ng, CimType.UInt16));
            if (interleave is { } il)
                args.Add(InParam("Interleave", il, CimType.UInt64));
            if (writeCacheSize is { } wcs)
                args.Add(InParam("WriteCacheSize", wcs, CimType.UInt64));
            if (autoWriteCacheSize)
                args.Add(InParam("AutoWriteCacheSize", true, CimType.Boolean));
            if (readCacheSize is { } rcs)
                args.Add(InParam("ReadCacheSize", rcs, CimType.UInt64));

            using CimSession session = CimSession.Create(null);
            List<CimInstance> pools = session.QueryInstances(StorageNamespace, "WQL", "SELECT * FROM MSFT_StoragePool").ToList();
            try
            {
                CimInstance pool = SelectByName(pools, storagePoolFriendlyName, storagePoolUniqueId, "Storage pool", "storage pools");
                CimMethodResult result = Invoke(session, pool, "CreateVirtualDisk", args, "create", timeoutSeconds,
                    cancellationToken, out bool ranAsJob);
                if (!ranAsJob)
                {
                    using CimInstance created = (CimInstance)result.OutParameters["CreatedVirtualDisk"].Value;
                    return ToVirtualDiskInfo(created.CimInstanceProperties);
                }
                return SelectOne(EnumeratePoolDisks(session, pool), d => d.FriendlyName, d => d.UniqueId,
                    friendlyName, null, "Virtual disk", "virtual disks");
            }
            finally
            {
                foreach (CimInstance pool in pools)
                    pool.Dispose();
            }
        }
        catch (Exception ex) when (ex is CimException or NullReferenceException or InvalidCastException)
        {
            throw new McpException($"Failed to create virtual disk: {ex.Message}", ex);
        }
    }

    /// <summary>로컬 Windows의 가상 디스크 1개의 크기를 바꾼다(Resize-VirtualDisk).</summary>
    /// <param name="size">새 크기(바이트)</param>
    /// <param name="friendlyName">가상 디스크 이름(대소문자 무시)</param>
    /// <param name="uniqueId">가상 디스크 고유 ID(대소문자 무시)</param>
    /// <param name="timeoutSeconds">저장소 job 대기 제한시간(초)</param>
    /// <param name="cancellationToken">요청 취소 토큰</param>
    /// <returns>크기를 바꾼 가상 디스크 정보</returns>
    /// <exception cref="McpException">Windows가 아니거나, 입력이 잘못되었거나, 가상 디스크가 없거나 이름이 여러 가상 디스크와 일치하거나, CIM 호출에 실패했다.</exception>
    [McpServerTool(Name = "resize_virtual_disk", UseStructuredContent = true,
        ReadOnly = false, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("로컬 Windows의 저장소 공간(가상 디스크) 1개의 크기를 바꾼다(Resize-VirtualDisk). 대상은 friendlyName 또는 uniqueId 중 하나로 지정한다. 크기 축소 등 허용되지 않는 크기는 CIM 오류를 반환한다. 없거나 이름이 여러 가상 디스크와 일치하면 오류를 반환한다. Windows가 아니면 오류를 반환한다.")]
    public static VirtualDiskInfo ResizeVirtualDisk(
        [Description("새 크기(바이트), 0보다 커야 한다")] ulong size,
        [Description("가상 디스크 이름(FriendlyName), uniqueId와 함께 쓰지 않는다")] string? friendlyName = null,
        [Description("가상 디스크 고유 ID(UniqueId), friendlyName과 함께 쓰지 않는다")] string? uniqueId = null,
        [Description("저장소 job으로 진행될 때 완료를 기다리는 제한시간(초), 초과해도 job은 계속 진행된다")] int timeoutSeconds = 300,
        CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        EnsureExactlyOne(friendlyName, uniqueId);
        if (size == 0)
            throw new McpException("size must be greater than 0");
        EnsureTimeout(timeoutSeconds);
        string id = WithVirtualDisk(friendlyName, uniqueId, "resize", (session, disk) =>
        {
            using CimMethodParametersCollection args = new();
            args.Add(InParam("Size", size, CimType.UInt64));
            Invoke(session, disk, "Resize", args, "resize", timeoutSeconds, cancellationToken, out _);
            return (string)disk.CimInstanceProperties["UniqueId"].Value;
        });
        return SelectOne(QueryVirtualDisks(), d => d.FriendlyName, d => d.UniqueId,
            null, id, "Virtual disk", "virtual disks");
    }

    /// <summary>로컬 Windows의 가상 디스크 1개의 속성을 바꾼다(Set-VirtualDisk).</summary>
    /// <param name="friendlyName">가상 디스크 이름(대소문자 무시)</param>
    /// <param name="uniqueId">가상 디스크 고유 ID(대소문자 무시)</param>
    /// <param name="newFriendlyName">새 이름</param>
    /// <param name="usage">용도</param>
    /// <param name="otherUsageDescription">기타 용도 설명</param>
    /// <param name="isManualAttach">수동 연결 여부</param>
    /// <param name="storageNodeName">저장소 노드 이름</param>
    /// <param name="access">접근 유형</param>
    /// <returns>속성을 바꾼 가상 디스크 정보</returns>
    /// <exception cref="McpException">Windows가 아니거나, 입력이 잘못되었거나, 가상 디스크가 없거나 이름이 여러 가상 디스크와 일치하거나, CIM 호출에 실패했다.</exception>
    [McpServerTool(Name = "set_virtual_disk", UseStructuredContent = true,
        ReadOnly = false, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("로컬 Windows의 저장소 공간(가상 디스크) 1개의 속성을 바꾼다(Set-VirtualDisk). 대상은 friendlyName 또는 uniqueId 중 하나로 지정하고, 바꿀 속성을 하나 이상 지정한다. 용도 → 속성(isManualAttach·storageNodeName·access) → 이름 순서로 적용하며, 중간에 실패하면 앞서 적용한 변경은 되돌리지 않는다. 없거나 이름이 여러 가상 디스크와 일치하면 오류를 반환한다. Windows가 아니면 오류를 반환한다.")]
    public static VirtualDiskInfo SetVirtualDisk(
        [Description("가상 디스크 이름(FriendlyName), uniqueId와 함께 쓰지 않는다")] string? friendlyName = null,
        [Description("가상 디스크 고유 ID(UniqueId), friendlyName과 함께 쓰지 않는다")] string? uniqueId = null,
        [Description("새 이름(NewFriendlyName)")] string? newFriendlyName = null,
        [Description("용도(Usage)")] VirtualDiskUsage? usage = null,
        [Description("기타 용도 설명(OtherUsageDescription), usage와 함께 지정한다")] string? otherUsageDescription = null,
        [Description("수동 연결 여부(IsManualAttach)")] bool? isManualAttach = null,
        [Description("저장소 노드 이름(StorageNodeName)")] string? storageNodeName = null,
        [Description("접근 유형(Access)")] VirtualDiskAccess? access = null)
    {
        EnsureWindows();
        EnsureExactlyOne(friendlyName, uniqueId);
        if (newFriendlyName is null && usage is null && otherUsageDescription is null
            && isManualAttach is null && storageNodeName is null && access is null)
            throw new McpException("Specify at least one property to change");
        if (newFriendlyName is not null && string.IsNullOrWhiteSpace(newFriendlyName))
            throw new McpException("newFriendlyName must not be blank");
        if (otherUsageDescription is not null && usage is null)
            throw new McpException("otherUsageDescription requires usage");
        string id = WithVirtualDisk(friendlyName, uniqueId, "set", (session, disk) =>
        {
            if (usage is { } u)
            {
                using CimMethodParametersCollection args = new();
                args.Add(InParam("Usage", (ushort)u, CimType.UInt16));
                if (otherUsageDescription is not null)
                    args.Add(InParam("OtherUsageDescription", otherUsageDescription, CimType.String));
                Invoke(session, disk, "SetUsage", args, "set usage of", 0, CancellationToken.None, out _);
            }
            if (isManualAttach is not null || storageNodeName is not null || access is not null)
            {
                using CimMethodParametersCollection args = new();
                if (isManualAttach is { } m)
                    args.Add(InParam("IsManualAttach", m, CimType.Boolean));
                if (storageNodeName is not null)
                    args.Add(InParam("StorageNodeName", storageNodeName, CimType.String));
                if (access is { } a)
                    args.Add(InParam("Access", (ushort)a, CimType.UInt16));
                Invoke(session, disk, "SetAttributes", args, "set attributes of", 0, CancellationToken.None, out _);
            }
            if (newFriendlyName is not null)
            {
                using CimMethodParametersCollection args = new();
                args.Add(InParam("FriendlyName", newFriendlyName, CimType.String));
                Invoke(session, disk, "SetFriendlyName", args, "rename", 0, CancellationToken.None, out _);
            }
            return (string)disk.CimInstanceProperties["UniqueId"].Value;
        });
        return SelectOne(QueryVirtualDisks(), d => d.FriendlyName, d => d.UniqueId,
            null, id, "Virtual disk", "virtual disks");
    }

    /// <summary>로컬 Windows의 가상 디스크 1개를 삭제한다(Remove-VirtualDisk).</summary>
    /// <param name="friendlyName">가상 디스크 이름(대소문자 무시)</param>
    /// <param name="uniqueId">가상 디스크 고유 ID(대소문자 무시)</param>
    /// <param name="confirm">true일 때만 삭제한다</param>
    /// <param name="timeoutSeconds">저장소 job 대기 제한시간(초)</param>
    /// <param name="cancellationToken">요청 취소 토큰</param>
    /// <returns>삭제한 가상 디스크</returns>
    /// <exception cref="McpException">Windows가 아니거나, 입력이 잘못되었거나, 가상 디스크가 없거나 이름이 여러 가상 디스크와 일치하거나, CIM 호출에 실패했다.</exception>
    [McpServerTool(Name = "remove_virtual_disk", UseStructuredContent = true,
        ReadOnly = false, Idempotent = false, Destructive = true, OpenWorld = false)]
    [Description("로컬 Windows의 저장소 공간(가상 디스크) 1개를 삭제한다(Remove-VirtualDisk). 가상 디스크의 데이터가 영구 삭제된다. confirm=true가 필요하다. 대상은 friendlyName 또는 uniqueId 중 하나로 지정한다. 없거나 이름이 여러 가상 디스크와 일치하면 오류를 반환한다. Windows가 아니면 오류를 반환한다.")]
    public static VirtualDiskRemoveResult RemoveVirtualDisk(
        [Description("가상 디스크 이름(FriendlyName), uniqueId와 함께 쓰지 않는다")] string? friendlyName = null,
        [Description("가상 디스크 고유 ID(UniqueId), friendlyName과 함께 쓰지 않는다")] string? uniqueId = null,
        [Description("삭제를 확인한다. true가 아니면 삭제하지 않고 오류를 반환한다")] bool confirm = false,
        [Description("저장소 job으로 진행될 때 완료를 기다리는 제한시간(초), 초과해도 job은 계속 진행된다")] int timeoutSeconds = 300,
        CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        EnsureExactlyOne(friendlyName, uniqueId);
        if (!confirm)
            throw new McpException("Set confirm to true to remove the virtual disk; this permanently deletes its data");
        EnsureTimeout(timeoutSeconds);
        return WithVirtualDisk(friendlyName, uniqueId, "remove", (session, disk) =>
        {
            VirtualDiskRemoveResult removed = new(
                (string)disk.CimInstanceProperties["FriendlyName"].Value,
                (string)disk.CimInstanceProperties["UniqueId"].Value);
            using CimMethodParametersCollection args = new();
            Invoke(session, disk, "DeleteObject", args, "remove", timeoutSeconds, cancellationToken, out _);
            return removed;
        });
    }

    /// <summary>로컬 Windows 가상 디스크의 디스크를 초기화한다(Initialize-Disk). RAW 디스크만 허용한다.</summary>
    /// <param name="friendlyName">가상 디스크 이름(대소문자 무시)</param>
    /// <param name="uniqueId">가상 디스크 고유 ID(대소문자 무시)</param>
    /// <param name="partitionStyle">파티션 스타일(MBR 또는 GPT)</param>
    /// <param name="confirm">true일 때만 초기화한다</param>
    /// <returns>초기화한 디스크 정보</returns>
    /// <exception cref="McpException">Windows가 아니거나, 입력이 잘못되었거나, 가상 디스크·디스크가 없거나, 디스크가 RAW가 아니거나, CIM 호출에 실패했다.</exception>
    [McpServerTool(Name = "initialize_disk", UseStructuredContent = true,
        ReadOnly = false, Idempotent = false, Destructive = true, OpenWorld = false)]
    [Description("로컬 Windows 저장소 공간(가상 디스크)의 디스크를 GPT 또는 MBR로 초기화한다(Initialize-Disk). 초기화되지 않은(RAW) 디스크만 허용하며, 이미 초기화된 디스크는 오류를 반환한다. RAW로 보이는 디스크에도 데이터가 있을 수 있으며 초기화하면 덮어쓴다. confirm=true가 필요하다. 대상은 가상 디스크 friendlyName 또는 uniqueId 중 하나로 지정한다. Windows가 아니면 오류를 반환한다.")]
    public static DiskInfo InitializeDisk(
        [Description("가상 디스크 이름(FriendlyName), uniqueId와 함께 쓰지 않는다")] string? friendlyName = null,
        [Description("가상 디스크 고유 ID(UniqueId), friendlyName과 함께 쓰지 않는다")] string? uniqueId = null,
        [Description("파티션 스타일(PartitionStyle), MBR 또는 GPT, 기본 GPT")] DiskPartitionStyle partitionStyle = DiskPartitionStyle.GPT,
        [Description("초기화를 확인한다. true가 아니면 초기화하지 않고 오류를 반환한다")] bool confirm = false)
    {
        EnsureWindows();
        EnsureExactlyOne(friendlyName, uniqueId);
        if (!confirm)
            throw new McpException("Set confirm to true to initialize the disk; a RAW disk may still contain data that will be overwritten");
        if (partitionStyle == DiskPartitionStyle.Raw)
            throw new McpException("partitionStyle must be MBR or GPT");
        return WithVirtualDiskDisk(friendlyName, uniqueId, "initialize the disk of", (session, disk) =>
        {
            DiskPartitionStyle style = (DiskPartitionStyle)(ushort)disk.CimInstanceProperties["PartitionStyle"].Value;
            if (style != DiskPartitionStyle.Raw)
                throw new McpException($"Disk {disk.CimInstanceProperties["Number"].Value} is already initialized ({style}); " +
                    "only RAW disks can be initialized");
            using CimMethodParametersCollection args = new();
            args.Add(InParam("PartitionStyle", (ushort)partitionStyle, CimType.UInt16));
            Invoke(session, disk, "Initialize", args, "initialize the disk of", 0, CancellationToken.None, out _);
            using CimInstance refreshed = session.GetInstance(StorageNamespace, disk);
            return ToDiskInfo(refreshed.CimInstanceProperties);
        });
    }

    /// <summary>로컬 Windows 가상 디스크의 디스크 빈 공간에 파티션을 만든다(New-Partition).</summary>
    /// <param name="friendlyName">가상 디스크 이름(대소문자 무시)</param>
    /// <param name="uniqueId">가상 디스크 고유 ID(대소문자 무시)</param>
    /// <param name="size">크기(바이트)</param>
    /// <param name="useMaximumSize">가장 큰 빈 공간 전체 사용</param>
    /// <param name="offset">시작 위치(바이트)</param>
    /// <param name="alignment">정렬 단위(바이트)</param>
    /// <param name="driveLetter">드라이브 문자</param>
    /// <param name="assignDriveLetter">드라이브 문자 자동 할당 여부</param>
    /// <param name="gptType">GPT 파티션 유형</param>
    /// <param name="mbrType">MBR 파티션 유형</param>
    /// <param name="isHidden">숨김 여부</param>
    /// <param name="isActive">활성 파티션 여부(MBR)</param>
    /// <returns>만든 파티션 정보</returns>
    /// <exception cref="McpException">Windows가 아니거나, 입력이 잘못되었거나, 가상 디스크·디스크가 없거나, 디스크가 초기화되지 않았거나, CIM 호출에 실패했다.</exception>
    [McpServerTool(Name = "create_partition", UseStructuredContent = true,
        ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
    [Description("로컬 Windows 저장소 공간(가상 디스크)의 초기화된 디스크 빈 공간에 파티션을 만든다(New-Partition). 기존 파티션은 바꾸지 않는다. 초기화되지 않은(RAW) 디스크는 오류를 반환한다. size 또는 useMaximumSize 중 하나를 지정한다. driveLetter를 지정하지 않으면 드라이브 문자를 자동 할당하며, assignDriveLetter=false면 할당하지 않는다. 대상은 가상 디스크 friendlyName 또는 uniqueId 중 하나로 지정한다. Windows가 아니면 오류를 반환한다.")]
    public static PartitionInfo CreatePartition(
        [Description("가상 디스크 이름(FriendlyName), uniqueId와 함께 쓰지 않는다")] string? friendlyName = null,
        [Description("가상 디스크 고유 ID(UniqueId), friendlyName과 함께 쓰지 않는다")] string? uniqueId = null,
        [Description("크기(바이트, Size), useMaximumSize와 함께 쓰지 않는다")] ulong? size = null,
        [Description("가장 큰 빈 공간 전체 사용(UseMaximumSize), size와 함께 쓰지 않는다")] bool useMaximumSize = false,
        [Description("시작 위치(바이트, Offset)")] ulong? offset = null,
        [Description("정렬 단위(바이트, Alignment)")] uint? alignment = null,
        [Description("드라이브 문자(DriveLetter), A-Z 한 글자")] string? driveLetter = null,
        [Description("드라이브 문자 자동 할당(AssignDriveLetter), 생략하면 true, driveLetter와 false를 함께 쓰지 않는다")] bool? assignDriveLetter = null,
        [Description("GPT 파티션 유형(GptType), mbrType과 함께 쓰지 않는다")] PartitionGptType? gptType = null,
        [Description("MBR 파티션 유형(MbrType), gptType과 함께 쓰지 않는다")] PartitionMbrType? mbrType = null,
        [Description("숨김 여부(IsHidden)")] bool? isHidden = null,
        [Description("활성 파티션 여부(IsActive, MBR 디스크)")] bool? isActive = null)
    {
        EnsureWindows();
        EnsureExactlyOne(friendlyName, uniqueId);
        if (size.HasValue == useMaximumSize)
            throw new McpException("Specify exactly one of size or useMaximumSize");
        if (driveLetter is not null && (driveLetter.Length != 1 || !char.IsAsciiLetter(driveLetter[0])))
            throw new McpException("driveLetter must be a single letter A-Z");
        if (driveLetter is not null && assignDriveLetter == false)
            throw new McpException("driveLetter cannot be used with assignDriveLetter=false");
        if (gptType is not null && mbrType is not null)
            throw new McpException("Specify only one of gptType or mbrType");
        return WithVirtualDiskDisk(friendlyName, uniqueId, "create a partition on", (session, disk) =>
        {
            if ((ushort)disk.CimInstanceProperties["PartitionStyle"].Value == (ushort)DiskPartitionStyle.Raw)
                throw new McpException($"Disk {disk.CimInstanceProperties["Number"].Value} is not initialized; run initialize_disk first");
            using CimMethodParametersCollection args = new();
            if (size is { } s)
                args.Add(InParam("Size", s, CimType.UInt64));
            if (useMaximumSize)
                args.Add(InParam("UseMaximumSize", true, CimType.Boolean));
            if (offset is { } o)
                args.Add(InParam("Offset", o, CimType.UInt64));
            if (alignment is { } a)
                args.Add(InParam("Alignment", a, CimType.UInt32));
            if (driveLetter is not null)
                args.Add(InParam("DriveLetter", char.ToUpperInvariant(driveLetter[0]), CimType.Char16));
            else
                args.Add(InParam("AssignDriveLetter", assignDriveLetter ?? true, CimType.Boolean));
            if (gptType is { } g)
                args.Add(InParam("GptType", GptTypeGuid(g), CimType.String));
            if (mbrType is { } m)
                args.Add(InParam("MbrType", (ushort)m, CimType.UInt16));
            if (isHidden is { } h)
                args.Add(InParam("IsHidden", h, CimType.Boolean));
            if (isActive is { } act)
                args.Add(InParam("IsActive", act, CimType.Boolean));
            CimMethodResult result = Invoke(session, disk, "CreatePartition", args, "create a partition on",
                0, CancellationToken.None, out _);
            using CimInstance created = (CimInstance)result.OutParameters["CreatedPartition"].Value;
            using CimInstance refreshed = session.GetInstance(StorageNamespace, created);
            return ToPartitionInfo(refreshed.CimInstanceProperties);
        });
    }

    /// <summary>로컬 Windows 가상 디스크의 파티션 1개에 있는 볼륨을 포맷한다(Format-Volume). 포맷되지 않은 볼륨만 허용한다.</summary>
    /// <param name="partitionNumber">파티션 번호</param>
    /// <param name="friendlyName">가상 디스크 이름(대소문자 무시)</param>
    /// <param name="uniqueId">가상 디스크 고유 ID(대소문자 무시)</param>
    /// <param name="fileSystem">파일 시스템</param>
    /// <param name="newFileSystemLabel">볼륨 레이블</param>
    /// <param name="allocationUnitSize">할당 단위 크기(바이트)</param>
    /// <param name="full">전체 포맷 여부</param>
    /// <param name="compress">압축 사용 여부</param>
    /// <param name="shortFileNameSupport">8.3 짧은 파일 이름 지원 여부</param>
    /// <param name="setIntegrityStreams">무결성 스트림 사용 여부(ReFS)</param>
    /// <param name="useLargeFRS">큰 파일 레코드 세그먼트 사용 여부(NTFS)</param>
    /// <param name="disableHeatGathering">열 수집 비활성화 여부(ReFS)</param>
    /// <param name="isDAX">DAX 모드 여부</param>
    /// <param name="noTrim">TRIM 생략 여부</param>
    /// <param name="devDrive">개발 드라이브 여부(ReFS)</param>
    /// <param name="confirm">true일 때만 포맷한다</param>
    /// <param name="timeoutSeconds">저장소 job 대기 제한시간(초)</param>
    /// <param name="cancellationToken">요청 취소 토큰</param>
    /// <returns>포맷한 볼륨 정보</returns>
    /// <exception cref="McpException">Windows가 아니거나, 입력이 잘못되었거나, 가상 디스크·디스크·파티션·볼륨이 없거나, 볼륨이 이미 포맷되었거나, CIM 호출에 실패했다.</exception>
    [McpServerTool(Name = "format_volume", UseStructuredContent = true,
        ReadOnly = false, Idempotent = false, Destructive = true, OpenWorld = false)]
    [Description("로컬 Windows 저장소 공간(가상 디스크)의 파티션 1개에 있는 볼륨을 포맷한다(Format-Volume). 파일 시스템이 없는(RAW) 볼륨만 허용하며, 이미 포맷된 볼륨은 오류를 반환한다. 잠긴 BitLocker 볼륨, 인식되지 않는 파일 시스템처럼 RAW로 보이는 볼륨에도 데이터가 있을 수 있으며 포맷하면 사라진다. confirm=true가 필요하다. 대상은 가상 디스크 friendlyName 또는 uniqueId 중 하나와 partitionNumber로 지정한다. Windows가 아니면 오류를 반환한다.")]
    public static VolumeInfo FormatVolume(
        [Description("파티션 번호(PartitionNumber), create_partition 결과의 partitionNumber")] uint partitionNumber,
        [Description("가상 디스크 이름(FriendlyName), uniqueId와 함께 쓰지 않는다")] string? friendlyName = null,
        [Description("가상 디스크 고유 ID(UniqueId), friendlyName과 함께 쓰지 않는다")] string? uniqueId = null,
        [Description("파일 시스템(FileSystem), 생략하면 시스템 기본값")] VolumeFileSystem? fileSystem = null,
        [Description("볼륨 레이블(NewFileSystemLabel)")] string? newFileSystemLabel = null,
        [Description("할당 단위 크기(바이트, AllocationUnitSize)")] uint? allocationUnitSize = null,
        [Description("전체 포맷(Full), 생략하면 빠른 포맷")] bool? full = null,
        [Description("압축 사용(Compress)")] bool? compress = null,
        [Description("8.3 짧은 파일 이름 지원(ShortFileNameSupport)")] bool? shortFileNameSupport = null,
        [Description("무결성 스트림 사용(SetIntegrityStreams, ReFS)")] bool? setIntegrityStreams = null,
        [Description("큰 파일 레코드 세그먼트 사용(UseLargeFRS, NTFS)")] bool? useLargeFRS = null,
        [Description("열 수집 비활성화(DisableHeatGathering, ReFS)")] bool? disableHeatGathering = null,
        [Description("DAX 모드(IsDAX)")] bool? isDAX = null,
        [Description("TRIM 생략(NoTrim)")] bool? noTrim = null,
        [Description("개발 드라이브(DevDrive, ReFS)")] bool? devDrive = null,
        [Description("포맷을 확인한다. true가 아니면 포맷하지 않고 오류를 반환한다")] bool confirm = false,
        [Description("저장소 job으로 진행될 때 완료를 기다리는 제한시간(초), 초과해도 job은 계속 진행된다")] int timeoutSeconds = 300,
        CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        EnsureExactlyOne(friendlyName, uniqueId);
        if (!confirm)
            throw new McpException("Set confirm to true to format the volume; a RAW volume may still contain data " +
                "(for example a locked BitLocker volume) that will be lost");
        if (partitionNumber == 0)
            throw new McpException("partitionNumber must be greater than 0");
        EnsureTimeout(timeoutSeconds);
        return WithVirtualDiskDisk(friendlyName, uniqueId, "format a volume on", (session, disk) =>
        {
            using CimInstance partition = FindPartition(session, disk, partitionNumber);
            using CimInstance volume = FindVolume(session, partition);
            if (volume.CimInstanceProperties["FileSystem"].Value is string { Length: > 0 } fs
                && !string.Equals(fs, "RAW", StringComparison.OrdinalIgnoreCase))
                throw new McpException($"Volume on partition {partitionNumber} is already formatted ({fs}); " +
                    "only unformatted volumes can be formatted");
            using CimMethodParametersCollection args = new();
            if (fileSystem is { } f)
                args.Add(InParam("FileSystem", f.ToString(), CimType.String));
            if (newFileSystemLabel is not null)
                args.Add(InParam("FileSystemLabel", newFileSystemLabel, CimType.String));
            if (allocationUnitSize is { } aus)
                args.Add(InParam("AllocationUnitSize", aus, CimType.UInt32));
            AddFlag(args, "Full", full);
            AddFlag(args, "Compress", compress);
            AddFlag(args, "ShortFileNameSupport", shortFileNameSupport);
            AddFlag(args, "SetIntegrityStreams", setIntegrityStreams);
            AddFlag(args, "UseLargeFRS", useLargeFRS);
            AddFlag(args, "DisableHeatGathering", disableHeatGathering);
            AddFlag(args, "IsDAX", isDAX);
            AddFlag(args, "NoTrim", noTrim);
            AddFlag(args, "DevDrive", devDrive);
            Invoke(session, volume, "Format", args, "format a volume on", timeoutSeconds, cancellationToken, out _);
            using CimInstance refreshed = session.GetInstance(StorageNamespace, volume);
            return ToVolumeInfo(refreshed.CimInstanceProperties);
        });
    }

    private static void AddFlag(CimMethodParametersCollection args, string name, bool? value)
    {
        if (value is { } v)
            args.Add(InParam(name, v, CimType.Boolean));
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

    private static CimInstance SelectByName(List<CimInstance> items, string? friendlyName, string? uniqueId,
        string kind, string plural) =>
        SelectOne(items,
            i => (string)i.CimInstanceProperties["FriendlyName"].Value,
            i => (string)i.CimInstanceProperties["UniqueId"].Value,
            friendlyName, uniqueId, kind, plural);

    private static List<VirtualDiskInfo> EnumeratePoolDisks(CimSession session, CimInstance pool)
    {
        List<VirtualDiskInfo> disks = [];
        foreach (CimInstance disk in session.EnumerateAssociatedInstances(StorageNamespace, pool,
            "MSFT_StoragePoolToVirtualDisk", "MSFT_VirtualDisk", "StoragePool", "VirtualDisk"))
        {
            using (disk)
                disks.Add(ToVirtualDiskInfo(disk.CimInstanceProperties));
        }
        return disks;
    }

    private static T WithVirtualDisk<T>(string? friendlyName, string? uniqueId, string action,
        Func<CimSession, CimInstance, T> operation)
    {
        try
        {
            using CimSession session = CimSession.Create(null);
            List<CimInstance> disks = session.QueryInstances(StorageNamespace, "WQL", "SELECT * FROM MSFT_VirtualDisk").ToList();
            try
            {
                return operation(session, SelectByName(disks, friendlyName, uniqueId, "Virtual disk", "virtual disks"));
            }
            finally
            {
                foreach (CimInstance disk in disks)
                    disk.Dispose();
            }
        }
        catch (Exception ex) when (ex is CimException or NullReferenceException or InvalidCastException)
        {
            throw new McpException($"Failed to {action} virtual disk: {ex.Message}", ex);
        }
    }

    private static void EnsureTimeout(int timeoutSeconds)
    {
        if (timeoutSeconds <= 0)
            throw new McpException("timeoutSeconds must be greater than 0");
    }

    private static CimMethodParameter InParam(string name, object value, CimType type) =>
        CimMethodParameter.Create(name, value, type, CimFlags.In);

    private static CimMethodResult Invoke(CimSession session, CimInstance target, string method,
        CimMethodParametersCollection parameters, string action, int timeoutSeconds,
        CancellationToken cancellationToken, out bool ranAsJob)
    {
        CimMethodResult result = session.InvokeMethod(StorageNamespace, target, method, parameters);
        uint code = (uint)result.ReturnValue.Value;
        ranAsJob = code == JobStarted;
        if (ranAsJob)
            WaitForJob(session, (CimInstance)result.OutParameters["CreatedStorageJob"].Value, timeoutSeconds, cancellationToken);
        else if (code != 0)
            throw new McpException($"Failed to {action} virtual disk: {ExtendedMessage(result)} (code {code})");
        return result;
    }

    private static void WaitForJob(CimSession session, CimInstance jobRef, int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        using (jobRef)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            while (true)
            {
                using CimInstance job = session.GetInstance(StorageNamespace, jobRef);
                ushort state = (ushort)job.CimInstanceProperties["JobState"].Value;
                string name = (string)job.CimInstanceProperties["Name"].Value;
                if (state == JobCompleted)
                    return;
                if (state is JobTerminated or JobKilled or JobException)
                    throw new McpException($"Storage job '{name}' failed: {job.CimInstanceProperties["ErrorDescription"].Value}");
                if (DateTime.UtcNow > deadline)
                    throw new McpException($"Timed out after {timeoutSeconds}s waiting for storage job '{name}' " +
                        $"({job.CimInstanceProperties["PercentComplete"].Value}%); the job continues");
                cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(1));
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    private static string ExtendedMessage(CimMethodResult result) =>
        (result.OutParameters["ExtendedStatus"]?.Value as CimInstance)
            ?.CimInstanceProperties["Message"]?.Value as string ?? "error";

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

    private static T WithVirtualDiskDisk<T>(string? friendlyName, string? uniqueId, string action,
        Func<CimSession, CimInstance, T> operation) =>
        WithVirtualDisk(friendlyName, uniqueId, action, (session, virtualDisk) =>
        {
            List<CimInstance> disks = session.EnumerateAssociatedInstances(StorageNamespace, virtualDisk,
                "MSFT_VirtualDiskToDisk", "MSFT_Disk", "VirtualDisk", "Disk").ToList();
            try
            {
                if (disks.Count == 0)
                    throw new McpException(
                        $"Virtual disk '{virtualDisk.CimInstanceProperties["FriendlyName"].Value}' has no disk; it may be detached");
                return operation(session, disks[0]);
            }
            finally
            {
                foreach (CimInstance disk in disks)
                    disk.Dispose();
            }
        });

    private static CimInstance FindPartition(CimSession session, CimInstance disk, uint partitionNumber)
    {
        CimInstance? found = null;
        foreach (CimInstance partition in session.EnumerateAssociatedInstances(StorageNamespace, disk,
            "MSFT_DiskToPartition", "MSFT_Partition", "Disk", "Partition"))
        {
            if (found is null && (uint)partition.CimInstanceProperties["PartitionNumber"].Value == partitionNumber)
                found = partition;
            else
                partition.Dispose();
        }
        return found ?? throw new McpException(
            $"Partition {partitionNumber} not found on disk {disk.CimInstanceProperties["Number"].Value}");
    }

    private static CimInstance FindVolume(CimSession session, CimInstance partition)
    {
        CimInstance? found = null;
        foreach (CimInstance volume in session.EnumerateAssociatedInstances(StorageNamespace, partition,
            "MSFT_PartitionToVolume", "MSFT_Volume", "Partition", "Volume"))
        {
            if (found is null)
                found = volume;
            else
                volume.Dispose();
        }
        return found ?? throw new McpException(
            $"Partition {partition.CimInstanceProperties["PartitionNumber"].Value} on disk " +
            $"{partition.CimInstanceProperties["DiskNumber"].Value} has no volume");
    }

    private static string GptTypeGuid(PartitionGptType type) => type switch
    {
        PartitionGptType.SystemPartition => "{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}",
        PartitionGptType.MicrosoftReserved => "{e3c9e316-0b5c-4db8-817d-f92df00215ae}",
        PartitionGptType.BasicData => "{ebd0a0a2-b9e5-4433-87c0-68b6b72699c7}",
        PartitionGptType.MicrosoftRecovery => "{de94bba4-06d1-4d40-a16a-bfd50179d6ac}",
        _ => throw new McpException($"Unsupported gptType: {type}"),
    };

    private static string? ToDriveLetter(object? value) =>
        value is char c && c != '\0' ? c.ToString() : null;

    private static DiskInfo ToDiskInfo(CimKeyedCollection<CimProperty> p) => new(
        (uint)p["Number"].Value,
        (string)p["FriendlyName"].Value,
        (string)p["UniqueId"].Value,
        (DiskPartitionStyle)(ushort)p["PartitionStyle"].Value,
        (ulong)p["Size"].Value,
        (ulong)p["AllocatedSize"].Value,
        (ulong)p["LargestFreeExtent"].Value,
        (uint)p["NumberOfPartitions"].Value,
        (uint)p["LogicalSectorSize"].Value,
        (uint)p["PhysicalSectorSize"].Value,
        (bool)p["IsOffline"].Value,
        (bool)p["IsReadOnly"].Value,
        (StorageHealthStatus)(ushort)p["HealthStatus"].Value);

    private static PartitionInfo ToPartitionInfo(CimKeyedCollection<CimProperty> p) => new(
        (uint)p["DiskNumber"].Value,
        (uint)p["PartitionNumber"].Value,
        ToDriveLetter(p["DriveLetter"].Value),
        (string[]?)p["AccessPaths"].Value ?? [],
        (ulong)p["Offset"].Value,
        (ulong)p["Size"].Value,
        (string?)p["GptType"].Value,
        (ushort?)p["MbrType"].Value,
        (bool)p["IsActive"].Value,
        (bool)p["IsHidden"].Value,
        (bool)p["IsReadOnly"].Value,
        (bool)p["IsOffline"].Value);

    private static VolumeInfo ToVolumeInfo(CimKeyedCollection<CimProperty> p) => new(
        (string)p["UniqueId"].Value,
        ToDriveLetter(p["DriveLetter"].Value),
        p["FileSystem"].Value is string { Length: > 0 } fs ? fs : null,
        p["FileSystemLabel"].Value is string { Length: > 0 } label ? label : null,
        (uint)p["AllocationUnitSize"].Value,
        (ulong)p["Size"].Value,
        (ulong)p["SizeRemaining"].Value,
        (StorageHealthStatus)(ushort)p["HealthStatus"].Value);
}
