---
status: done
category: storage
feature: storage-disk-format
phase: 개발
---

# storage-disk-format 개발

선행: `0011-storage-disk-format-설계.md`

단위마다 구현한 뒤 검증하고, 통과해야 다음 단위로 넘어간다. 자동 테스트는 CIM 메서드 호출 전에 실패하는 경로만 다룬다. 로컬 디스크는 바꾸지 않는다.

## U1. 모델·enum

- **대상**: `Models/Storage/DiskInfo.cs`, `PartitionInfo.cs`, `VolumeInfo.cs`, `DiskPartitionStyle.cs`, `PartitionGptType.cs`, `PartitionMbrType.cs`, `VolumeFileSystem.cs` (신규)
- **코드 초안**:

```csharp
[JsonConverter(typeof(JsonStringEnumConverter<DiskPartitionStyle>))]
public enum DiskPartitionStyle : ushort { Raw = 0, MBR = 1, GPT = 2 }

[JsonConverter(typeof(JsonStringEnumConverter<PartitionGptType>))]
public enum PartitionGptType { SystemPartition, MicrosoftReserved, BasicData, MicrosoftRecovery }

[JsonConverter(typeof(JsonStringEnumConverter<VolumeFileSystem>))]
public enum VolumeFileSystem { FAT, FAT32, exFAT, NTFS, ReFS }

public sealed record PartitionInfo(
    uint DiskNumber, uint PartitionNumber,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? DriveLetter,
    IReadOnlyList<string> AccessPaths, ulong Offset, ulong Size,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? GptType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] ushort? MbrType,
    bool IsActive, bool IsHidden, bool IsReadOnly, bool IsOffline);
// ... DiskInfo, VolumeInfo, PartitionMbrType은 설계 표 그대로
```

- 멤버마다 기존 Storage enum처럼 한국어 `/// <summary>`를 붙인다.
- **검증**: `dotnet build` 종료 코드 0
- **선행 단위**: 없음

## U2. 변환·대상 선택 헬퍼

- **대상**: `StorageTools`의 private `ToDiskInfo`, `ToPartitionInfo`, `ToVolumeInfo`, `ToDriveLetter`, `GptTypeGuid`, `WithVirtualDiskDisk`, `FindPartition`, `FindVolume`
- **코드 초안**:

```csharp
private static string? ToDriveLetter(object? value) =>
    value is char c && c != '\0' ? c.ToString() : null;

private static string GptTypeGuid(PartitionGptType type) => type switch
{
    PartitionGptType.SystemPartition => "{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}",
    PartitionGptType.MicrosoftReserved => "{e3c9e316-0b5c-4db8-817d-f92df00215ae}",
    PartitionGptType.BasicData => "{ebd0a0a2-b9e5-4433-87c0-68b6b72699c7}",
    PartitionGptType.MicrosoftRecovery => "{de94bba4-06d1-4d40-a16a-bfd50179d6ac}",
    _ => throw new McpException($"Unsupported gptType: {type}"),
};

private static T WithVirtualDiskDisk<T>(string? friendlyName, string? uniqueId, string action,
    Func<CimSession, CimInstance, T> operation) =>
    WithVirtualDisk(friendlyName, uniqueId, action, (session, vd) =>
    {
        List<CimInstance> disks = session.EnumerateAssociatedInstances(StorageNamespace, vd,
            "MSFT_VirtualDiskToDisk", "MSFT_Disk", "VirtualDisk", "Disk").ToList();
        try
        {
            if (disks.Count == 0)
                throw new McpException($"Virtual disk '{vd.CimInstanceProperties["FriendlyName"].Value}' has no disk; it may be detached");
            return operation(session, disks[0]);
        }
        finally
        {
            foreach (CimInstance disk in disks)
                disk.Dispose();
        }
    });

private static DiskInfo ToDiskInfo(CimKeyedCollection<CimProperty> p) => new(
    (uint)p["Number"].Value, (string)p["FriendlyName"].Value, (string)p["UniqueId"].Value,
    (DiskPartitionStyle)(ushort)p["PartitionStyle"].Value,
    // ... 설계 모델 순서대로
    (StorageHealthStatus)(ushort)p["HealthStatus"].Value);
```

- `FindPartition(session, disk, partitionNumber)`: `MSFT_DiskToPartition` 연결(`"Disk"`, `"Partition"` 역할)로 열거해 `PartitionNumber`가 일치하는 인스턴스를 반환한다. 나머지는 dispose한다. 없으면 `Partition {n} not found on disk {d}`
- `FindVolume(session, partition)`: `MSFT_PartitionToVolume` 연결(`"Partition"`, `"Volume"`)로 첫 볼륨을 반환한다. 없으면 `Partition {n} on disk {d} has no volume`
- `AccessPaths`가 null이면 빈 목록으로 둔다.
- **검증**: `dotnet build` 종료 코드 0, 기존 테스트 전부 통과
- **선행 단위**: U1

## U3. initialize_disk

- **대상**: `StorageTools.InitializeDisk`, 테스트
- **코드 초안**:

```csharp
EnsureWindows();
EnsureExactlyOne(friendlyName, uniqueId);
if (!confirm)
    throw new McpException("Set confirm to true to initialize the disk; a RAW disk may still contain data that will be overwritten");
if (partitionStyle == DiskPartitionStyle.Raw)
    throw new McpException("partitionStyle must be MBR or GPT");
return WithVirtualDiskDisk(friendlyName, uniqueId, "initialize the disk of", (session, disk) =>
{
    var style = (DiskPartitionStyle)(ushort)disk.CimInstanceProperties["PartitionStyle"].Value;
    uint number = (uint)disk.CimInstanceProperties["Number"].Value;
    if (style != DiskPartitionStyle.Raw)
        throw new McpException($"Disk {number} is already initialized ({style}); only RAW disks can be initialized");
    using CimMethodParametersCollection args = new();
    args.Add(InParam("PartitionStyle", (ushort)partitionStyle, CimType.UInt16));
    Invoke(session, disk, "Initialize", args, "initialize the disk of", 0, CancellationToken.None, out _);
    using CimInstance refreshed = session.GetInstance(StorageNamespace, disk);
    return ToDiskInfo(refreshed.CimInstanceProperties);
});
```

- `[McpServerTool(..., Destructive = true, ...)]`로 표시하고 `bool confirm = false` 인자를 마지막에 둔다(설계 회귀).
- **테스트**: 식별자 오류(DataRow 3개), `confirm` 누락, `partitionStyle = Raw`, GUID 이름 → `Virtual disk not found`, 비Windows. 기존 테스트 호출에는 `confirm: true`를 넣는다.
- **검증**: `dotnet test --filter InitializeDisk` 통과
- **선행 단위**: U2

## U4. create_partition

- **대상**: `StorageTools.CreatePartition`, 테스트
- **코드 초안**:

```csharp
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
    // RAW 디스크면 "Disk {d} is not initialized; run initialize_disk first"
    using CimMethodParametersCollection args = new();
    if (size is { } s) args.Add(InParam("Size", s, CimType.UInt64));
    if (useMaximumSize) args.Add(InParam("UseMaximumSize", true, CimType.Boolean));
    if (driveLetter is not null)
        args.Add(InParam("DriveLetter", char.ToUpperInvariant(driveLetter[0]), CimType.Char16));
    else
        args.Add(InParam("AssignDriveLetter", assignDriveLetter ?? true, CimType.Boolean));
    if (gptType is { } g) args.Add(InParam("GptType", GptTypeGuid(g), CimType.String));
    // ... offset(UInt64), alignment(UInt32), mbrType(UInt16), isHidden, isActive(Boolean)
    CimMethodResult result = Invoke(session, disk, "CreatePartition", args, "create a partition on",
        0, CancellationToken.None, out _);
    using CimInstance created = (CimInstance)result.OutParameters["CreatedPartition"].Value;
    using CimInstance refreshed = session.GetInstance(StorageNamespace, created);
    return ToPartitionInfo(refreshed.CimInstanceProperties);
});
```

- **테스트**: 식별자 오류, size/useMaximumSize 둘 다 또는 둘 다 없음, `driveLetter` `"AB"`·`"1"`·`""`, `driveLetter`와 `assignDriveLetter=false`, gpt/mbr 동시 지정, GUID 이름 → not found, 비Windows
- **검증**: `dotnet test --filter CreatePartition` 통과
- **선행 단위**: U2

## U5. format_volume

- **대상**: `StorageTools.FormatVolume`, 테스트
- **코드 초안**:

```csharp
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
    string? fs = volume.CimInstanceProperties["FileSystem"].Value as string;
    if (!string.IsNullOrEmpty(fs) && !string.Equals(fs, "RAW", StringComparison.OrdinalIgnoreCase))
        throw new McpException($"Volume on partition {partitionNumber} is already formatted ({fs}); only unformatted volumes can be formatted");
    using CimMethodParametersCollection args = new();
    if (fileSystem is { } f) args.Add(InParam("FileSystem", f.ToString(), CimType.String));
    if (newFileSystemLabel is not null) args.Add(InParam("FileSystemLabel", newFileSystemLabel, CimType.String));
    if (allocationUnitSize is { } a) args.Add(InParam("AllocationUnitSize", a, CimType.UInt32));
    // ... full, compress, shortFileNameSupport, setIntegrityStreams, useLargeFRS,
    //     disableHeatGathering, isDAX, noTrim, devDrive (Boolean, 값 있을 때만)
    Invoke(session, volume, "Format", args, "format a volume on", timeoutSeconds, cancellationToken, out _);
    using CimInstance refreshed = session.GetInstance(StorageNamespace, volume);
    return ToVolumeInfo(refreshed.CimInstanceProperties);
});
```

- `[McpServerTool(..., Destructive = true, ...)]`로 표시하고 `bool confirm = false` 인자를 `timeoutSeconds` 앞에 둔다(설계 회귀).
- **테스트**: 식별자 오류, `confirm` 누락, `partitionNumber = 0`, `timeoutSeconds = 0`, GUID 이름 → not found, 비Windows. 기존 테스트 호출에는 `confirm: true`를 넣는다.
- **검증**: `dotnet test --filter FormatVolume` 통과
- **선행 단위**: U2

## U6. 등록·어노테이션·스키마

- **대상**: `StorageToolsTests`
- **코드 초안**:

```csharp
[DataRow("initialize_disk", true)]
[DataRow("create_partition", false)]
[DataRow("format_volume", true)]
public void DiskFormatTools_Annotations(string name, bool destructive)
{
    // ... 기존 VirtualDiskWriteTools_Annotations와 같은 준비
    Assert.AreEqual(destructive, tool.ProtocolTool.Annotations!.DestructiveHint);
    Assert.AreEqual(false, tool.ProtocolTool.Annotations.ReadOnlyHint);
}

string formatInput = Tool("format_volume").ProtocolTool.InputSchema.GetRawText();
StringAssert.Contains(formatInput, "\"ReFS\"");
Assert.IsFalse(formatInput.Contains("cancellationToken", StringComparison.OrdinalIgnoreCase));
StringAssert.Contains(Tool("create_partition").ProtocolTool.InputSchema.GetRawText(), "\"BasicData\"");

JsonElement partitionSchema = Tool("create_partition").ProtocolTool.OutputSchema!.Value;
// required 배열에 "driveLetter" 포함

string json = JsonSerializer.Serialize(new PartitionInfo(16, 2, null, [], 0, 0, null, null,
    false, false, false, false), McpJsonUtilities.DefaultOptions);
// "driveLetter":null, "gptType":null, "mbrType":null 포함
```

- 등록 테스트 `WithBizadmToolKit_RegistersStorageTools`에 Tool 3개를 추가한다.
- 직렬화 옵션과 required 확인은 기존 `ParityLayout`·`StoragePoolInfo` null 테스트와 같은 방식으로 한다.
- **검증**: 전체 `dotnet test` 통과
- **선행 단위**: U3–U5

## U7. 수동 E2E (선택)

- **대상**: 빌드한 MCP 서버, 기존 `Test` 가상 디스크(RAW, 1.50GB)
- 사용자가 승인할 때만 실행한다.
- 순서:
  1. `initialize_disk(friendlyName: "Test")`를 `confirm` 없이 호출 → 거부되는지 확인. `confirm: true`로 다시 호출 → `PartitionStyle` GPT
  2. `create_partition(useMaximumSize: true)` → 드라이브 문자가 자동 할당됨
  3. `format_volume(partitionNumber: 2번째 결과의 번호, fileSystem: NTFS, confirm: true)` → `FileSystem` NTFS
  4. 제어판에서 "포맷되지 않았습니다"가 사라졌는지 확인한다.
  5. 같은 `initialize_disk`/`format_volume` 재호출이 RAW 검사로 거부되는지 확인한다.
- **검증**: 단계마다 반환값과 거부 메시지를 확인한다. 승인이 없으면 미검증으로 보고한다.
- **선행 단위**: U6

## 순서

U1 → U2 → U3 → U4 → U5 → U6 → U7
