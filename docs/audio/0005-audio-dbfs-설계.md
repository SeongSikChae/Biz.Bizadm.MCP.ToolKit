---
status: done
category: audio
feature: audio-dbfs
phase: 설계
---

# audio-dbfs 설계

선행: `0004-audio-dbfs-방안.md`
관련: `docs/audio/0002-audio-level-설계.md` (구조·이름·검증 규칙)

## 구조

```
src/Biz.Bizadm.MCP.ToolKit/
  Tools/
    AudioLevelTools.cs                         # convert_dbu_to_dbfs, convert_dbfs_to_dbu 추가
  Models/
    Audio/
      DbuToDbfsResult.cs                       # convert_dbu_to_dbfs 결과 record (신규)
      DbfsToDbuResult.cs                       # convert_dbfs_to_dbu 결과 record (신규)
tests/Biz.Bizadm.MCP.ToolKit.Tests/
  AudioLevelToolsTests.cs                      # 테스트 추가
```

- 이름: 메서드 `ConvertDbuToDbfs`, `ConvertDbfsToDbu` / 결과 `DbuToDbfsResult`, `DbfsToDbuResult`.
- 등록(`BizadmToolKitMcpServerBuilderExtensions`)은 바꾸지 않는다.

## 인터페이스

```csharp
// Models/Audio/DbuToDbfsResult.cs
public sealed record DbuToDbfsResult(
    double FullScaleDbu,
    double Dbu,
    double Dbfs);

// Models/Audio/DbfsToDbuResult.cs
public sealed record DbfsToDbuResult(
    double FullScaleDbu,
    double Dbfs,
    double Dbu);

// Tools/AudioLevelTools.cs (추가)
[McpServerTool(Name = "convert_dbu_to_dbfs", UseStructuredContent = true,
    ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
[Description("0dBFS에 해당하는 기준 dBu로 레벨(dBu)을 dBFS로 환산한다. 0dBFS를 넘는 레벨(클리핑)은 거부한다.")]
public static DbuToDbfsResult ConvertDbuToDbfs(
    [Description("0dBFS에 해당하는 기준 레벨(dBu), 예: +24")] double fullScaleDbu,
    [Description("환산할 레벨(dBu), fullScaleDbu 이하")] double dbu);

[McpServerTool(Name = "convert_dbfs_to_dbu", UseStructuredContent = true,
    ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
[Description("0dBFS에 해당하는 기준 dBu로 디지털 레벨(dBFS)을 dBu로 환산한다. 0dBFS를 넘는 레벨(클리핑)은 거부한다.")]
public static DbfsToDbuResult ConvertDbfsToDbu(
    [Description("0dBFS에 해당하는 기준 레벨(dBu), 예: +24")] double fullScaleDbu,
    [Description("환산할 디지털 레벨(dBFS), 0 이하")] double dbfs);
```

- 매개변수 순서는 기준 → 대상(요청 순서)이다. 모두 필수다.
- 기존과 같이 `[Description]`과 `///` XML 주석(한국어)을 단다.

## 계산

| 필드 | 식 |
| --- | --- |
| `DbuToDbfsResult.Dbfs` | `dbu − fullScaleDbu` |
| `DbfsToDbuResult.Dbu` | `fullScaleDbu + dbfs` |

- 입력값은 그대로 결과에 담는다. 반올림하지 않는다.

## 입력 검증

| 메서드 | 조건 | 헬퍼 |
| --- | --- | --- |
| 공통 | `fullScaleDbu` 유한수 | 기존 private `EnsureFinite` |
| `ConvertDbuToDbfs` | `dbu` 유한수, `dbu ≤ fullScaleDbu` | `EnsureFinite` + 메서드 안 검사 |
| `ConvertDbfsToDbu` | `dbfs` 유한수, `dbfs ≤ 0` | `EnsureFinite` + 메서드 안 검사 |

- 위반 시 `McpException`. 메시지 예:
  - `dbu must not exceed fullScaleDbu (0dBFS): 25 > 24`
  - `dbfs must not exceed 0 (0dBFS): 0.1`
- 경계값(`dbu == fullScaleDbu`, `dbfs == 0`)은 허용한다.
- 새 헬퍼는 만들지 않는다. 기존 Vrms↔dBu 메서드와 헬퍼는 바꾸지 않는다.

## 의존성

추가 패키지 없음.

## 결정 목록

1. 기존 `AudioLevelTools`에 `ConvertDbuToDbfs`(`convert_dbu_to_dbfs`), `ConvertDbfsToDbu`(`convert_dbfs_to_dbu`) 추가
2. 결과 `DbuToDbfsResult(FullScaleDbu, Dbu, Dbfs)`, `DbfsToDbuResult(FullScaleDbu, Dbfs, Dbu)`
3. 매개변수 순서 `fullScaleDbu` → 대상값, 모두 필수
4. 검증: 모든 입력 유한수, `dbu ≤ fullScaleDbu`, `dbfs ≤ 0` → 위반 시 `McpException`, 경계 허용
5. 기존 `EnsureFinite` 재사용, 등록·기존 Tool 무변경, 반올림 없음
