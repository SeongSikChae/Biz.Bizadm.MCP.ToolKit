---
status: done
category: storage
feature: storage-virtual-disk
phase: 개발
---

# storage-virtual-disk 개발

선행: `0005-storage-virtual-disk-설계.md`

## 단위 1. 모델(enum·record)

- **대상**: `Models/Storage/VirtualDiskOperationalStatus.cs`, `VirtualDiskUsage.cs`, `VirtualDiskAccess.cs`, `VirtualDiskDetachedReason.cs`, `VirtualDiskMediaType.cs`, `VirtualDiskParityLayout.cs`, `VirtualDiskInfo.cs`, `VirtualDiskListResult.cs` (모두 신규)
- **코드 초안**:

```csharp
/// <summary>가상 디스크 접근 유형(MSFT_VirtualDisk.Access)</summary>
[JsonConverter(typeof(JsonStringEnumConverter<VirtualDiskAccess>))]
public enum VirtualDiskAccess : ushort
{
    /// <summary>알 수 없음</summary>
    Unknown = 0,

    /// <summary>읽기 가능</summary>
    Readable = 1,

    /// <summary>쓰기 가능</summary>
    Writeable = 2,

    /// <summary>읽기·쓰기</summary>
    ReadWrite = 3,

    /// <summary>한 번 쓰기</summary>
    WriteOnce = 4,
}

// VirtualDiskOperationalStatus: Unknown = 0 ... Relocating = 19, Detached = 0xD002, Incomplete = 0xD003,
//                               Suboptimal = 0xD01B, NoRedundancy = 0xD024
// VirtualDiskUsage, VirtualDiskDetachedReason, VirtualDiskMediaType, VirtualDiskParityLayout: 설계 표의 값

/// <summary>가상 디스크 목록 조회 결과</summary>
/// <param name="VirtualDisks">가상 디스크 목록</param>
public sealed record VirtualDiskListResult(IReadOnlyList<VirtualDiskInfo> VirtualDisks);

/// <summary>저장소 공간(가상 디스크) 정보(MSFT_VirtualDisk)</summary>
/// <remarks>출력 스키마가 모든 속성을 required로 두므로 nullable 속성도 null을 그대로 쓴다.</remarks>
// ... <param> 27개 한국어 주석
public sealed record VirtualDiskInfo(
    string FriendlyName,
    // ... 설계 순서대로
    ulong Interleave,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] VirtualDiskParityLayout? ParityLayout,
    ulong LogicalSectorSize,
    // ...
    ulong WriteCacheSize);
```

- **검증**: `dotnet build` 경고·오류 없음
- **선행 단위**: 없음

## 단위 2. 단건 선택 헬퍼 추출

- **대상**: `Tools/StorageTools.cs` — `GetStoragePool`, private `EnsureExactlyOne`(신규), `SelectOne<T>`(신규)
- **코드 초안**:

```csharp
public static StoragePoolInfo GetStoragePool(string? friendlyName = null, string? uniqueId = null)
{
    EnsureWindows();
    EnsureExactlyOne(friendlyName, uniqueId);
    return SelectOne(QueryStoragePools(), p => p.FriendlyName, p => p.UniqueId,
        friendlyName, uniqueId, "Storage pool", "storage pools");
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
```

- 입력 검증(`EnsureExactlyOne`)은 CIM 조회 전에 호출해 기존 순서(OS → 입력 → 조회)를 유지한다. `SelectOne`은 검증된 입력만 받는다.
- `get_storage_pool` 오류 문구는 `kind`/`plural` 치환으로 기존과 같다.
- **검증**: `dotnet build` 성공, 기존 `StorageToolsTests` 전체 통과(회귀)
- **선행 단위**: 없음

## 단위 3. get_virtual_disk

- **대상**: `Tools/StorageTools.cs` — `GetVirtualDisk`, private `QueryVirtualDisks`, `ToVirtualDiskInfo`; `StorageToolsTests.cs`
- **코드 초안**:

```csharp
public static VirtualDiskInfo GetVirtualDisk(string? friendlyName = null, string? uniqueId = null)
{
    EnsureWindows();
    EnsureExactlyOne(friendlyName, uniqueId);
    return SelectOne(QueryVirtualDisks(), d => d.FriendlyName, d => d.UniqueId,
        friendlyName, uniqueId, "Virtual disk", "virtual disks");
}

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
    // ...
    ((ushort[]?)p["OperationalStatus"].Value ?? []).Select(s => (VirtualDiskOperationalStatus)s).ToList(),
    (StorageHealthStatus)(ushort)p["HealthStatus"].Value,
    // ...
    (VirtualDiskParityLayout?)(ushort?)p["ParityLayout"].Value,
    // ...
    (ulong)p["WriteCacheSize"].Value);
```

```csharp
[TestMethod]
[OSCondition(OperatingSystems.Windows)]
public void GetVirtualDisk_NotFound_Throws()
{
    string name = Guid.NewGuid().ToString();
    Assert.ThrowsExactly<McpException>(() => StorageTools.GetVirtualDisk(friendlyName: name));
    Assert.ThrowsExactly<McpException>(() => StorageTools.GetVirtualDisk(uniqueId: name));
}
```

- 테스트 추가: `GetVirtualDisk_NotExactlyOneInput_Throws`(DataRow `null,null` / `" ",""` / `"a","b"`), `GetVirtualDisk_NotFound_Throws`, `GetVirtualDisk_NonWindows_Throws`(Exclude Windows).
- **검증**: `dotnet build` 성공, 추가 테스트 통과. 필드 변환은 `GetVirtualDisk(friendlyName: "Source")` 결과를 PowerShell `Get-VirtualDisk -FriendlyName Source | Select ResiliencySettingName,NumberOfDataCopies,NumberOfColumns,FootprintOnPool` 출력과 대조한다(사전 조회값: Mirror, 2, 3, 52076478464)
- **선행 단위**: 1, 2

## 단위 4. list_virtual_disks

- **대상**: `Tools/StorageTools.cs` — `ListVirtualDisks`; `StorageToolsTests.cs`
- **코드 초안**:

```csharp
public static VirtualDiskListResult ListVirtualDisks(string? friendlyName = null, string? uniqueId = null)
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
```

- `SelectOne`의 `McpException`은 `catch` 필터에 걸리지 않으므로 풀 미존재·중복 메시지가 그대로 전달된다.

```csharp
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
```

- 테스트 프로젝트는 `ProjectReference`로 `Microsoft.Management.Infrastructure`를 전이 참조하므로 패키지 추가 없이 `CimSession`을 쓴다.

- 테스트 추가:
  - `ListVirtualDisks_AllPools_CoverAllVirtualDisks`: 모든 풀의 `list_virtual_disks` 합집합 `UniqueId`가 중복 없이 나오고, 개수가 CIM 직접 조회한 `MSFT_VirtualDisk` 전체 개수와 같으며, 각 항목을 `GetVirtualDisk(uniqueId 소문자)`로 재조회한 결과와 `FriendlyName`·`ObjectId`·`Size`·`HealthStatus`·`OperationalStatus`가 같음
  - `ListVirtualDisks_Primordial_ReturnsEmpty`: `IsPrimordial` 풀 → 빈 목록
  - `GetVirtualDisk_ByFriendlyName_MatchesList`: 유일 이름을 대문자로 조회
  - `ListVirtualDisks_NotExactlyOneInput_Throws`, `ListVirtualDisks_PoolNotFound_Throws`, `ListVirtualDisks_NonWindows_Throws`
- **검증**: 추가 테스트 통과(개수 대조는 테스트 안의 CIM 직접 조회 단언으로 자동화), 참고로 PowerShell `(Get-VirtualDisk).Count`(이 PC 9)와도 같음
- **선행 단위**: 3

## 단위 5. 등록·스키마 테스트

- **대상**: `StorageToolsTests.cs`
- **코드 초안**:

```csharp
[TestMethod]
public void GetVirtualDisk_OutputSchema_EnumsAreString()
{
    // ... 기존 GetStoragePool_OutputSchema_HealthStatusIsString과 같은 방식
    StringAssert.Contains(schema, "\"Detached\"");
    StringAssert.Contains(schema, "\"ReadWrite\"");
}

// WithBizadmToolKit_RegistersStorageTools에 추가
CollectionAssert.Contains(names, "list_virtual_disks");
CollectionAssert.Contains(names, "get_virtual_disk");
```

- 테스트 추가: `ListVirtualDisks_NullParityLayout_SerializeAllRequiredProperties` — `ParityLayout = null`인 `VirtualDiskInfo`를 `McpJsonUtilities.DefaultOptions`로 직렬화, `list_virtual_disks` 출력 스키마 `virtualDisks.items.required` 속성이 모두 있고 `parityLayout`이 JSON null.
- **검증**: `dotnet test` 전체 통과(비Windows 테스트는 이 PC에서 건너뜀 — 미검증으로 보고)
- **선행 단위**: 4

## 순서

1 → 2 → 3 → 4 → 5

## 수용한 위험 (검토 반영)

- 비필수 CIM 값(`Interleave`, `IsTiered` 등)이 null이거나 속성이 없는 환경(Storage Spaces가 아닌 LUN, 옛 Windows)에서는 조회 전체가 `Failed to query virtual disks` 오류가 된다. 풀 Tool과 같은 정책이며 설계 합의대로 둔다.
- 풀 인스턴스 `ToList()` 열거 중 예외가 나면 이미 받은 인스턴스 일부가 dispose되지 않는다. 기존 `QueryStoragePools`와 같은 수준이라 별도 방어 코드를 넣지 않는다.
