using System.Text.Json;
using Biz.Bizadm.MCP.ToolKit.Models.Storage;
using Biz.Bizadm.MCP.ToolKit.Tools;
using Microsoft.Extensions.DependencyInjection;
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
    public void WithBizadmToolKit_RegistersStorageTools()
    {
        ServiceCollection services = new();
        services.AddMcpServer().WithBizadmToolKit();
        using ServiceProvider provider = services.BuildServiceProvider();
        List<string> names = provider.GetServices<McpServerTool>().Select(t => t.ProtocolTool.Name).ToList();
        CollectionAssert.Contains(names, "list_storage_pools");
        CollectionAssert.Contains(names, "get_storage_pool");
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
}
