---
status: done
category: storage
feature: storage-virtual-disk
phase: 방안
---

# storage-virtual-disk 방안

참고: [Get-VirtualDisk](https://learn.microsoft.com/en-us/powershell/module/storage/get-virtualdisk?view=windowsserver2025-ps)
관련: `0001-storage-pool-방안.md` (풀 조회 Tool)

## 문제

`list_storage_pools`·`get_storage_pool`로 풀은 조회할 수 있지만, 풀 안에 만든 저장소 공간(가상 디스크)은 MCP 클라이언트가 확인할 수단이 없다. 매번 PowerShell에서 `Get-VirtualDisk -StoragePool`을 직접 실행해야 한다.

## 목표

- MCP Tool로 가상 디스크 정보를 조회한다.
  - 풀에 속한 가상 디스크 목록: 풀을 `friendlyName` 또는 `uniqueId` 중 정확히 하나로 지정
  - 가상 디스크 1개 조회: `friendlyName` 또는 `uniqueId` 중 정확히 하나로 지정(로컬 전체 대상)
- OS가 Windows가 아니면 오류(`McpException`)로 거부한다.
- 지정한 풀·가상 디스크가 없거나 이름이 여럿과 일치하면 오류(`McpException`)로 거부한다.
- 결과는 구조화 객체로 반환하며 확장 필드(이름·ID·상태·복원력·프로비저닝·용량·풀 점유량·복사본/컬럼 수·중복성·섹터 크기·계층/스냅숏/수동 연결 여부·접근·분리 사유·미디어 유형 등)를 담는다.

## 대안

| 안 | 내용 | 판단 |
| --- | --- | --- |
| A | CIM 연결 조회: `MSFT_StoragePool` 인스턴스 → `MSFT_StoragePoolToVirtualDisk` 연결로 `MSFT_VirtualDisk` 열거 | 채택: `Get-VirtualDisk -StoragePool`과 같은 원본, 기존 풀 조회와 같은 CIM 방식 |
| B | `MSFT_VirtualDisk` 전체 조회 후 필드로 풀 필터 | 기각: `MSFT_VirtualDisk`에 풀 식별 속성이 없음(로컬 `Get-CimClass` 확인) |
| C | `powershell.exe`로 `Get-VirtualDisk \| ConvertTo-Json` | 기각: 프로세스 비용, 출력 파싱 의존(풀 방안과 같은 사유) |
| D | 목록 Tool 1개만 | 기각: 사용자 결정(목록·단건 2개) |
| E | 기본 필드만 반환 | 기각: 사용자 결정(확장 필드) |

## 채택 방안

- 기존 `StorageTools`에 Tool 2개를 추가한다.
  - `list_virtual_disks`: 풀 지정 규칙·오류는 `get_storage_pool`과 같다(입력 정확히 하나, 미존재·동명 풀 다수 → 오류). Primordial 풀은 빈 목록을 반환한다.
  - `get_virtual_disk`: 풀 지정 없이 로컬 전체 가상 디스크에서 `friendlyName`/`uniqueId`로 1개를 찾는다(`Get-VirtualDisk -FriendlyName/-UniqueId`와 같음). 미존재·동명 다수 → 오류.
- CIM 로컬 세션, 네임스페이스 `root/Microsoft/Windows/Storage`.
- 기존 상태·프로비저닝 enum과 Windows 검사 헬퍼를 재사용한다.
- `WithBizadmToolKit()`은 이미 `StorageTools`를 등록하므로 변경하지 않는다.

## 비범위

- 가상 디스크 생성·수정·삭제·연결/분리(`New/Set/Remove/Connect/Disconnect-VirtualDisk`)
- 원격 컴퓨터 CIM 세션
- 물리 디스크·볼륨·저장소 계층 조회
- 기존 `list_storage_pools`·`get_storage_pool`의 공개 동작·오류 메시지 변경
- 기존 amp·speaker·audio Tool 변경
