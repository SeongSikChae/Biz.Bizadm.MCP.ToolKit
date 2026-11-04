---
status: done
category: storage
feature: storage-virtual-disk-write
phase: 방안
---

# storage-virtual-disk-write 방안

참고: [New-VirtualDisk](https://learn.microsoft.com/en-us/powershell/module/storage/new-virtualdisk?view=windowsserver2025-ps), [Resize-VirtualDisk](https://learn.microsoft.com/en-us/powershell/module/storage/resize-virtualdisk?view=windowsserver2025-ps), [Set-VirtualDisk](https://learn.microsoft.com/en-us/powershell/module/storage/set-virtualdisk?view=windowsserver2025-ps), [Remove-VirtualDisk](https://learn.microsoft.com/en-us/powershell/module/storage/remove-virtualdisk?view=windowsserver2025-ps)
관련: `0004-storage-virtual-disk-방안.md` (가상 디스크 조회 Tool, 생성·수정·삭제를 비범위로 둠)

## 문제

`list_virtual_disks`와 `get_virtual_disk`로 가상 디스크를 조회할 수는 있다. 하지만 MCP 클라이언트가 저장소 풀에 가상 디스크를 만들거나, 크기·속성을 바꾸거나, 삭제할 방법은 없다. 지금은 PowerShell에서 `New/Resize/Set/Remove-VirtualDisk`를 직접 실행해야 한다.

## 목표

- MCP Tool로 로컬 Windows 저장소 풀의 가상 디스크를 생성·수정·삭제한다.
  - 생성: 풀을 `friendlyName` 또는 `uniqueId` 중 정확히 하나로 지정한다. 인자는 New-VirtualDisk의 스칼라 파라미터 전부를 받는다.
  - 크기 변경: Resize-VirtualDisk(`Size`)
  - 속성 변경: Set-VirtualDisk(`NewFriendlyName`, `Usage`, `OtherUsageDescription`, `IsManualAttach`, `StorageNodeName`, `Access`)
  - 삭제: Remove-VirtualDisk. `confirm=true`를 명시할 때만 삭제한다.
- 대상 가상 디스크는 `friendlyName` 또는 `uniqueId` 중 정확히 하나로 지정한다. 기존 조회 Tool과 같은 규칙이다.
- OS가 Windows가 아니거나, 입력이 잘못되었거나, 대상이 없거나, 이름이 여러 대상과 일치하거나, CIM 메서드가 실패하면 `McpException`으로 거부한다.

## 대안

| 안 | 내용 | 판단 |
| --- | --- | --- |
| A | CIM 메서드 호출: `MSFT_StoragePool.CreateVirtualDisk`, `MSFT_VirtualDisk.Resize`, `SetFriendlyName`, `SetUsage`, `SetAttributes`, `DeleteObject` | 채택(사용자 결정). cmdlet과 같은 원본이고, 기존 조회와 같은 CIM 세션 방식이다. |
| B | `powershell.exe`로 cmdlet 실행 | 기각: 프로세스 비용이 들고 출력 파싱에 의존한다(0001·0004 방안과 같은 사유). |
| C | 수정은 Resize만 | 기각: 사용자 결정(Resize와 Set 모두) |
| D | 생성 인자를 핵심 파라미터만 | 기각: 사용자 결정(스칼라 파라미터 전부) |
| E | 삭제는 Destructive 표시만 | 기각: 사용자 결정(`confirm` 인자 추가) |

## 채택 방안

- 기존 `StorageTools`에 Tool 4개를 추가한다: `create_virtual_disk`, `resize_virtual_disk`, `set_virtual_disk`, `remove_virtual_disk`.
- 생성 인자에서 제외하는 것: CimInstance 배열(`StorageTiers`, `StorageTierSizes`, `PhysicalDisksToUse`)과 `CimSession`, `ThrottleLimit`, `AsJob`
- 지정하지 않은 선택 인자는 CIM 메서드에 넘기지 않는다. 풀 기본값을 따른다.
- 생성·수정 Tool은 작업 후 가상 디스크를 다시 조회해 `VirtualDiskInfo`를 반환한다.
- 기존 헬퍼(`EnsureWindows`, `EnsureExactlyOne`, `SelectOne`)와 enum을 재사용한다.
- `WithBizadmToolKit()`은 이미 `StorageTools`를 등록하므로 바꾸지 않는다.

## 비범위

- 가상 디스크 연결/분리(`Connect/Disconnect-VirtualDisk`), 복구·최적화
- 저장소 계층·물리 디스크 지정 생성
- 원격 컴퓨터 CIM 세션, 비동기 job 반환
- 디스크 초기화·파티션·볼륨 생성
- 기존 조회 Tool(`list/get_storage_pool`, `list/get_virtual_disk`)의 공개 동작·오류 메시지 변경
- 기존 amp·speaker·audio Tool 변경
