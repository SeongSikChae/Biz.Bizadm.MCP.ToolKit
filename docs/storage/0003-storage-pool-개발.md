---
status: done
category: storage
feature: storage-pool
phase: 개발
---

# storage-pool 개발

선행: `0002-storage-pool-설계.md`

## 단위 1. 패키지 추가

- **대상**: `Directory.Packages.props`, `src/Biz.Bizadm.MCP.ToolKit/Biz.Bizadm.MCP.ToolKit.csproj`
- **코드 초안**:

```xml
<!-- Directory.Packages.props -->
<PackageVersion Include="Microsoft.Management.Infrastructure" Version="3.0.0" />

<!-- Biz.Bizadm.MCP.ToolKit.csproj -->
<PackageReference Include="Microsoft.Management.Infrastructure" />
```

- **검증**: `dotnet build` 성공, `dotnet package list --vulnerable --include-transitive`·`--deprecated`에 이 패키지 없음
- **선행 단위**: 없음

## 단위 2. 모델(enum·record)

- **대상**: `Models/Storage/StorageOperationalStatus.cs`, `StorageHealthStatus.cs`, `StorageProvisioningType.cs`, `StoragePoolUsage.cs`, `StoragePoolInfo.cs`, `StoragePoolListResult.cs`
- **코드 초안**:

```csharp
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

// StorageOperationalStatus: ... PowerMode = 18, Relocating = 19, ReadOnly = 0xD000, Incomplete = 0xD001
// StorageProvisioningType, StoragePoolUsage: 설계 표의 값

/// <summary>저장소 풀 목록 조회 결과</summary>
/// <param name="StoragePools">저장소 풀 목록</param>
public sealed record StoragePoolListResult(IReadOnlyList<StoragePoolInfo> StoragePools);

// StoragePoolInfo: 설계의 16개 필드, 각 <param> 한국어 주석
```

- **검증**: `dotnet build` 성공
- **선행 단위**: 없음

## 단위 3. StorageTools

- **대상**: `Tools/StorageTools.cs` — `ListStoragePools`, `GetStoragePool`, private `EnsureWindows`, `QueryStoragePools`, `ToStoragePoolInfo`
- **코드 초안**:

```csharp
private const string StorageNamespace = "root/Microsoft/Windows/Storage";

public static StoragePoolListResult ListStoragePools()
{
    EnsureWindows();
    return new StoragePoolListResult(QueryStoragePools());
}

public static StoragePoolInfo GetStoragePool(string? friendlyName = null, string? uniqueId = null)
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

[SupportedOSPlatform("windows")]
private static List<StoragePoolInfo> QueryStoragePools()
{
    try
    {
        using CimSession session = CimSession.Create(null);
        List<StoragePoolInfo> pools = [];
        foreach (CimInstance instance in session.QueryInstances(StorageNamespace, "WQL", "SELECT * FROM MSFT_StoragePool"))
            using (instance)
                pools.Add(ToStoragePoolInfo(instance.CimInstanceProperties));
        return pools;
    }
    catch (CimException ex)
    {
        throw new McpException($"Failed to query storage pools: {ex.Message}", ex);
    }
}

private static StoragePoolInfo ToStoragePoolInfo(CimKeyedCollection<CimProperty> p) => new(
    (string)p["FriendlyName"].Value,
    // ...
    ((ushort[]?)p["OperationalStatus"].Value ?? []).Select(s => (StorageOperationalStatus)s).ToList(),
    (StorageHealthStatus)(ushort)p["HealthStatus"].Value,
    // ...
    (ulong?)p["LogicalSectorSize"].Value,
    // ...
    (ushort)p["Version"].Value);
```

- `EnsureWindows`가 `[SupportedOSPlatformGuard("windows")]`가 아니므로 CA1416 경고가 나면 `if (!OperatingSystem.IsWindows())` 를 각 Tool 안에 직접 두는 형태로 맞춘다(기능 동일).
- **검증**: `dotnet build` 경고·오류 없음
- **선행 단위**: 1, 2

## 단위 4. 등록

- **대상**: `BizadmToolKitMcpServerBuilderExtensions.WithBizadmToolKit`
- **코드 초안**:

```csharp
builder.WithTools<AudioLevelTools>();
builder.WithTools<StorageTools>();
```

- **검증**: 기존 `BizadmToolKitMcpServerBuilderExtensionsTests` 통과
- **선행 단위**: 3

## 단위 5. 테스트

- **대상**: `tests/Biz.Bizadm.MCP.ToolKit.Tests/StorageToolsTests.cs`
- **코드 초안**:

```csharp
[TestMethod]
[OSCondition(OperatingSystems.Windows)]
public void ListStoragePools_ReturnsPools()
{
    StoragePoolListResult result = StorageTools.ListStoragePools();
    Assert.IsNotEmpty(result.StoragePools);
}

[TestMethod]
[OSCondition(OperatingSystems.Windows)]
public void GetStoragePool_ByUniqueId_MatchesList()
{
    StoragePoolInfo expected = StorageTools.ListStoragePools().StoragePools[0];
    Assert.AreEqual(expected, StorageTools.GetStoragePool(uniqueId: expected.UniqueId));  // record 값 비교: 목록 필드는 SequenceEqual로 별도 비교
}

[TestMethod]
[OSCondition(OperatingSystems.Windows)]
[DataRow(null, null)]
[DataRow("a", "b")]
public void GetStoragePool_NotExactlyOneInput_Throws(string? friendlyName, string? uniqueId) =>
    Assert.ThrowsExactly<McpException>(() => StorageTools.GetStoragePool(friendlyName, uniqueId));

[TestMethod]
[OSCondition(ConditionMode.Exclude, OperatingSystems.Windows)]
public void ListStoragePools_NonWindows_Throws() =>
    Assert.ThrowsExactly<McpException>(() => StorageTools.ListStoragePools());
```

- 그 외: `GetStoragePool_ByFriendlyName`(Primordial 등 중복 없는 이름), `GetStoragePool_NotFound_Throws`(GUID 이름), `GetStoragePool_NonWindows_Throws`.
- **검증**: `dotnet test` 전체 통과(비Windows 테스트는 이 PC에서 건너뜀), PowerShell `Get-StoragePool | Select FriendlyName,UniqueId,Size` 결과와 Tool 결과 대조
- **선행 단위**: 4

## 순서

1 → 2 → 3 → 4 → 5
