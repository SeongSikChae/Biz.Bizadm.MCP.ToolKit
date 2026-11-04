---
status: done
category: storage
feature: storage-virtual-disk-write
phase: 개발
---

# storage-virtual-disk-write 개발

선행: `0008-storage-virtual-disk-write-설계.md`

단위마다 구현한 뒤 검증하고, 통과해야 다음 단위로 넘어간다. 자동 테스트는 CIM 메서드를 호출하기 전에 실패하는 경로만 다룬다. 로컬 디스크는 바꾸지 않는다.

## U1. 모델

- **대상**: `Models/Storage/VirtualDiskFaultDomainType.cs`, `Models/Storage/VirtualDiskRemoveResult.cs` (신규)
- **코드 초안**:

```csharp
/// <summary>가상 디스크 장애 도메인 유형(MSFT_StoragePool.CreateVirtualDisk FaultDomainAwareness·ColumnIsolation)</summary>
[JsonConverter(typeof(JsonStringEnumConverter<VirtualDiskFaultDomainType>))]
public enum VirtualDiskFaultDomainType : ushort
{
    PhysicalDisk = 1,
    StorageEnclosure = 2,
    StorageScaleUnit = 3,
    StorageChassis = 4,
    StorageRack = 5,
}

/// <summary>삭제한 가상 디스크</summary>
public sealed record VirtualDiskRemoveResult(string FriendlyName, string UniqueId);
```

- **검증**: `dotnet build` 종료 코드 0
- **선행 단위**: 없음

## U2. CIM 메서드 호출·job 대기 헬퍼

- **대상**: `Tools/StorageTools.cs`의 private `InParam`, `Invoke`, `WaitForJob`, `ExtendedMessage`, `QueryInstances`, `SelectByName`
- **코드 초안**:

```csharp
private const uint JobStarted = 4096;

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
            if (state == 7)
                return;
            if (state is 8 or 9 or 10)
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
```

- `SelectByName`: 기존 `ListVirtualDisks`의 풀 선택 람다를 CimInstance용으로 묶는다. `ListVirtualDisks`도 이 헬퍼를 쓰도록 바꾸되 동작은 그대로 둔다.

```csharp
private static CimInstance SelectByName(List<CimInstance> items, string? friendlyName, string? uniqueId,
    string kind, string plural) =>
    SelectOne(items,
        i => (string)i.CimInstanceProperties["FriendlyName"].Value,
        i => (string)i.CimInstanceProperties["UniqueId"].Value,
        friendlyName, uniqueId, kind, plural);
```

- 취소 예외(`OperationCanceledException`)는 Tool 메서드의 `catch (... when ex is CimException or ...)` 필터에 걸리지 않으므로 그대로 SDK에 전달된다.
- `CimMethodParametersCollection`은 컬렉션 식(`[...]`) 대신 `new()` 뒤에 `Add`를 쓴다.
- **검증**: `dotnet build` 종료 코드 0, 기존 `StorageToolsTests` 전부 통과(`ListVirtualDisks` 회귀 확인)
- **선행 단위**: U1

## U3. remove_virtual_disk

- **대상**: `StorageTools.RemoveVirtualDisk`, 테스트
- **코드 초안**:

```csharp
public static VirtualDiskRemoveResult RemoveVirtualDisk(
    string? friendlyName = null, string? uniqueId = null, bool confirm = false, int timeoutSeconds = 300,
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
```

- `WithVirtualDisk<T>(friendlyName, uniqueId, action, Func<CimSession, CimInstance, T>)` 헬퍼는 다음 순서로 동작한다: 세션 생성 → `MSFT_VirtualDisk` 조회 → `SelectByName` → 콜백 → 인스턴스 dispose. 예외(`CimException`, `NullReferenceException`, `InvalidCastException`)는 `Failed to {action} virtual disk: ...` 메시지의 `McpException`으로 감싼다. 이 헬퍼는 U4·U5에서도 쓴다.
- `EnsureTimeout`: `timeoutSeconds <= 0`이면 `McpException("timeoutSeconds must be greater than 0")`
- **테스트**:
  - 식별자가 정확히 하나가 아님 → 예외(DataRow 3개)
  - 존재하는 이름이라도 `confirm=false`이면 예외. GUID 이름으로 호출해 CIM 호출 전에 실패하는지 확인한다.
  - `confirm=true`, `timeoutSeconds=0` → 예외
  - `confirm=true`, GUID 이름 → `Virtual disk not found` 예외
  - 비Windows 테스트(Exclude)
- **검증**: `dotnet test --filter RemoveVirtualDisk` 통과
- **선행 단위**: U2

## U4. resize_virtual_disk

- **대상**: `StorageTools.ResizeVirtualDisk`, 테스트
- **코드 초안**:

```csharp
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
```

- **테스트**: 식별자 오류, `size=0`, `timeoutSeconds=0`, GUID 이름 → not found, 비Windows
- **검증**: `dotnet test --filter ResizeVirtualDisk` 통과
- **선행 단위**: U3

## U5. set_virtual_disk

- **대상**: `StorageTools.SetVirtualDisk`, 테스트
- **코드 초안**:

```csharp
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
    if (usage is not null)
    {
        using CimMethodParametersCollection args = new();
        args.Add(InParam("Usage", (ushort)usage, CimType.UInt16));
        if (otherUsageDescription is not null)
            args.Add(InParam("OtherUsageDescription", otherUsageDescription, CimType.String));
        Invoke(session, disk, "SetUsage", args, "set usage of", 0, CancellationToken.None, out _);
    }
    if (isManualAttach is not null || storageNodeName is not null || access is not null)
    {
        using CimMethodParametersCollection args = new();
        // ... 값이 있는 IsManualAttach(Boolean), StorageNodeName(String), Access(UInt16)만 추가
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
// 반환은 U4와 같이 uniqueId로 다시 조회한다.
```

- Set 계열 메서드에는 `CreatedStorageJob` 출력이 없으므로 4096이 오지 않는다. 그래서 `timeoutSeconds`는 0으로 넘긴다.
- **테스트**: 식별자 오류, 변경 항목 없음, `newFriendlyName=" "`, `otherUsageDescription`만 지정, GUID 이름 → not found, 비Windows
- **검증**: `dotnet test --filter SetVirtualDisk` 통과
- **선행 단위**: U3

## U6. create_virtual_disk

- **대상**: `StorageTools.CreateVirtualDisk`, 테스트
- **코드 초안**:

```csharp
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

using CimMethodParametersCollection args = new();
args.Add(InParam("FriendlyName", friendlyName, CimType.String));
if (size is ulong s) args.Add(InParam("Size", s, CimType.UInt64));
if (useMaximumSize) args.Add(InParam("UseMaximumSize", true, CimType.Boolean));
if (provisioningType is { } pt) args.Add(InParam("ProvisioningType", (ushort)pt, CimType.UInt16));
// ... 나머지 선택 인자도 같은 패턴(설계 CIM 매핑 표의 이름·형식)

// 풀 선택은 ListVirtualDisks와 같다(MSFT_StoragePool 조회 → SelectByName → dispose).
CimMethodResult result = Invoke(session, pool, "CreateVirtualDisk", args, "create", timeoutSeconds,
    cancellationToken, out bool ranAsJob);
if (!ranAsJob)
{
    using CimInstance created = (CimInstance)result.OutParameters["CreatedVirtualDisk"].Value;
    return ToVirtualDiskInfo(created.CimInstanceProperties);
}
// job 경로: 출력 구조를 추정하지 않고 풀에서 이름으로 다시 찾는다.
List<VirtualDiskInfo> disks = [];
foreach (CimInstance disk in session.EnumerateAssociatedInstances(StorageNamespace, pool,
    "MSFT_StoragePoolToVirtualDisk", "MSFT_VirtualDisk", "StoragePool", "VirtualDisk"))
{
    using (disk)
        disks.Add(ToVirtualDiskInfo(disk.CimInstanceProperties));
}
return SelectOne(disks, d => d.FriendlyName, d => d.UniqueId, friendlyName, null, "Virtual disk", "virtual disks");
```

- 동기 경로의 출력 `CreatedVirtualDisk`는 로컬 `Get-CimClass`로 확인한 `CreateVirtualDisk`의 Out 인자다.
- job 경로의 연결 열거는 `ListVirtualDisks`와 같은 코드다. 구현할 때 private 헬퍼(`EnumeratePoolDisks`)로 묶어 두 곳이 함께 쓴다.
- job 경로는 자동 테스트로 재현할 수 없다. U8에서 job이 실제로 발생하지 않으면 미검증으로 남긴다.
- **테스트**: 풀 식별자 오류(DataRow), `friendlyName` 공백, size/useMaximumSize 둘 다 또는 둘 다 없음, columns 충돌, writeCache 충돌, `otherUsageDescription`만 지정, `timeoutSeconds=0`, GUID 풀 → `Storage pool not found`, 비Windows
- **검증**: `dotnet test --filter CreateVirtualDisk` 통과
- **선행 단위**: U2

## U7. 등록·스키마

- **대상**: `StorageToolsTests`
- **코드 초안**:

```csharp
CollectionAssert.Contains(names, "create_virtual_disk");
// ... resize_virtual_disk, set_virtual_disk, remove_virtual_disk

McpServerTool remove = tools.Single(t => t.ProtocolTool.Name == "remove_virtual_disk");
Assert.AreEqual(true, remove.ProtocolTool.Annotations!.DestructiveHint);
// create/resize/set은 DestructiveHint false, ReadOnlyHint false (모두 bool? 이므로 AreEqual로 비교)

string input = tools.Single(t => t.ProtocolTool.Name == "create_virtual_disk").ProtocolTool.InputSchema.GetRawText();
StringAssert.Contains(input, "\"StorageRack\"");
Assert.IsFalse(input.Contains("cancellationToken", StringComparison.OrdinalIgnoreCase));
```

- `ToolAnnotations.DestructiveHint`, `ReadOnlyHint`, `IdempotentHint`는 SDK 2.3.1에서 `bool?`이다(SDK XML 문서 확인).

- **검증**: 전체 `dotnet test` 통과
- **선행 단위**: U3–U6

## U8. 수동 E2E (선택)

- **대상**: 빌드한 MCP 서버
- 사용자가 테스트용 풀과 디스크 이름을 지정하고 승인할 때만 실행한다.
- 순서: `create_virtual_disk`(Simple, 작은 size) → `get_virtual_disk` → `resize_virtual_disk`(더 큰 size) → `set_virtual_disk`(newFriendlyName) → `remove_virtual_disk(confirm=true)` → `get_virtual_disk` not found
- **검증**: 단계마다 반환값의 `Size`, `FriendlyName`이 바뀌었는지, 마지막 조회에서 not found가 나는지 확인한다. 승인이 없으면 미검증으로 보고한다.
- **선행 단위**: U7

## 순서

U1 → U2 → U3 → (U4, U5) → U6 → U7 → U8
