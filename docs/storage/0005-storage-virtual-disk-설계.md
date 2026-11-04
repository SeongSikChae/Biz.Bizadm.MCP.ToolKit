---
status: done
category: storage
feature: storage-virtual-disk
phase: 설계
---

# storage-virtual-disk 설계

선행: `0004-storage-virtual-disk-방안.md`
참고: [Get-VirtualDisk](https://learn.microsoft.com/en-us/powershell/module/storage/get-virtualdisk?view=windowsserver2025-ps), [MSFT_VirtualDisk](https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-virtualdisk), 연결 클래스 `MSFT_StoragePoolToVirtualDisk` (namespace `root/Microsoft/Windows/Storage`)

## 구조

```
src/Biz.Bizadm.MCP.ToolKit/
  Tools/
    StorageTools.cs                              # list_virtual_disks, get_virtual_disk 추가, 단건 선택 헬퍼 추출
  Models/
    Storage/
      VirtualDiskInfo.cs                         # 가상 디스크 1개 정보 record (신규)
      VirtualDiskListResult.cs                   # 목록 결과 record (신규)
      VirtualDiskOperationalStatus.cs            # enum (신규)
      VirtualDiskUsage.cs                        # enum (신규)
      VirtualDiskAccess.cs                       # enum (신규)
      VirtualDiskDetachedReason.cs               # enum (신규)
      VirtualDiskMediaType.cs                    # enum (신규)
      VirtualDiskParityLayout.cs                 # enum (신규)
tests/Biz.Bizadm.MCP.ToolKit.Tests/
  StorageToolsTests.cs                           # 테스트 추가
```

- 이름: 메서드 `ListVirtualDisks`, `GetVirtualDisk`.
- `BizadmToolKitMcpServerBuilderExtensions`·패키지 참조는 바꾸지 않는다(`StorageTools` 이미 등록, `Microsoft.Management.Infrastructure` 이미 참조).

## 인터페이스

```csharp
// Tools/StorageTools.cs
[McpServerTool(Name = "list_virtual_disks", UseStructuredContent = true,
    ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
[Description("로컬 Windows 저장소 풀 1개에 속한 저장소 공간(가상 디스크) 목록을 조회한다(Get-VirtualDisk -StoragePool). 풀은 friendlyName 또는 uniqueId 중 하나로 지정한다. 풀이 없거나 이름이 여러 풀과 일치하면 오류를 반환한다. Windows가 아니면 오류를 반환한다.")]
public static VirtualDiskListResult ListVirtualDisks(
    [Description("저장소 풀 이름(FriendlyName), uniqueId와 함께 쓰지 않는다")] string? friendlyName = null,
    [Description("저장소 풀 고유 ID(UniqueId), friendlyName과 함께 쓰지 않는다")] string? uniqueId = null);

[McpServerTool(Name = "get_virtual_disk", UseStructuredContent = true,
    ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
[Description("로컬 Windows의 저장소 공간(가상 디스크) 1개를 friendlyName 또는 uniqueId 중 하나로 조회한다(Get-VirtualDisk). 없거나 이름이 여러 가상 디스크와 일치하면 오류를 반환한다. Windows가 아니면 오류를 반환한다.")]
public static VirtualDiskInfo GetVirtualDisk(
    [Description("가상 디스크 이름(FriendlyName), uniqueId와 함께 쓰지 않는다")] string? friendlyName = null,
    [Description("가상 디스크 고유 ID(UniqueId), friendlyName과 함께 쓰지 않는다")] string? uniqueId = null);
```

```csharp
// Models/Storage/VirtualDiskListResult.cs
public sealed record VirtualDiskListResult(IReadOnlyList<VirtualDiskInfo> VirtualDisks);

// Models/Storage/VirtualDiskInfo.cs
public sealed record VirtualDiskInfo(
    string FriendlyName,
    string UniqueId,
    string ObjectId,
    IReadOnlyList<VirtualDiskOperationalStatus> OperationalStatus,
    StorageHealthStatus HealthStatus,
    string ResiliencySettingName,
    StorageProvisioningType ProvisioningType,
    VirtualDiskUsage Usage,
    ulong Size,
    ulong AllocatedSize,
    ulong FootprintOnPool,
    ushort NumberOfDataCopies,
    ushort PhysicalDiskRedundancy,
    ushort NumberOfColumns,
    ulong Interleave,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] VirtualDiskParityLayout? ParityLayout,
    ulong LogicalSectorSize,
    ulong PhysicalSectorSize,
    VirtualDiskMediaType MediaType,
    VirtualDiskAccess Access,
    VirtualDiskDetachedReason DetachedReason,
    bool IsTiered,
    bool IsSnapshot,
    bool IsManualAttach,
    bool IsDeduplicationEnabled,
    bool IsEnclosureAware,
    ulong WriteCacheSize);
```

- 목록은 구조화 출력 루트가 객체여야 하므로 래퍼 record(`VirtualDisks`)로 반환한다(풀과 같음).
- 필드 27개. 로컬 9개 인스턴스에서 항상 null인 `Name`·`NameFormat`·`NumberOfAvailableCopies`·`PassThrough*`·`Other*Description`·`UniqueIdFormatDescription`과 `UniqueIdFormat`·`ColumnIsolation`·`FaultDomainAwareness`·캐시/QoS 세부값(`ReadCacheSize`·`WriteCacheReserveSize`·`MaxIops`·`MaxIoBandwidth`·`AllocationUnitSize`·`MinimumLogicalDataCopies`·`NumberOfGroups`·`TrackValidData`·`RequestNoSinglePointOfFailure`)은 제외한다.
- `ParityLayout`만 nullable이다. 문서상 패리티가 아니면 NULL이며 로컬 9개 모두 null이다. 출력 스키마가 모든 속성을 required로 두므로 `JsonIgnoreCondition.Never`로 null을 직렬화한다(`StoragePoolInfo` 패턴).
- 그 밖의 값이 null이거나 형식이 다르면 변환 예외를 `McpException`으로 감싼다(풀과 같음).

## enum

모두 `ushort` 기반, `[JsonConverter(typeof(JsonStringEnumConverter<T>))]`로 이름 직렬화(기존 Storage enum과 같음). 값은 로컬 `MSFT_VirtualDisk` ValueMap(amended qualifier)과 MS 문서를 따른다. 정의 밖 코드는 숫자로 직렬화된다. 상태·프로비저닝은 기존 `StorageHealthStatus`·`StorageProvisioningType`을 재사용한다(값 동일).

| enum | 값 |
| --- | --- |
| `VirtualDiskOperationalStatus` | 0 Unknown, 1 Other, 2 OK, 3 Degraded, 4 Stressed, 5 PredictiveFailure, 6 Error, 7 NonRecoverableError, 8 Starting, 9 Stopping, 10 Stopped, 11 InService, 12 NoContact, 13 LostCommunication, 14 Aborted, 15 Dormant, 16 SupportingEntityInError, 17 Completed, 18 PowerMode, 19 Relocating, 0xD002 Detached, 0xD003 Incomplete, 0xD01B Suboptimal, 0xD024 NoRedundancy |
| `VirtualDiskUsage` | 0 Unknown, 1 Other, 2 Unrestricted, 3 ReservedForComputerSystem, 4 ReservedByReplicationServices, 5 ReservedByMigrationServices, 6 LocalReplicaSource, 7 RemoteReplicaSource, 8 LocalReplicaTarget, 9 RemoteReplicaTarget, 10 LocalReplicaSourceOrTarget, 11 RemoteReplicaSourceOrTarget, 12 DeltaReplicaTarget, 13 ElementComponent, 14 ReservedAsPoolContributor, 15 CompositeVolumeMember, 16 CompositeVirtualDiskMember, 17 ReservedForSparing, 18 Configuration, 19 Data, 20 Journal, 21 ReadCache, 22 CacheLog, 23 DirtyRegionTracking, 24 StripeStateTracking, 25 Snapshot, 26 Simple, 27 ValidDataTracking, 28 CacheLines |
| `VirtualDiskAccess` | 0 Unknown, 1 Readable, 2 Writeable, 3 ReadWrite, 4 WriteOnce |
| `VirtualDiskDetachedReason` | 0 Unknown, 1 None, 2 ByPolicy, 3 MajorityDisksUnhealthy, 4 Incomplete, 5 Timeout |
| `VirtualDiskMediaType` | 0 Unspecified, 3 HDD, 4 SSD, 5 SCM |
| `VirtualDiskParityLayout` | 1 NonRotatedParity, 2 RotatedParity |

- `OperationalStatus`는 풀과 코드가 달라(풀 0xD000 ReadOnly·0xD001 Incomplete) 전용 enum을 둔다. 기존 `StorageOperationalStatus`와 풀 출력 스키마는 바꾸지 않는다(사용자 결정).
- `Usage`도 풀(`StoragePoolUsage`)과 값 의미가 달라 전용 enum을 둔다.

## 조회

```
OperatingSystem.IsWindows() 아니면 McpException
using CimSession session = CimSession.Create(null);   // 로컬

list_virtual_disks:
  session.QueryInstances(ns, "WQL", "SELECT * FROM MSFT_StoragePool")
    → CimInstance 목록에서 FriendlyName·UniqueId 값만 읽어 단건 선택 헬퍼로 풀 1개 선택
  session.EnumerateAssociatedInstances(ns, pool,
      "MSFT_StoragePoolToVirtualDisk", "MSFT_VirtualDisk", "StoragePool", "VirtualDisk")
    → 각 인스턴스를 VirtualDiskInfo로 변환

get_virtual_disk:
  session.QueryInstances(ns, "WQL", "SELECT * FROM MSFT_VirtualDisk")
    → VirtualDiskInfo 목록에서 단건 선택 헬퍼로 1개 선택
```

- 연결 열거에 쓸 풀 인스턴스는 같은 세션에서 조회한 것을 그대로 쓴다. 풀 선택에는 `StoragePoolInfo` 전체 변환 없이 `FriendlyName`·`UniqueId`만 읽는다(구현 후 검토 반영). 조회한 풀 인스턴스는 열거 후 모두 dispose한다.
- Primordial 풀은 연결이 없어 빈 목록이다.
- `CimException`·`NullReferenceException`·`InvalidCastException`은 `McpException`으로 감싼다. 메시지는 `Failed to query virtual disks: ...`.
- 결과 순서는 CIM 반환 순서 그대로 둔다.

### 단건 선택 헬퍼 (사용자 결정: 공용 추출)

`GetStoragePool`의 입력 검증·필터·오류를 private 제네릭 헬퍼로 빼서 `get_storage_pool`, `list_virtual_disks`(풀 선택), `get_virtual_disk`가 공유한다.

```csharp
private static T SelectOne<T>(IEnumerable<T> items, Func<T, string> name, Func<T, string> id,
    string? friendlyName, string? uniqueId, string kind, string plural);
```

- `kind`/`plural`: `"Storage pool"`/`"storage pools"`, `"Virtual disk"`/`"virtual disks"`.
- 입력 검증은 CIM 조회 전에 한다(`GetStoragePool`의 현재 순서 유지: OS 검사 → 입력 검증 → 조회).
- `get_storage_pool`의 오류 메시지는 바뀌지 않는다.

## 입력 검증

| 조건 | `list_virtual_disks` | `get_virtual_disk` |
| --- | --- | --- |
| 비Windows | `McpException("Storage pools are only supported on Windows")` (기존 `EnsureWindows` 재사용) | 같음 |
| `friendlyName`·`uniqueId` 둘 다 비어 있음 또는 둘 다 값 있음 | `McpException("Specify exactly one of friendlyName or uniqueId")` | 같음 |
| 일치 0개 | `McpException("Storage pool not found: ...")` | `McpException("Virtual disk not found: ...")` |
| `friendlyName` 일치 2개 이상 | `McpException("Multiple storage pools match friendlyName '...'; use uniqueId")` | `McpException("Multiple virtual disks match friendlyName '...'; use uniqueId")` |

- 일치는 대소문자 무시(`StringComparison.OrdinalIgnoreCase`), 풀과 같다.
- 비Windows 메시지는 기존 헬퍼 문구를 그대로 쓴다(기존 동작 유지, 문구 일반화는 비범위).

## 테스트

- Windows(`[OSCondition(OperatingSystems.Windows)]`):
  - 비-Primordial 풀마다 `list_virtual_disks(uniqueId)` 결과의 `UniqueId` 합집합이 `get_virtual_disk` 전체 대상(`MSFT_VirtualDisk`)과 일치. 이 PC는 가상 디스크 9개.
  - Primordial 풀 → 빈 목록.
  - `get_virtual_disk(uniqueId 소문자)`·`get_virtual_disk(friendlyName 대문자, 유일 이름)` 결과가 목록 항목과 같음.
  - 입력 둘 다/둘 다 없음/공백 → `McpException`(두 Tool).
  - 없는 풀·없는 가상 디스크 → `McpException`.
  - 기존 `get_storage_pool` 테스트가 그대로 통과(헬퍼 추출 회귀).
- 비Windows: `[OSCondition(ConditionMode.Exclude, OperatingSystems.Windows)]`로 두 Tool이 `McpException`. 이 PC에서는 건너뛴다(미검증).
- 공통: `WithBizadmToolKit()` 등록에 `list_virtual_disks`·`get_virtual_disk` 포함, `get_virtual_disk` 출력 스키마에 `"Detached"`·`"ReadWrite"` 문자열 포함, `ParityLayout = null` 직렬화 시 required 속성 모두 존재·`parityLayout`이 JSON null.

## 의존성

- 추가 없음(`Microsoft.Management.Infrastructure` 3.0.0 기존 참조).

## 결정 목록

1. `StorageTools`에 `ListVirtualDisks`(`list_virtual_disks`), `GetVirtualDisk`(`get_virtual_disk`)
2. `list_virtual_disks`는 풀을 `friendlyName`/`uniqueId` 중 하나로 지정, `get_virtual_disk`는 로컬 전체 대상
3. 목록 결과는 래퍼 `VirtualDiskListResult(VirtualDisks)`
4. `VirtualDiskInfo` 필드 27개, `ParityLayout`만 nullable
5. 운영 상태·용도는 전용 enum, 상태·프로비저닝은 기존 enum 재사용, 그 외 신규 enum 4개
6. 풀 → `MSFT_StoragePoolToVirtualDisk` 연결 열거, 단건은 전체 조회 후 메모리 필터
7. 단건 선택 로직을 private 제네릭 헬퍼 `SelectOne<T>`로 추출, `get_storage_pool` 메시지 유지
8. 비Windows·입력 오류·미존재·중복 이름·CIM 오류 → `McpException`
