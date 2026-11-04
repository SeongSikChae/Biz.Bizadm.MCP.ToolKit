---
status: done
category: storage
feature: storage-pool
phase: 설계
---

# storage-pool 설계

선행: `0001-storage-pool-방안.md`
참고: [Get-StoragePool](https://learn.microsoft.com/en-us/powershell/module/storage/get-storagepool?view=windowsserver2025-ps), CIM 클래스 `MSFT_StoragePool` (namespace `root/Microsoft/Windows/Storage`)

## 구조

```
Directory.Packages.props                         # PackageVersion Microsoft.Management.Infrastructure 3.0.0
src/Biz.Bizadm.MCP.ToolKit/
  Biz.Bizadm.MCP.ToolKit.csproj                  # PackageReference 추가
  BizadmToolKitMcpServerBuilderExtensions.cs     # builder.WithTools<StorageTools>() 추가
  Tools/
    StorageTools.cs                              # list_storage_pools, get_storage_pool (신규)
  Models/
    Storage/
      StoragePoolInfo.cs                         # 풀 1개 정보 record (신규)
      StoragePoolListResult.cs                   # 목록 결과 record (신규)
      StorageOperationalStatus.cs                # enum (신규)
      StorageHealthStatus.cs                     # enum (신규)
      StorageProvisioningType.cs                 # enum (신규)
      StoragePoolUsage.cs                        # enum (신규)
tests/Biz.Bizadm.MCP.ToolKit.Tests/
  StorageToolsTests.cs                           # 신규
```

- 이름: 클래스 `StorageTools` / 메서드 `ListStoragePools`, `GetStoragePool`.
- 기존 Tool·모델은 바꾸지 않는다.

## 인터페이스

```csharp
// Tools/StorageTools.cs
[McpServerTool(Name = "list_storage_pools", UseStructuredContent = true,
    ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
[Description("로컬 Windows의 저장소 공간(Storage Spaces) 저장소 풀 목록을 조회한다(Get-StoragePool). Windows가 아니면 오류를 반환한다.")]
public static StoragePoolListResult ListStoragePools();

[McpServerTool(Name = "get_storage_pool", UseStructuredContent = true,
    ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
[Description("로컬 Windows의 저장소 풀 1개를 friendlyName 또는 uniqueId 중 하나로 조회한다. 없거나 이름이 여러 풀과 일치하면 오류를 반환한다. Windows가 아니면 오류를 반환한다.")]
public static StoragePoolInfo GetStoragePool(
    [Description("저장소 풀 이름(FriendlyName), uniqueId와 함께 쓰지 않는다")] string? friendlyName = null,
    [Description("저장소 풀 고유 ID(UniqueId), friendlyName과 함께 쓰지 않는다")] string? uniqueId = null);
```

```csharp
// Models/Storage/StoragePoolListResult.cs
public sealed record StoragePoolListResult(IReadOnlyList<StoragePoolInfo> StoragePools);

// Models/Storage/StoragePoolInfo.cs
public sealed record StoragePoolInfo(
    string FriendlyName,
    string UniqueId,
    string ObjectId,
    IReadOnlyList<StorageOperationalStatus> OperationalStatus,
    StorageHealthStatus HealthStatus,
    bool IsPrimordial,
    bool IsReadOnly,
    bool IsClustered,
    ulong Size,
    ulong AllocatedSize,
    ulong? LogicalSectorSize,
    ulong? PhysicalSectorSize,
    string? ResiliencySettingNameDefault,
    StorageProvisioningType ProvisioningTypeDefault,
    StoragePoolUsage Usage,
    ushort Version);
```

- 목록은 구조화 출력 루트가 객체여야 하므로 래퍼 record(`StoragePools`)로 반환한다.
- `LogicalSectorSize`·`PhysicalSectorSize`는 Primordial 풀에서 null이다(로컬 확인).
- `Version`은 CIM 원값(`ushort`) 그대로 둔다.

## enum

모두 `ushort` 기반, 기존 `SpeakerSourceType`처럼 `[JsonConverter(typeof(JsonStringEnumConverter<T>))]`로 이름 직렬화. 값은 `MSFT_StoragePool` ValueMap(로컬 `Get-CimClass` 확인)과 MS 문서를 따른다. 정의 밖 코드는 캐스팅된 숫자로 직렬화된다.

| enum | 값 |
| --- | --- |
| `StorageOperationalStatus` | 0 Unknown, 1 Other, 2 OK, 3 Degraded, 4 Stressed, 5 PredictiveFailure, 6 Error, 7 NonRecoverableError, 8 Starting, 9 Stopping, 10 Stopped, 11 InService, 12 NoContact, 13 LostCommunication, 14 Aborted, 15 Dormant, 16 SupportingEntityInError, 17 Completed, 18 PowerMode, 19 Relocating, 0xD000 ReadOnly, 0xD001 Incomplete |
| `StorageHealthStatus` | 0 Healthy, 1 Warning, 2 Unhealthy, 5 Unknown |
| `StorageProvisioningType` | 0 Unknown, 1 Thin, 2 Fixed |
| `StoragePoolUsage` | 0 Unknown, 1 Other, 2 Unrestricted, 3 ReservedForComputerSystem, 4 ReservedAsDeltaReplicaContainer, 5 ReservedForMigrationServices, 6 ReservedForLocalReplicationServices, 7 ReservedForRemoteReplicationServices, 8 ReservedForSparing |

## 조회

```
OperatingSystem.IsWindows() 아니면 McpException
using CimSession session = CimSession.Create(null);   // 로컬
session.QueryInstances("root/Microsoft/Windows/Storage", "WQL", "SELECT * FROM MSFT_StoragePool")
  → 각 CimInstance를 StoragePoolInfo로 변환
```

- `GetStoragePool`은 목록을 조회한 뒤 메모리에서 필터한다(WQL 문자열 이스케이프를 피함).
  - `friendlyName`: 대소문자 무시 일치(`Get-StoragePool -FriendlyName`과 같음)
  - `uniqueId`: 대소문자 무시 일치
- `CimException`은 `McpException`으로 감싸 메시지를 전달한다.
- 필수 CIM 값이 null이거나 형식이 달라 변환에 실패한 경우(`NullReferenceException`, `InvalidCastException`)도 같은 `McpException`으로 감싼다(구현 후 검토 반영).

## 입력 검증 (`GetStoragePool`)

| 조건 | 결과 |
| --- | --- |
| 비Windows | `McpException("Storage pools are only supported on Windows")` |
| `friendlyName`·`uniqueId` 둘 다 비어 있음(null/공백) 또는 둘 다 값 있음 | `McpException("Specify exactly one of friendlyName or uniqueId")` |
| 일치 0개 | `McpException("Storage pool not found: ...")` |
| `friendlyName` 일치 2개 이상 | `McpException("Multiple storage pools match friendlyName '...'; use uniqueId")` |

- OS 검사는 입력 검증보다 먼저 한다(두 Tool 공통 private 헬퍼).

## 테스트

- Windows: `list_storage_pools`가 1개 이상 반환(Primordial 포함), 첫 항목 `UniqueId`·`FriendlyName`로 `get_storage_pool` 결과가 목록 항목과 같음, 없는 이름 → `McpException`, 입력 둘 다/둘 다 없음 → `McpException`.
- 비Windows: `[OSCondition(ConditionMode.Exclude, OperatingSystems.Windows)]`로 두 Tool이 `McpException`. 이 PC에서는 건너뛴다(미검증).
- 실제 풀 조회 테스트는 CIM 접근이 되는 Windows에서만 의미가 있으므로 `[OSCondition(OperatingSystems.Windows)]`.

## 의존성

- `Microsoft.Management.Infrastructure` 3.0.0 (nuget.org 최신, `dotnet-nuget-package` 절차로 취약·Deprecated 확인)

## 결정 목록

1. 새 `StorageTools`에 `ListStoragePools`(`list_storage_pools`), `GetStoragePool`(`get_storage_pool`)
2. 목록 결과는 래퍼 `StoragePoolListResult(StoragePools)`
3. `StoragePoolInfo` 확장 필드 16개, 섹터 크기는 nullable
4. 상태·프로비저닝·용도는 `ushort` enum + 문자열 직렬화
5. 단건 조회는 목록 후 메모리 필터, 대소문자 무시, 입력 정확히 하나
6. 비Windows·입력 오류·미존재·중복 이름·CIM 오류 → `McpException`
7. `WithBizadmToolKit()`에 `WithTools<StorageTools>()` 추가
