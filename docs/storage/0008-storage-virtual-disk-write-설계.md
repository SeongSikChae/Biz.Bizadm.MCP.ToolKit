---
status: done
category: storage
feature: storage-virtual-disk-write
phase: 설계
---

# storage-virtual-disk-write 설계

선행: `0007-storage-virtual-disk-write-방안.md`
참고: New/Resize/Set/Remove-VirtualDisk (windowsserver2025-ps), 로컬 `Get-CimClass`로 확인한 `MSFT_StoragePool`, `MSFT_VirtualDisk`, `MSFT_StorageJob` 메서드·속성 (namespace `root/Microsoft/Windows/Storage`)

## 구조

```
src/Biz.Bizadm.MCP.ToolKit/
  Tools/
    StorageTools.cs                       # Tool 4개, CIM 메서드 호출·job 대기 헬퍼 추가
  Models/
    Storage/
      VirtualDiskFaultDomainType.cs       # enum (신규)
      VirtualDiskRemoveResult.cs          # 삭제 결과 record (신규)
tests/Biz.Bizadm.MCP.ToolKit.Tests/
  StorageToolsTests.cs                    # 입력 검증·등록·스키마 테스트 추가
```

- 메서드 이름은 `CreateVirtualDisk`, `ResizeVirtualDisk`, `SetVirtualDisk`, `RemoveVirtualDisk`로 한다.
- `BizadmToolKitMcpServerBuilderExtensions`와 패키지 참조는 바꾸지 않는다.

## 인터페이스

```csharp
[McpServerTool(Name = "create_virtual_disk", UseStructuredContent = true,
    ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
public static VirtualDiskInfo CreateVirtualDisk(
    string friendlyName,                          // 새 가상 디스크 이름(필수)
    string? storagePoolFriendlyName = null,       // 풀 지정: 둘 중 정확히 하나
    string? storagePoolUniqueId = null,
    ulong? size = null,                           // size와 useMaximumSize 중 정확히 하나
    bool useMaximumSize = false,
    string? resiliencySettingName = null,         // Simple, Mirror, Parity 등(CIM이 검증)
    StorageProvisioningType? provisioningType = null,
    VirtualDiskUsage? usage = null,
    string? otherUsageDescription = null,
    ulong? allocationUnitSize = null,
    VirtualDiskMediaType? mediaType = null,
    bool? isEnclosureAware = null,
    VirtualDiskFaultDomainType? faultDomainAwareness = null,
    VirtualDiskFaultDomainType? columnIsolation = null,
    ushort? physicalDiskRedundancy = null,
    ushort? numberOfDataCopies = null,
    ushort? numberOfColumns = null,               // numberOfColumns와 autoNumberOfColumns는 함께 쓰지 않음
    bool autoNumberOfColumns = false,
    ushort? numberOfGroups = null,
    ulong? interleave = null,
    ulong? writeCacheSize = null,                 // writeCacheSize와 autoWriteCacheSize는 함께 쓰지 않음
    bool autoWriteCacheSize = false,
    ulong? readCacheSize = null,
    int timeoutSeconds = 300,
    CancellationToken cancellationToken = default);

[McpServerTool(Name = "resize_virtual_disk", UseStructuredContent = true,
    ReadOnly = false, Idempotent = true, Destructive = false, OpenWorld = false)]
public static VirtualDiskInfo ResizeVirtualDisk(
    ulong size, string? friendlyName = null, string? uniqueId = null, int timeoutSeconds = 300,
    CancellationToken cancellationToken = default);

[McpServerTool(Name = "set_virtual_disk", UseStructuredContent = true,
    ReadOnly = false, Idempotent = true, Destructive = false, OpenWorld = false)]
public static VirtualDiskInfo SetVirtualDisk(
    string? friendlyName = null, string? uniqueId = null,
    string? newFriendlyName = null,
    VirtualDiskUsage? usage = null, string? otherUsageDescription = null,
    bool? isManualAttach = null, string? storageNodeName = null, VirtualDiskAccess? access = null);

[McpServerTool(Name = "remove_virtual_disk", UseStructuredContent = true,
    ReadOnly = false, Idempotent = false, Destructive = true, OpenWorld = false)]
public static VirtualDiskRemoveResult RemoveVirtualDisk(
    string? friendlyName = null, string? uniqueId = null, bool confirm = false, int timeoutSeconds = 300,
    CancellationToken cancellationToken = default);
```

```csharp
// Models/Storage/VirtualDiskRemoveResult.cs
public sealed record VirtualDiskRemoveResult(string FriendlyName, string UniqueId);
```

- 각 Tool과 인자에는 기존 Tool처럼 한국어 `[Description]`을 붙인다. 삭제 Tool의 설명에는 "데이터가 영구 삭제된다"와 "`confirm=true`가 필요하다"를 적는다.
- 생성 Tool의 풀 인자 이름은 cmdlet과 같게 `storagePoolFriendlyName`/`storagePoolUniqueId`로 한다. `friendlyName`은 새 디스크 이름이다.
- `CancellationToken`은 MCP SDK 2.3.1이 요청의 취소 토큰으로 자동 바인딩하며 입력 스키마에 나타나지 않는다(SDK XML 문서 확인). job을 시작할 수 있는 create/resize/remove에만 둔다. set은 job이 없으므로 두지 않는다.

## enum

| enum | 값 |
| --- | --- |
| `VirtualDiskFaultDomainType` | 1 PhysicalDisk, 2 StorageEnclosure, 3 StorageScaleUnit, 4 StorageChassis, 5 StorageRack |

- 로컬 `CreateVirtualDisk` 메서드의 `FaultDomainAwareness`/`ColumnIsolation` ValueMap(1–5)과 MS 문서를 따른다. `ushort` 기반이고 `JsonStringEnumConverter`를 적용한다(기존 Storage enum과 같음).
- 나머지 enum은 기존 것을 재사용한다: `StorageProvisioningType`, `VirtualDiskUsage`, `VirtualDiskMediaType`, `VirtualDiskAccess`. 값 범위는 CIM이 검증한다.

## CIM 메서드 매핑

| Tool | 대상 인스턴스 | 메서드 | 인자 |
| --- | --- | --- | --- |
| create | `MSFT_StoragePool` (풀 선택) | `CreateVirtualDisk` | 위 인자 중 값이 있는 것만. bool 스위치는 true일 때만 넣는다. enum은 `UInt16`이다. |
| resize | `MSFT_VirtualDisk` | `Resize` | `Size` |
| set | `MSFT_VirtualDisk` | `SetUsage` → `SetAttributes` → `SetFriendlyName` | 해당 인자가 있을 때만 호출한다. |
| remove | `MSFT_VirtualDisk` | `DeleteObject` | 없음 |

- 호출은 `session.InvokeMethod(StorageNamespace, instance, methodName, parameters)`이고, 인자는 `CimMethodParameter.Create(name, value, CimType, CimFlags.In)`로 만든다.
- `RunAsJob`은 넘기지 않는다(cmdlet 기본 동기 동작).
- set 순서: 이름 변경을 마지막에 둬서, 앞 단계가 실패해도 대상 이름이 바뀌지 않게 한다. 중간에 실패하면 앞서 적용한 변경은 되돌리지 않는다. Tool 설명에도 적는다.
- 대상 선택은 기존 `SelectOne`을 CimInstance 목록에 적용한다(`ListVirtualDisks`의 풀 선택과 같은 방식). 조회한 인스턴스는 모두 dispose한다.

## 결과 처리

```
ReturnValue 0     → 성공
ReturnValue 4096  → CreatedStorageJob 참조로 job 대기(아래)
그 밖             → McpException("Failed to {action} virtual disk: {message} (code {n})")
```

- `{message}`는 `ExtendedStatus` 임베디드 인스턴스의 `Message`이다. 없으면 `"error"`로 쓴다.
- `{action}`: `create`, `resize`, `rename`, `set usage of`, `set attributes of`, `remove`

### job 대기 (사용자 결정: Tool 인자 `timeoutSeconds`, 기본 300)

```
deadline = now + timeoutSeconds
loop:
  job = session.GetInstance(ns, jobRef)
  JobState 7(Completed)            → 성공
  JobState 8, 9, 10 (Terminated, Killed, Exception)
                                   → McpException("Storage job '{Name}' failed: {ErrorDescription}")
  now > deadline                   → McpException("Timed out after {n}s waiting for storage job '{Name}' ({PercentComplete}%); the job continues")
  cancellationToken.WaitHandle.WaitOne(1s)
  cancellationToken.ThrowIfCancellationRequested()   → OperationCanceledException
```

- 시간이 초과되거나 요청이 취소돼도 job은 취소하지 않는다(사용자 결정). 취소는 대기만 멈춘다. 취소 예외는 `McpException`으로 감싸지 않고 SDK에 그대로 넘긴다.
- 대기는 1초 간격 폴링이다. 취소 신호가 오면 `WaitHandle`로 즉시 깨어난다.

### 반환값

- create: 동기로 끝나면(ReturnValue 0) `CreatedVirtualDisk` 출력 인스턴스를 `ToVirtualDiskInfo`로 변환한다. 이 출력 인자는 로컬 `Get-CimClass`에서 확인했다. job으로 끝나면 job 출력의 구조를 추정하지 않는다. 같은 세션에서 대상 풀의 `MSFT_StoragePoolToVirtualDisk` 연결을 다시 열거하고, `SelectOne`으로 `friendlyName`과 일치하는 디스크를 찾는다. 같은 이름이 여러 개면 기존 `Multiple virtual disks match friendlyName ...` 오류가 난다. 이때 디스크는 이미 만들어진 상태이며 메시지로 알 수 있다.
- resize, set: 작업 전에 읽은 `UniqueId`로 다시 조회한다(`QueryVirtualDisks` + `SelectOne`). 이름이 바뀌어도 찾을 수 있다.
- remove: 삭제 전에 읽은 `FriendlyName`, `UniqueId`를 반환한다.
- `CimException`, `NullReferenceException`, `InvalidCastException`은 기존 Tool처럼 `McpException`으로 감싼다. 메시지는 `Failed to {action} virtual disk: ...`이다.

## 입력 검증

순서는 OS 검사 → 입력 검증 → CIM 호출이다(기존 Tool과 같음). 아래 검증은 모두 CIM 호출 전에 한다.

| 조건 | 메시지 |
| --- | --- |
| 비Windows | 기존 `EnsureWindows` 문구 |
| create: 풀 식별자가 정확히 하나가 아님 | `Specify exactly one of storagePoolFriendlyName or storagePoolUniqueId` |
| create: `friendlyName` 공백 | `friendlyName is required` |
| create: `size`와 `useMaximumSize`가 둘 다 있거나 둘 다 없음 | `Specify exactly one of size or useMaximumSize` |
| create: `numberOfColumns`와 `autoNumberOfColumns`를 함께 지정 | `Specify only one of numberOfColumns or autoNumberOfColumns` |
| create: `writeCacheSize`와 `autoWriteCacheSize`를 함께 지정 | `Specify only one of writeCacheSize or autoWriteCacheSize` |
| create, set: `otherUsageDescription`만 있고 `usage`가 없음 | `otherUsageDescription requires usage` |
| resize, set, remove: 디스크 식별자가 정확히 하나가 아님 | 기존 `EnsureExactlyOne` 문구 |
| resize: `size` = 0 | `size must be greater than 0` |
| set: 변경 인자가 하나도 없음 | `Specify at least one property to change` |
| set: `newFriendlyName` 공백 문자열 | `newFriendlyName must not be blank` |
| remove: `confirm`이 false | `Set confirm to true to remove the virtual disk; this permanently deletes its data` |
| create, resize, remove: `timeoutSeconds` ≤ 0 | `timeoutSeconds must be greater than 0` |

- 0이 아닌 크기를 줄이려는 resize나 잘못된 복원력 이름 같은 값 검증은 CIM에 맡기고, 오류는 결과 처리 규칙으로 전달한다.

## 테스트

- 입력 검증: 위 표의 각 조건에서 `McpException`이 나는지 확인한다. 모든 테스트는 CIM 호출 전에 실패하므로 로컬 디스크를 바꾸지 않는다. Windows 조건에서 실행한다.
- 비Windows: 기존처럼 `[OSCondition(ConditionMode.Exclude, OperatingSystems.Windows)]`로 4개 Tool의 `McpException`을 확인한다. 이 PC에서는 건너뛴다(미검증).
- 등록: `WithBizadmToolKit()`에 Tool 4개가 포함되는지, `remove_virtual_disk`가 Destructive이고 나머지 3개는 아닌지 확인한다.
- 스키마: `create_virtual_disk` 입력 스키마에 `"StorageRack"`이 있고 `cancellationToken`이 없는지 확인한다.
- 실제 E2E(생성 → resize → set → remove)는 자동 테스트에 넣지 않는다. 사용자가 테스트 풀을 지정하고 승인하면 MCP 서버로 수동 검증한다.

## 의존성

- 추가 없음

## 결정 목록

1. Tool 4개: `create_virtual_disk`, `resize_virtual_disk`, `set_virtual_disk`(단일 Tool, 선택 인자), `remove_virtual_disk`
2. 생성 인자는 New-VirtualDisk 스칼라 파라미터만 받는다. CIM 전용 인자는 제외한다.
3. 동기 호출이 4096을 반환하면 job 완료까지 폴링한다(`timeoutSeconds` 기본 300, `CancellationToken`으로 대기 중단). 시간 초과나 취소로 job을 취소하지는 않는다. 생성이 job으로 끝나면 풀에서 friendlyName으로 다시 조회한다.
4. set 호출 순서는 SetUsage → SetAttributes → SetFriendlyName이고, 실패해도 되돌리지 않는다.
5. 삭제는 `confirm=true`일 때만 하고, Destructive=true로 표시한다.
6. 반환: create/resize/set은 `VirtualDiskInfo`, remove는 `VirtualDiskRemoveResult`
7. 신규 enum은 `VirtualDiskFaultDomainType` 하나이고 나머지 enum은 재사용한다.
8. 입력 검증은 모두 CIM 호출 전에 하며, 기존 메시지와 헬퍼를 유지한다.
