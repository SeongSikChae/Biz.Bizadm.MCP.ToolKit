using System.Text.Json;
using Biz.Bizadm.MCP.ToolKit.Models.Storage;
using Biz.Bizadm.MCP.ToolKit.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Management.Infrastructure;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Biz.Bizadm.MCP.ToolKit.Tests;

[TestClass]
public sealed class StorageToolsTests
{
    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void ListStoragePools_ReturnsPools()
    {
        StoragePoolListResult r = StorageTools.ListStoragePools();
        Assert.IsNotEmpty(r.StoragePools);
        Assert.IsTrue(r.StoragePools.All(p => !string.IsNullOrEmpty(p.UniqueId)));
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void GetStoragePool_ByUniqueId_MatchesList()
    {
        StoragePoolInfo expected = StorageTools.ListStoragePools().StoragePools[0];
        AssertSamePool(expected, StorageTools.GetStoragePool(uniqueId: expected.UniqueId.ToLowerInvariant()));
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void GetStoragePool_ByFriendlyName_MatchesList()
    {
        IReadOnlyList<StoragePoolInfo> pools = StorageTools.ListStoragePools().StoragePools;
        StoragePoolInfo expected = pools.First(p =>
            pools.Count(q => string.Equals(q.FriendlyName, p.FriendlyName, StringComparison.OrdinalIgnoreCase)) == 1);
        AssertSamePool(expected, StorageTools.GetStoragePool(friendlyName: expected.FriendlyName.ToUpperInvariant()));
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    [DataRow(null, null)]
    [DataRow(" ", "")]
    [DataRow("a", "b")]
    public void GetStoragePool_NotExactlyOneInput_Throws(string? friendlyName, string? uniqueId)
    {
        Assert.ThrowsExactly<McpException>(() => StorageTools.GetStoragePool(friendlyName, uniqueId));
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void GetStoragePool_NotFound_Throws()
    {
        string name = Guid.NewGuid().ToString();
        Assert.ThrowsExactly<McpException>(() => StorageTools.GetStoragePool(friendlyName: name));
        Assert.ThrowsExactly<McpException>(() => StorageTools.GetStoragePool(uniqueId: name));
    }

    [TestMethod]
    [OSCondition(ConditionMode.Exclude, OperatingSystems.Windows)]
    public void ListStoragePools_NonWindows_Throws()
    {
        Assert.ThrowsExactly<McpException>(() => StorageTools.ListStoragePools());
    }

    [TestMethod]
    [OSCondition(ConditionMode.Exclude, OperatingSystems.Windows)]
    public void GetStoragePool_NonWindows_Throws()
    {
        Assert.ThrowsExactly<McpException>(() => StorageTools.GetStoragePool(friendlyName: "Primordial"));
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void ListVirtualDisks_AllPools_CoverAllVirtualDisks()
    {
        List<VirtualDiskInfo> disks = StorageTools.ListStoragePools().StoragePools
            .SelectMany(p => StorageTools.ListVirtualDisks(uniqueId: p.UniqueId).VirtualDisks)
            .ToList();
        Assert.AreEqual(disks.Count, disks.Select(d => d.UniqueId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.AreEqual(CountVirtualDisksByCim(), disks.Count);
        foreach (VirtualDiskInfo expected in disks)
            AssertSameVirtualDisk(expected, StorageTools.GetVirtualDisk(uniqueId: expected.UniqueId.ToLowerInvariant()));
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void ListVirtualDisks_Primordial_ReturnsEmpty()
    {
        StoragePoolInfo primordial = StorageTools.ListStoragePools().StoragePools.First(p => p.IsPrimordial);
        Assert.IsEmpty(StorageTools.ListVirtualDisks(uniqueId: primordial.UniqueId).VirtualDisks);
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void GetVirtualDisk_ByFriendlyName_MatchesList()
    {
        List<VirtualDiskInfo> disks = StorageTools.ListStoragePools().StoragePools
            .SelectMany(p => StorageTools.ListVirtualDisks(uniqueId: p.UniqueId).VirtualDisks)
            .ToList();
        VirtualDiskInfo expected = disks.First(d =>
            disks.Count(q => string.Equals(q.FriendlyName, d.FriendlyName, StringComparison.OrdinalIgnoreCase)) == 1);
        AssertSameVirtualDisk(expected, StorageTools.GetVirtualDisk(friendlyName: expected.FriendlyName.ToUpperInvariant()));
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    [DataRow(null, null)]
    [DataRow(" ", "")]
    [DataRow("a", "b")]
    public void ListVirtualDisks_NotExactlyOneInput_Throws(string? friendlyName, string? uniqueId)
    {
        Assert.ThrowsExactly<McpException>(() => StorageTools.ListVirtualDisks(friendlyName, uniqueId));
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void ListVirtualDisks_PoolNotFound_Throws()
    {
        string name = Guid.NewGuid().ToString();
        Assert.ThrowsExactly<McpException>(() => StorageTools.ListVirtualDisks(friendlyName: name));
        Assert.ThrowsExactly<McpException>(() => StorageTools.ListVirtualDisks(uniqueId: name));
    }

    [TestMethod]
    [OSCondition(ConditionMode.Exclude, OperatingSystems.Windows)]
    public void ListVirtualDisks_NonWindows_Throws()
    {
        Assert.ThrowsExactly<McpException>(() => StorageTools.ListVirtualDisks(friendlyName: "Primordial"));
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    [DataRow(null, null)]
    [DataRow(" ", "")]
    [DataRow("a", "b")]
    public void GetVirtualDisk_NotExactlyOneInput_Throws(string? friendlyName, string? uniqueId)
    {
        Assert.ThrowsExactly<McpException>(() => StorageTools.GetVirtualDisk(friendlyName, uniqueId));
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void GetVirtualDisk_NotFound_Throws()
    {
        string name = Guid.NewGuid().ToString();
        Assert.ThrowsExactly<McpException>(() => StorageTools.GetVirtualDisk(friendlyName: name));
        Assert.ThrowsExactly<McpException>(() => StorageTools.GetVirtualDisk(uniqueId: name));
    }

    [TestMethod]
    [OSCondition(ConditionMode.Exclude, OperatingSystems.Windows)]
    public void GetVirtualDisk_NonWindows_Throws()
    {
        Assert.ThrowsExactly<McpException>(() => StorageTools.GetVirtualDisk(friendlyName: "Source"));
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    [DataRow(null, null)]
    [DataRow(" ", "")]
    [DataRow("a", "b")]
    public void CreateVirtualDisk_NotExactlyOnePool_Throws(string? poolName, string? poolId)
    {
        McpException ex = Assert.ThrowsExactly<McpException>(() => StorageTools.CreateVirtualDisk(
            "Disk", storagePoolFriendlyName: poolName, storagePoolUniqueId: poolId, size: 1UL << 30));
        StringAssert.Contains(ex.Message, "storagePool");
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    [DataRow("")]
    [DataRow(" ")]
    public void CreateVirtualDisk_BlankFriendlyName_Throws(string friendlyName)
    {
        McpException ex = Assert.ThrowsExactly<McpException>(() => StorageTools.CreateVirtualDisk(
            friendlyName, storagePoolFriendlyName: Guid.NewGuid().ToString(), size: 1UL << 30));
        StringAssert.Contains(ex.Message, "friendlyName");
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    [DataRow(false)]
    [DataRow(true)]
    public void CreateVirtualDisk_NotExactlyOneSize_Throws(bool both)
    {
        McpException ex = Assert.ThrowsExactly<McpException>(() => StorageTools.CreateVirtualDisk(
            "Disk", storagePoolFriendlyName: Guid.NewGuid().ToString(),
            size: both ? 1UL << 30 : null, useMaximumSize: both));
        StringAssert.Contains(ex.Message, "size or useMaximumSize");
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void CreateVirtualDisk_ColumnsConflict_Throws()
    {
        McpException ex = Assert.ThrowsExactly<McpException>(() => StorageTools.CreateVirtualDisk(
            "Disk", storagePoolFriendlyName: Guid.NewGuid().ToString(), size: 1UL << 30,
            numberOfColumns: 2, autoNumberOfColumns: true));
        StringAssert.Contains(ex.Message, "numberOfColumns");
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void CreateVirtualDisk_WriteCacheConflict_Throws()
    {
        McpException ex = Assert.ThrowsExactly<McpException>(() => StorageTools.CreateVirtualDisk(
            "Disk", storagePoolFriendlyName: Guid.NewGuid().ToString(), size: 1UL << 30,
            writeCacheSize: 1UL << 20, autoWriteCacheSize: true));
        StringAssert.Contains(ex.Message, "writeCacheSize");
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void CreateVirtualDisk_OtherUsageDescriptionWithoutUsage_Throws()
    {
        McpException ex = Assert.ThrowsExactly<McpException>(() => StorageTools.CreateVirtualDisk(
            "Disk", storagePoolFriendlyName: Guid.NewGuid().ToString(), size: 1UL << 30, otherUsageDescription: "x"));
        StringAssert.Contains(ex.Message, "requires usage");
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void CreateVirtualDisk_InvalidTimeout_Throws()
    {
        McpException ex = Assert.ThrowsExactly<McpException>(() => StorageTools.CreateVirtualDisk(
            "Disk", storagePoolFriendlyName: Guid.NewGuid().ToString(), size: 1UL << 30, timeoutSeconds: 0));
        StringAssert.Contains(ex.Message, "timeoutSeconds");
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void CreateVirtualDisk_PoolNotFound_Throws()
    {
        string name = Guid.NewGuid().ToString();
        McpException ex = Assert.ThrowsExactly<McpException>(
            () => StorageTools.CreateVirtualDisk("Disk", storagePoolFriendlyName: name, size: 1UL << 30));
        StringAssert.Contains(ex.Message, "Storage pool not found");
        Assert.ThrowsExactly<McpException>(
            () => StorageTools.CreateVirtualDisk("Disk", storagePoolUniqueId: name, useMaximumSize: true));
    }

    [TestMethod]
    [OSCondition(ConditionMode.Exclude, OperatingSystems.Windows)]
    public void CreateVirtualDisk_NonWindows_Throws()
    {
        Assert.ThrowsExactly<McpException>(
            () => StorageTools.CreateVirtualDisk("Disk", storagePoolFriendlyName: "Pool", size: 1UL << 30));
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    [DataRow(null, null)]
    [DataRow(" ", "")]
    [DataRow("a", "b")]
    public void ResizeVirtualDisk_NotExactlyOneInput_Throws(string? friendlyName, string? uniqueId)
    {
        Assert.ThrowsExactly<McpException>(() => StorageTools.ResizeVirtualDisk(1UL << 30, friendlyName, uniqueId));
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void ResizeVirtualDisk_ZeroSize_Throws()
    {
        McpException ex = Assert.ThrowsExactly<McpException>(
            () => StorageTools.ResizeVirtualDisk(0, friendlyName: Guid.NewGuid().ToString()));
        StringAssert.Contains(ex.Message, "size");
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void ResizeVirtualDisk_InvalidTimeout_Throws()
    {
        McpException ex = Assert.ThrowsExactly<McpException>(
            () => StorageTools.ResizeVirtualDisk(1UL << 30, friendlyName: Guid.NewGuid().ToString(), timeoutSeconds: -1));
        StringAssert.Contains(ex.Message, "timeoutSeconds");
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void ResizeVirtualDisk_NotFound_Throws()
    {
        string name = Guid.NewGuid().ToString();
        McpException ex = Assert.ThrowsExactly<McpException>(() => StorageTools.ResizeVirtualDisk(1UL << 30, friendlyName: name));
        StringAssert.Contains(ex.Message, "Virtual disk not found");
        Assert.ThrowsExactly<McpException>(() => StorageTools.ResizeVirtualDisk(1UL << 30, uniqueId: name));
    }

    [TestMethod]
    [OSCondition(ConditionMode.Exclude, OperatingSystems.Windows)]
    public void ResizeVirtualDisk_NonWindows_Throws()
    {
        Assert.ThrowsExactly<McpException>(() => StorageTools.ResizeVirtualDisk(1UL << 30, friendlyName: "Source"));
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    [DataRow(null, null)]
    [DataRow(" ", "")]
    [DataRow("a", "b")]
    public void SetVirtualDisk_NotExactlyOneInput_Throws(string? friendlyName, string? uniqueId)
    {
        Assert.ThrowsExactly<McpException>(() => StorageTools.SetVirtualDisk(friendlyName, uniqueId, newFriendlyName: "x"));
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void SetVirtualDisk_NoChange_Throws()
    {
        McpException ex = Assert.ThrowsExactly<McpException>(
            () => StorageTools.SetVirtualDisk(friendlyName: Guid.NewGuid().ToString()));
        StringAssert.Contains(ex.Message, "at least one");
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void SetVirtualDisk_BlankNewFriendlyName_Throws()
    {
        McpException ex = Assert.ThrowsExactly<McpException>(
            () => StorageTools.SetVirtualDisk(friendlyName: Guid.NewGuid().ToString(), newFriendlyName: " "));
        StringAssert.Contains(ex.Message, "newFriendlyName");
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void SetVirtualDisk_OtherUsageDescriptionWithoutUsage_Throws()
    {
        McpException ex = Assert.ThrowsExactly<McpException>(
            () => StorageTools.SetVirtualDisk(friendlyName: Guid.NewGuid().ToString(), otherUsageDescription: "x"));
        StringAssert.Contains(ex.Message, "requires usage");
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void SetVirtualDisk_NotFound_Throws()
    {
        string name = Guid.NewGuid().ToString();
        McpException ex = Assert.ThrowsExactly<McpException>(
            () => StorageTools.SetVirtualDisk(friendlyName: name, access: VirtualDiskAccess.ReadWrite));
        StringAssert.Contains(ex.Message, "Virtual disk not found");
        Assert.ThrowsExactly<McpException>(() => StorageTools.SetVirtualDisk(uniqueId: name, isManualAttach: false));
    }

    [TestMethod]
    [OSCondition(ConditionMode.Exclude, OperatingSystems.Windows)]
    public void SetVirtualDisk_NonWindows_Throws()
    {
        Assert.ThrowsExactly<McpException>(() => StorageTools.SetVirtualDisk(friendlyName: "Source", newFriendlyName: "x"));
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    [DataRow(null, null)]
    [DataRow(" ", "")]
    [DataRow("a", "b")]
    public void RemoveVirtualDisk_NotExactlyOneInput_Throws(string? friendlyName, string? uniqueId)
    {
        Assert.ThrowsExactly<McpException>(() => StorageTools.RemoveVirtualDisk(friendlyName, uniqueId, confirm: true));
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void RemoveVirtualDisk_NotConfirmed_Throws()
    {
        McpException ex = Assert.ThrowsExactly<McpException>(
            () => StorageTools.RemoveVirtualDisk(friendlyName: Guid.NewGuid().ToString()));
        StringAssert.Contains(ex.Message, "confirm");
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void RemoveVirtualDisk_InvalidTimeout_Throws()
    {
        McpException ex = Assert.ThrowsExactly<McpException>(
            () => StorageTools.RemoveVirtualDisk(friendlyName: Guid.NewGuid().ToString(), confirm: true, timeoutSeconds: 0));
        StringAssert.Contains(ex.Message, "timeoutSeconds");
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void RemoveVirtualDisk_NotFound_Throws()
    {
        string name = Guid.NewGuid().ToString();
        McpException ex = Assert.ThrowsExactly<McpException>(() => StorageTools.RemoveVirtualDisk(friendlyName: name, confirm: true));
        StringAssert.Contains(ex.Message, "Virtual disk not found");
        Assert.ThrowsExactly<McpException>(() => StorageTools.RemoveVirtualDisk(uniqueId: name, confirm: true));
    }

    [TestMethod]
    [OSCondition(ConditionMode.Exclude, OperatingSystems.Windows)]
    public void RemoveVirtualDisk_NonWindows_Throws()
    {
        Assert.ThrowsExactly<McpException>(() => StorageTools.RemoveVirtualDisk(friendlyName: "Source", confirm: true));
    }

    [TestMethod]
    public void WithBizadmToolKit_RegistersStorageTools()
    {
        ServiceCollection services = new();
        services.AddMcpServer().WithBizadmToolKit();
        using ServiceProvider provider = services.BuildServiceProvider();
        List<string> names = provider.GetServices<McpServerTool>().Select(t => t.ProtocolTool.Name).ToList();
        CollectionAssert.Contains(names, "list_storage_pools");
        CollectionAssert.Contains(names, "get_storage_pool");
        CollectionAssert.Contains(names, "list_virtual_disks");
        CollectionAssert.Contains(names, "get_virtual_disk");
        CollectionAssert.Contains(names, "create_virtual_disk");
        CollectionAssert.Contains(names, "resize_virtual_disk");
        CollectionAssert.Contains(names, "set_virtual_disk");
        CollectionAssert.Contains(names, "remove_virtual_disk");
    }

    [TestMethod]
    [DataRow("create_virtual_disk", false)]
    [DataRow("resize_virtual_disk", false)]
    [DataRow("set_virtual_disk", false)]
    [DataRow("remove_virtual_disk", true)]
    public void VirtualDiskWriteTools_Annotations(string name, bool destructive)
    {
        ServiceCollection services = new();
        services.AddMcpServer().WithBizadmToolKit();
        using ServiceProvider provider = services.BuildServiceProvider();
        McpServerTool tool = provider.GetServices<McpServerTool>().Single(t => t.ProtocolTool.Name == name);
        Assert.AreEqual(destructive, tool.ProtocolTool.Annotations!.DestructiveHint);
        Assert.AreEqual(false, tool.ProtocolTool.Annotations.ReadOnlyHint);
        Assert.AreEqual(false, tool.ProtocolTool.Annotations.OpenWorldHint);
    }

    [TestMethod]
    public void CreateVirtualDisk_InputSchema_EnumsAreStringWithoutCancellationToken()
    {
        ServiceCollection services = new();
        services.AddMcpServer().WithBizadmToolKit();
        using ServiceProvider provider = services.BuildServiceProvider();
        McpServerTool tool = provider.GetServices<McpServerTool>()
            .Single(t => t.ProtocolTool.Name == "create_virtual_disk");
        string schema = tool.ProtocolTool.InputSchema.GetRawText();
        StringAssert.Contains(schema, "\"StorageRack\"");
        Assert.IsFalse(schema.Contains("cancellationToken", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void GetVirtualDisk_OutputSchema_EnumsAreString()
    {
        ServiceCollection services = new();
        services.AddMcpServer().WithBizadmToolKit();
        using ServiceProvider provider = services.BuildServiceProvider();
        McpServerTool tool = provider.GetServices<McpServerTool>()
            .Single(t => t.ProtocolTool.Name == "get_virtual_disk");
        string schema = tool.ProtocolTool.OutputSchema!.Value.GetRawText();
        StringAssert.Contains(schema, "\"Detached\"");
        StringAssert.Contains(schema, "\"ReadWrite\"");
    }

    [TestMethod]
    public void ListVirtualDisks_NullParityLayout_SerializeAllRequiredProperties()
    {
        ServiceCollection services = new();
        services.AddMcpServer().WithBizadmToolKit();
        using ServiceProvider provider = services.BuildServiceProvider();
        McpServerTool tool = provider.GetServices<McpServerTool>()
            .Single(t => t.ProtocolTool.Name == "list_virtual_disks");
        JsonElement diskSchema = tool.ProtocolTool.OutputSchema!.Value
            .GetProperty("properties").GetProperty("virtualDisks").GetProperty("items");

        VirtualDiskInfo disk = new("Source", "{id}", "{object}", [VirtualDiskOperationalStatus.OK],
            StorageHealthStatus.Healthy, "Mirror", StorageProvisioningType.Thin, VirtualDiskUsage.Data,
            2, 1, 4, 2, 1, 3, 262144, null, 4096, 4096, VirtualDiskMediaType.Unspecified,
            VirtualDiskAccess.ReadWrite, VirtualDiskDetachedReason.None, false, false, false, false, false, 0);
        JsonElement json = JsonSerializer.SerializeToElement(disk, McpJsonUtilities.DefaultOptions);

        foreach (JsonElement required in diskSchema.GetProperty("required").EnumerateArray())
            Assert.IsTrue(json.TryGetProperty(required.GetString()!, out _), required.GetString());
        Assert.AreEqual(JsonValueKind.Null, json.GetProperty("parityLayout").ValueKind);
    }

    [TestMethod]
    public void GetStoragePool_OutputSchema_HealthStatusIsString()
    {
        ServiceCollection services = new();
        services.AddMcpServer().WithBizadmToolKit();
        using ServiceProvider provider = services.BuildServiceProvider();
        McpServerTool tool = provider.GetServices<McpServerTool>()
            .Single(t => t.ProtocolTool.Name == "get_storage_pool");
        string schema = tool.ProtocolTool.OutputSchema!.Value.GetRawText();
        StringAssert.Contains(schema, "\"Healthy\"");
        StringAssert.Contains(schema, "\"Thin\"");
    }

    [TestMethod]
    public void ListStoragePools_NullFields_SerializeAllRequiredProperties()
    {
        ServiceCollection services = new();
        services.AddMcpServer().WithBizadmToolKit();
        using ServiceProvider provider = services.BuildServiceProvider();
        McpServerTool tool = provider.GetServices<McpServerTool>()
            .Single(t => t.ProtocolTool.Name == "list_storage_pools");
        JsonElement poolSchema = tool.ProtocolTool.OutputSchema!.Value
            .GetProperty("properties").GetProperty("storagePools").GetProperty("items");

        StoragePoolInfo pool = new("Primordial", "{id}", "{object}", [StorageOperationalStatus.OK],
            StorageHealthStatus.Healthy, true, false, false, 1, 0, null, null, null,
            StorageProvisioningType.Fixed, StoragePoolUsage.Other, 29);
        JsonElement json = JsonSerializer.SerializeToElement(pool, McpJsonUtilities.DefaultOptions);

        foreach (JsonElement required in poolSchema.GetProperty("required").EnumerateArray())
            Assert.IsTrue(json.TryGetProperty(required.GetString()!, out _), required.GetString());
        Assert.AreEqual(JsonValueKind.Null, json.GetProperty("logicalSectorSize").ValueKind);
    }

    private static void AssertSamePool(StoragePoolInfo expected, StoragePoolInfo actual)
    {
        Assert.AreEqual(expected.UniqueId, actual.UniqueId);
        Assert.AreEqual(expected.FriendlyName, actual.FriendlyName);
        Assert.AreEqual(expected.ObjectId, actual.ObjectId);
        Assert.AreEqual(expected.Size, actual.Size);
        Assert.AreEqual(expected.HealthStatus, actual.HealthStatus);
        CollectionAssert.AreEqual(expected.OperationalStatus.ToList(), actual.OperationalStatus.ToList());
    }

    private static void AssertSameVirtualDisk(VirtualDiskInfo expected, VirtualDiskInfo actual)
    {
        Assert.AreEqual(expected.UniqueId, actual.UniqueId);
        Assert.AreEqual(expected.FriendlyName, actual.FriendlyName);
        Assert.AreEqual(expected.ObjectId, actual.ObjectId);
        Assert.AreEqual(expected.Size, actual.Size);
        Assert.AreEqual(expected.HealthStatus, actual.HealthStatus);
        CollectionAssert.AreEqual(expected.OperationalStatus.ToList(), actual.OperationalStatus.ToList());
    }

    // Tool 구현과 독립된 기준값: MSFT_VirtualDisk 전체 개수를 CIM으로 직접 조회
    private static int CountVirtualDisksByCim()
    {
        using CimSession session = CimSession.Create(null);
        int count = 0;
        foreach (CimInstance instance in session.QueryInstances("root/Microsoft/Windows/Storage", "WQL", "SELECT * FROM MSFT_VirtualDisk"))
        {
            using (instance)
                count++;
        }
        return count;
    }
}
