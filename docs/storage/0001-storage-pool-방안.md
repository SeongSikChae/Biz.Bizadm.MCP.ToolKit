---
status: done
category: storage
feature: storage-pool
phase: 방안
---

# storage-pool 방안

참고: [Get-StoragePool](https://learn.microsoft.com/en-us/powershell/module/storage/get-storagepool?view=windowsserver2025-ps)

## 문제

Windows 저장소 공간(Storage Spaces)의 풀 상태·용량을 확인하려면 매번 PowerShell에서 `Get-StoragePool`을 직접 실행해야 한다. MCP 클라이언트(에이전트)가 이 정보를 조회할 수단이 없다.

## 목표

- MCP Tool로 저장소 풀 정보를 조회한다.
  - 전체 목록 조회
  - 특정 풀 1개 조회: `friendlyName` 또는 `uniqueId` 중 정확히 하나로 지정
- OS가 Windows가 아니면 오류(`McpException`)로 거부한다.
- 지정한 풀이 없으면 오류(`McpException`)로 거부한다.
- 결과는 구조화 객체로 반환하며 확장 필드(이름·ID·상태·용량·섹터 크기·기본 복원력/프로비저닝·용도·버전 등)를 담는다.

## 대안

| 안 | 내용 | 판단 |
| --- | --- | --- |
| A | CIM 직접 조회(`Microsoft.Management.Infrastructure`, `MSFT_StoragePool`) | 채택: 사용자 결정, `Get-StoragePool`과 같은 원본, 프로세스 생성 없음 |
| B | `powershell.exe`로 `Get-StoragePool \| ConvertTo-Json` 실행 후 파싱 | 기각: 프로세스 비용, 출력 파싱 의존 |
| C | `System.Management`(WMI) | 기각: 오래된 API |
| D | 목록 Tool 1개에 필터 매개변수 | 기각: 사용자 결정(목록·단건 Tool 분리) |
| E | 미존재 풀에 null 반환 | 기각: 사용자 결정(`McpException`) |

## 채택 방안

- 새 Tool 클래스에 목록·단건 Tool 2개를 둔다.
- CIM 로컬 세션으로 `root/Microsoft/Windows/Storage` 네임스페이스의 `MSFT_StoragePool`을 조회한다.
- 단건 조회는 `friendlyName`·`uniqueId` 중 하나만 받는다. 둘 다 또는 둘 다 없음, 동명 풀이 여럿인 경우는 오류로 거부한다.
- `WithBizadmToolKit()`에 새 Tool 클래스를 등록한다.

## 비범위

- 풀 생성·수정·삭제(`New/Set/Remove-StoragePool`)
- 원격 컴퓨터 CIM 세션
- 가상 디스크·물리 디스크 조회
- 기존 amp·speaker·audio Tool 변경
