---
status: done
category: storage
feature: storage-disk-format
phase: 설계
---

# storage-disk-format 설계

선행: `0010-storage-disk-format-방안.md`
참고: Initialize-Disk, New-Partition, Format-Volume (windowsserver2025-ps). 로컬 `Get-CimClass`로 `MSFT_Disk`, `MSFT_Partition`, `MSFT_Volume`의 속성·메서드와 연결 클래스 `MSFT_VirtualDiskToDisk`, `MSFT_DiskToPartition`, `MSFT_PartitionToVolume`을 확인했다(namespace `root/Microsoft/Windows/Storage`). `create_virtual_disk`로 만든 `Test` 디스크에서는 Number 16, PartitionStyle 0(RAW)을 확인했다.

## 구조

```
src/Biz.Bizadm.MCP.ToolKit/
  Tools/
    StorageTools.cs                 # Tool 3개, 가상 디스크 → 디스크 선택 헬퍼 추가
  Models/
    Storage/
      DiskInfo.cs                   # 디스크 정보 record (신규)
      DiskPartitionStyle.cs         # enum (신규)
      PartitionInfo.cs              # 파티션 정보 record (신규)
      PartitionGptType.cs           # 입력 enum (신규)
      PartitionMbrType.cs           # 입력 enum (신규)
      VolumeInfo.cs                 # 볼륨 정보 record (신규)
      VolumeFileSystem.cs           # 입력 enum (신규)
tests/Biz.Bizadm.MCP.ToolKit.Tests/
  StorageToolsTests.cs              # 입력 검증·등록·스키마 테스트 추가
```

- 메서드 이름은 `InitializeDisk`, `CreatePartition`, `FormatVolume`이다.

## 인터페이스

세 Tool 모두 `ReadOnly = false`, `Idempotent = false`, `OpenWorld = false`이다. `initialize_disk`와 `format_volume`은 `Destructive = true`이고 `confirm=true`가 필요하다(검토 회귀, 사용자 결정). RAW로 보고되는 디스크·볼륨에도 데이터가 있을 수 있기 때문이다. 예: 잠긴 BitLocker 볼륨, Windows가 모르는 파일 시스템, 손상된 볼륨, 파티션 테이블 없이 쓰는 디스크. `create_partition`은 빈 공간에만 쓰므로 `Destructive = false`이고 `confirm`이 없다.

```csharp
[McpServerTool(Name = "initialize_disk", UseStructuredContent = true, ...)]
public static DiskInfo InitializeDisk(
    string? friendlyName = null, string? uniqueId = null,          // 가상 디스크
    DiskPartitionStyle partitionStyle = DiskPartitionStyle.GPT,    // Raw는 거부
    bool confirm = false);

[McpServerTool(Name = "create_partition", UseStructuredContent = true, ...)]
public static PartitionInfo CreatePartition(
    string? friendlyName = null, string? uniqueId = null,
    ulong? size = null, bool useMaximumSize = false,               // 정확히 하나
    ulong? offset = null, uint? alignment = null,
    string? driveLetter = null,                                    // A–Z 한 글자
    bool? assignDriveLetter = null,                                // null이면 자동 할당(true)
    PartitionGptType? gptType = null, PartitionMbrType? mbrType = null,
    bool? isHidden = null, bool? isActive = null);

[McpServerTool(Name = "format_volume", UseStructuredContent = true, ...)]
public static VolumeInfo FormatVolume(
    uint partitionNumber,                                          // 필수(사용자 결정)
    string? friendlyName = null, string? uniqueId = null,
    VolumeFileSystem? fileSystem = null,                           // null이면 CIM 기본값
    string? newFileSystemLabel = null, uint? allocationUnitSize = null,
    bool? full = null, bool? compress = null, bool? shortFileNameSupport = null,
    bool? setIntegrityStreams = null, bool? useLargeFRS = null,
    bool? disableHeatGathering = null, bool? isDAX = null, bool? noTrim = null,
    bool? devDrive = null,
    bool confirm = false,
    int timeoutSeconds = 300, CancellationToken cancellationToken = default);
```

- `confirm`이 필요한 두 Tool의 설명에는 "RAW로 보이는 대상에도 데이터가 있을 수 있으며 덮어쓴다"와 "`confirm=true`가 필요하다"를 적는다.

- 인자 이름은 cmdlet 이름을 따른다(`newFileSystemLabel` 등). 재포맷을 강제하는 `Force`, 그리고 cmdlet에 없는 CIM 전용 `SHA256Checksums`는 받지 않는다.
- `driveLetter`를 지정하면 `DriveLetter`만 넘긴다. 지정하지 않으면 `AssignDriveLetter = assignDriveLetter ?? true`로 넘긴다(사용자 결정: 자동 할당). 이렇게 하면 제어판 포맷과 결과가 같다.

## 모델

```csharp
public sealed record DiskInfo(
    uint Number, string FriendlyName, string UniqueId, DiskPartitionStyle PartitionStyle,
    ulong Size, ulong AllocatedSize, ulong LargestFreeExtent, uint NumberOfPartitions,
    uint LogicalSectorSize, uint PhysicalSectorSize, bool IsOffline, bool IsReadOnly,
    StorageHealthStatus HealthStatus);

public sealed record PartitionInfo(
    uint DiskNumber, uint PartitionNumber,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? DriveLetter,
    IReadOnlyList<string> AccessPaths, ulong Offset, ulong Size,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? GptType,   // GUID 원문
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] ushort? MbrType,   // 숫자 원문
    bool IsActive, bool IsHidden, bool IsReadOnly, bool IsOffline);

public sealed record VolumeInfo(
    string UniqueId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? DriveLetter,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? FileSystem,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? FileSystemLabel,
    uint AllocationUnitSize, ulong Size, ulong SizeRemaining, StorageHealthStatus HealthStatus);
```

- nullable 속성은 `StoragePoolInfo`처럼 `JsonIgnoreCondition.Never`로 null도 직렬화한다. 출력 스키마가 모든 속성을 required로 두기 때문이다.
- `DriveLetter`는 CIM `Char16`이다. 값이 `'\0'`이면 null, 아니면 한 글자 문자열로 바꾼다.
- `HealthStatus`는 기존 `StorageHealthStatus`(0 Healthy, 1 Warning, 2 Unhealthy, 5 Unknown)를 재사용한다.

## enum

모두 `JsonStringEnumConverter`를 적용한다(기존 Storage enum과 같음).

| enum | 기반 | 값 | CIM 전달 |
| --- | --- | --- | --- |
| `DiskPartitionStyle` | ushort | 0 Raw, 1 MBR, 2 GPT | `UInt16` |
| `PartitionGptType` | 없음 | SystemPartition, MicrosoftReserved, BasicData, MicrosoftRecovery | GUID 문자열(아래) |
| `PartitionMbrType` | ushort | 1 FAT12, 4 FAT16, 5 Extended, 6 Huge, 7 IFS, 12 FAT32 | `UInt16` |
| `VolumeFileSystem` | 없음 | FAT, FAT32, exFAT, NTFS, ReFS | 이름 문자열 |

- GPT 유형 GUID 매핑: SystemPartition `{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}`, MicrosoftReserved `{e3c9e316-0b5c-4db8-817d-f92df00215ae}`, BasicData `{ebd0a0a2-b9e5-4433-87c0-68b6b72699c7}`, MicrosoftRecovery `{de94bba4-06d1-4d40-a16a-bfd50179d6ac}`
- CIM ValueMap에 있는 LDM 메타데이터·데이터 GUID 2개는 동적 디스크용이라 제외한다.
- `MbrType` ValueMap은 로컬 `Get-CimClass` 값(1, 4, 5, 6, 7, 12)을 따른다.

## 대상 선택

```
WithVirtualDiskDisk(friendlyName, uniqueId, action, (session, disk) => ...)
  = WithVirtualDisk(...)                                   // 기존 헬퍼, 가상 디스크 1개 선택·예외 감싸기
      → session.EnumerateAssociatedInstances(ns, vd,
            "MSFT_VirtualDiskToDisk", "MSFT_Disk", "VirtualDisk", "Disk")
      → 0개: McpException("Virtual disk '{FriendlyName}' has no disk; it may be detached")
      → 1개: 콜백
```

- `format_volume`은 디스크에서 다음 순서로 대상을 찾는다.
  1. `MSFT_DiskToPartition` 연결에서 `PartitionNumber`가 일치하는 파티션을 찾는다. 없으면 `Partition {n} not found on disk {d}`
  2. `MSFT_PartitionToVolume` 연결에서 볼륨을 찾는다. 없으면 `Partition {n} on disk {d} has no volume`
- 열거한 인스턴스는 모두 dispose한다.

## RAW 검사 (사용자 결정: RAW만 허용)

| Tool | 조건 | 위반 메시지 |
| --- | --- | --- |
| initialize_disk | 디스크 `PartitionStyle == 0` | `Disk {d} is already initialized ({style}); only RAW disks can be initialized` |
| create_partition | 디스크 `PartitionStyle != 0` | `Disk {d} is not initialized; run initialize_disk first` |
| format_volume | 볼륨 `FileSystem`이 비어 있거나 `RAW` | `Volume on partition {n} is already formatted ({fs}); only unformatted volumes can be formatted` |

- create_partition은 기존 파티션이 있어도 빈 공간에 만든다. CIM이 빈 공간이 부족하면 오류를 내고, 그 오류를 결과 처리 규칙으로 전달한다.

## CIM 호출·결과

| Tool | 대상 | 메서드 | 반환 |
| --- | --- | --- | --- |
| initialize_disk | `MSFT_Disk` | `Initialize(PartitionStyle)` | 디스크 `GetInstance`로 새로 읽어 `DiskInfo` |
| create_partition | `MSFT_Disk` | `CreatePartition(...)` | 출력 `CreatedPartition`을 `GetInstance`로 새로 읽어 `PartitionInfo` |
| format_volume | `MSFT_Volume` | `Format(...)` | 볼륨 `GetInstance`로 새로 읽어 `VolumeInfo` |

- 호출과 오류 처리는 기존 `Invoke`를 재사용한다. ReturnValue가 0이면 성공, 4096이면 job을 대기하고, 그 밖에는 `McpException`이다.
- `Initialize`와 `CreatePartition`에는 `CreatedStorageJob` 출력이 없다. 그래서 `timeoutSeconds`를 받지 않고 0을 넘긴다(set_virtual_disk와 같은 방식).
- `format_volume`은 `RunAsJob`을 넘기지 않는다. 4096을 받으면 `timeoutSeconds`·`CancellationToken`으로 대기하고, job으로 끝나도 볼륨을 다시 읽어 반환한다. 출력 `FormattedVolume`의 구조는 추정하지 않는다.
- `Invoke`의 오류 메시지는 `Failed to {action} virtual disk: ...` 형식이다. `{action}`은 `initialize the disk of`, `create a partition on`, `format a volume on`으로 둔다. 기존 메시지 형식은 바꾸지 않는다.
- `DriveLetter`는 `CimType.Char16`, `Alignment`·`AllocationUnitSize`는 `UInt32`, `Size`·`Offset`은 `UInt64`이다.

## 입력 검증

순서는 OS 검사 → 입력 검증 → CIM 호출이다. 아래 검증은 모두 CIM 호출 전에 한다.

| 조건 | 메시지 |
| --- | --- |
| 비Windows | 기존 `EnsureWindows` 문구 |
| 가상 디스크 식별자가 정확히 하나가 아님 | 기존 `EnsureExactlyOne` 문구 |
| initialize: `partitionStyle == Raw` | `partitionStyle must be MBR or GPT` |
| initialize: `confirm`이 false | `Set confirm to true to initialize the disk; a RAW disk may still contain data that will be overwritten` |
| format: `confirm`이 false | `Set confirm to true to format the volume; a RAW volume may still contain data (for example a locked BitLocker volume) that will be lost` |
| create: `size`와 `useMaximumSize`가 둘 다 있거나 둘 다 없음 | `Specify exactly one of size or useMaximumSize` |
| create: `driveLetter`가 A–Z 한 글자가 아님(대소문자 무시) | `driveLetter must be a single letter A-Z` |
| create: `driveLetter`와 `assignDriveLetter=false`를 함께 지정 | `driveLetter cannot be used with assignDriveLetter=false` |
| create: `gptType`과 `mbrType`을 함께 지정 | `Specify only one of gptType or mbrType` |
| format: `partitionNumber == 0` | `partitionNumber must be greater than 0` |
| format: `timeoutSeconds <= 0` | 기존 `EnsureTimeout` 문구 |

- `confirm` 검사는 식별자 검사 바로 다음에 한다(`remove_virtual_disk`와 같은 순서).
- 디스크 스타일과 맞지 않는 옵션은 CIM 오류로 전달한다. 예: GPT 디스크에 `mbrType`이나 `isActive`, ReFS가 아닌 파일 시스템에 `setIntegrityStreams`
- `driveLetter`는 대문자로 바꿔 넘긴다.

## 테스트

- 입력 검증: 위 표의 각 조건에서 `McpException`이 나는지 확인한다. GUID 이름의 가상 디스크로 호출해 CIM 메서드 호출 전에 실패하는지 본다. Windows 조건에서 실행한다.
- 대상 없음: GUID 이름 → `Virtual disk not found` (3개 Tool)
- 비Windows: `[OSCondition(ConditionMode.Exclude, OperatingSystems.Windows)]` (이 PC에서는 건너뜀)
- `confirm` 누락: initialize·format에서 GUID 이름으로 호출해도 `confirm` 오류가 나는지(CIM 호출 전)
- 등록·어노테이션: Tool 3개 등록. `DestructiveHint`는 initialize·format이 true, create_partition이 false이다. `ReadOnlyHint`는 셋 다 false이다.
- 스키마: `format_volume` 입력 스키마에 `"ReFS"`가 있고 `cancellationToken`이 없는지, `create_partition` 입력 스키마에 `"BasicData"`가 있는지, `create_partition` 출력 스키마의 `driveLetter`가 required인지
- 모델: `DriveLetter = null`인 `PartitionInfo`를 직렬화할 때 `driveLetter`가 JSON null로 나오는지
- 실제 E2E는 자동 테스트에 넣지 않는다. 사용자 승인을 받으면 `Test` 가상 디스크로 다음 순서를 확인한다: initialize → create_partition → format_volume → 제어판에서 포맷 상태 확인 → 같은 단계 재호출이 RAW 검사로 거부되는지

## 의존성

- 추가 없음

## 결정 목록

1. Tool 3개(`initialize_disk`, `create_partition`, `format_volume`), 대상은 가상 디스크 식별자
2. 반환은 단계별 결과(`DiskInfo`, `PartitionInfo`, `VolumeInfo`)로 한다(사용자 결정).
3. format의 `partitionNumber`는 필수다(사용자 결정).
4. create의 드라이브 문자는 지정하지 않으면 자동 할당한다(사용자 결정).
5. RAW 검사: 초기화는 RAW 디스크만, 파티션은 초기화된 디스크만, 포맷은 파일 시스템이 없는 볼륨만 허용한다. `Force`는 받지 않는다.
6. initialize·format은 RAW 검사에 더해 `confirm=true`가 필요하고 Destructive=true로 둔다. create_partition은 Destructive=false다(검토 회귀: RAW로 보여도 데이터가 있을 수 있음).
7. format만 job 대기(`timeoutSeconds`, `CancellationToken`)를 지원한다.
8. 신규 enum 4개와 모델 3개를 추가하고, `StorageHealthStatus`, `WithVirtualDisk`, `Invoke`, `SelectByName`을 재사용한다.
