---
status: done
category: storage
feature: storage-disk-format
phase: 방안
---

# storage-disk-format 방안

참고: [Initialize-Disk](https://learn.microsoft.com/en-us/powershell/module/storage/initialize-disk?view=windowsserver2025-ps), [New-Partition](https://learn.microsoft.com/en-us/powershell/module/storage/new-partition?view=windowsserver2025-ps), [Format-Volume](https://learn.microsoft.com/en-us/powershell/module/storage/format-volume?view=windowsserver2025-ps)
관련: `0007-storage-virtual-disk-write-방안.md` (생성 Tool. 초기화·파티션·볼륨을 비범위로 둠)

## 문제

`create_virtual_disk`로 만든 저장소 공간은 초기화되지 않은 RAW 디스크다. 제어판에는 "포맷되지 않았습니다. 저장소 공간을 포맷하십시오."로 표시된다. 지금은 MCP 클라이언트가 이 디스크를 쓸 수 있게 만들 방법이 없다. 디스크 관리나 PowerShell에서 `Initialize-Disk`, `New-Partition`, `Format-Volume`을 직접 실행해야 한다.

## 목표

- 가상 디스크를 사용할 수 있게 만드는 MCP Tool 3개를 둔다. 각 Tool은 cmdlet 하나에 대응한다.
  - `initialize_disk`: 디스크를 GPT 또는 MBR로 초기화한다(Initialize-Disk).
  - `create_partition`: 초기화된 디스크의 빈 공간에 파티션을 만든다(New-Partition).
  - `format_volume`: 포맷되지 않은 파티션의 볼륨을 포맷한다(Format-Volume).
- 대상은 세 Tool 모두 가상 디스크로 지정한다(`friendlyName` 또는 `uniqueId` 중 정확히 하나, 기존 Tool과 같은 규칙). 가상 디스크에 대응하는 디스크는 `MSFT_VirtualDiskToDisk` 연결로 찾는다.
- RAW 대상만 허용한다(사용자 결정).
  - `initialize_disk`: 디스크의 `PartitionStyle`이 RAW일 때만 실행한다.
  - `create_partition`: 이미 초기화된 디스크의 빈 공간에만 만든다. CIM 메서드는 기존 파티션을 덮어쓰지 않는다.
  - `format_volume`: 볼륨의 파일 시스템이 없을 때(RAW)만 실행한다. 이미 포맷된 볼륨은 오류로 거부한다.
- RAW 검사만으로는 데이터 손실을 막지 못한다(구현 후 독립 검토에서 확인). 잠긴 BitLocker 볼륨, Windows가 인식하지 못하는 파일 시스템, 손상된 볼륨도 RAW로 보고된다. 파티션 테이블 없이 쓰는 디스크도 RAW 디스크로 보인다. 그래서 `initialize_disk`와 `format_volume`은 삭제 Tool처럼 `confirm=true`를 요구하고 Destructive로 표시한다(사용자 결정). `create_partition`은 빈 공간에만 쓰므로 그대로 둔다.
- 옵션은 cmdlet의 스칼라 파라미터를 전부 받는다(사용자 결정: 전체 옵션). 재포맷을 강제하는 `Force`는 받지 않는다.
- OS가 Windows가 아니거나, 입력이 잘못되었거나, 대상이 없거나, 이름이 여러 대상과 일치하거나, RAW 조건을 만족하지 않거나, CIM 메서드가 실패하면 `McpException`으로 거부한다.

## 대안

| 안 | 내용 | 판단 |
| --- | --- | --- |
| A | 단계별 Tool 3개, CIM 메서드 `MSFT_Disk.Initialize`, `MSFT_Disk.CreatePartition`, `MSFT_Volume.Format` | 채택(사용자 결정). cmdlet과 1:1로 대응하고, 기존 Tool과 같은 CIM 세션 방식이다. |
| B | 단일 Tool `format_virtual_disk`로 세 단계를 한 번에 | 기각: 사용자 결정(단계별) |
| C | 디스크 번호로 대상 지정 | 기각: 사용자 결정. 저장소 공간이 아닌 물리 디스크까지 대상이 돼 위험 범위가 넓다. |
| D | RAW가 아니면 `confirm=true`일 때 Clear-Disk 후 재포맷 | 기각: 사용자 결정(RAW만 허용) |
| D2 | RAW 검사만 두고 Destructive=false | 기각(검토 회귀): RAW로 보고되는 볼륨·디스크에도 데이터가 있을 수 있다. RAW 검사에 `confirm`을 더한다. |
| E | 핵심 옵션만 | 기각: 사용자 결정(전체 옵션) |
| F | `powershell.exe`로 cmdlet 실행 | 기각: 0001·0004·0007 방안과 같은 사유 |

## 채택 방안

- 기존 `StorageTools`에 Tool 3개를 추가한다. 기존 헬퍼(`EnsureWindows`, `EnsureExactlyOne`, `SelectByName`, `Invoke`, `WaitForJob`, `EnsureTimeout`)를 재사용한다.
- `format_volume`은 job을 반환할 수 있다(`RunAsJob`, `CreatedStorageJob`). 그래서 `timeoutSeconds`와 `CancellationToken`을 0008 설계와 같은 방식으로 받는다.
- 디스크·파티션·볼륨 정보를 반환하는 결과 모델과, 파티션이 여러 개일 때 대상 파티션을 고르는 방법은 설계에서 정한다.
- `WithBizadmToolKit()`은 바꾸지 않는다.

## 비범위

- 디스크 번호나 물리 디스크로 대상 지정
- 디스크 지우기(Clear-Disk), 파티션 삭제, 재포맷, 파티션 크기 변경
- 드라이브 문자 변경·제거(Set-Partition, Remove-PartitionAccessPath)
- 디스크·파티션·볼륨 조회 전용 Tool
- 세 단계를 한 번에 하는 Tool
- 원격 CIM 세션
- 기존 storage·amp·speaker·audio Tool의 공개 동작 변경
