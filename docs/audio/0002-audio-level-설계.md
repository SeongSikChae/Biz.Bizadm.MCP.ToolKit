---
status: done
category: audio
feature: audio-level
phase: 설계
---

# audio-level 설계

선행: `0001-audio-level-방안.md`
관련: `docs/speaker/0002-speaker-spl-설계.md` (구조·이름·enum 규칙)

## 구조

```
src/Biz.Bizadm.MCP.ToolKit/
  Tools/
    AudioLevelTools.cs                         # convert_vrms_to_dbu, convert_dbu_to_vrms Tool (신규)
  Models/
    Audio/
      DbuReference.cs                          # 0dBu 기준 전압 enum (신규)
      VrmsToDbuResult.cs                       # convert_vrms_to_dbu 결과 record (신규)
      DbuToVrmsResult.cs                       # convert_dbu_to_vrms 결과 record (신규)
  BizadmToolKitMcpServerBuilderExtensions.cs   # WithTools<AudioLevelTools>() 추가
tests/Biz.Bizadm.MCP.ToolKit.Tests/
  AudioLevelToolsTests.cs                      # 신규
```

- 이름: 클래스 `AudioLevelTools` / 메서드 `ConvertVrmsToDbu`, `ConvertDbuToVrms` / 결과 `VrmsToDbuResult`, `DbuToVrmsResult`.
- Tool 이름은 변환이라 `calculate_` 대신 `convert_` 접두를 쓴다.
- 네임스페이스: `Biz.Bizadm.MCP.ToolKit.Tools`, `Biz.Bizadm.MCP.ToolKit.Models.Audio` (file-scoped).

## 인터페이스

```csharp
// Models/Audio/DbuReference.cs
[JsonConverter(typeof(JsonStringEnumConverter<DbuReference>))]
public enum DbuReference
{
    /// <summary>0dBu = 0.775Vrms (실무 관행값)</summary>
    Nominal,
    /// <summary>0dBu = √0.6 ≈ 0.7745967Vrms (1mW @ 600Ω 정의값)</summary>
    Exact,
}

// Models/Audio/VrmsToDbuResult.cs
public sealed record VrmsToDbuResult(
    double Vrms,
    DbuReference Reference,
    double ReferenceVrms,
    double Dbu);

// Models/Audio/DbuToVrmsResult.cs
public sealed record DbuToVrmsResult(
    double Dbu,
    DbuReference Reference,
    double ReferenceVrms,
    double Vrms);

// Tools/AudioLevelTools.cs
[McpServerToolType]
public sealed class AudioLevelTools
{
    [McpServerTool(Name = "convert_vrms_to_dbu", UseStructuredContent = true,
        ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("전압(Vrms)을 dBu로 환산한다. 0dBu 기준은 Nominal(0.775V, 기본) 또는 Exact(√0.6V)이다.")]
    public static VrmsToDbuResult ConvertVrmsToDbu(
        [Description("전압(Vrms)")] double vrms,
        [Description("0dBu 기준: Nominal(0.775V, 기본) 또는 Exact(√0.6V)")] DbuReference reference = DbuReference.Nominal);

    [McpServerTool(Name = "convert_dbu_to_vrms", UseStructuredContent = true,
        ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("dBu를 전압(Vrms)으로 환산한다. 0dBu 기준은 Nominal(0.775V, 기본) 또는 Exact(√0.6V)이다.")]
    public static DbuToVrmsResult ConvertDbuToVrms(
        [Description("레벨(dBu)")] double dbu,
        [Description("0dBu 기준: Nominal(0.775V, 기본) 또는 Exact(√0.6V)")] DbuReference reference = DbuReference.Nominal);
}
```

- enum은 `JsonStringEnumConverter<T>`로 문자열 `"Nominal"`/`"Exact"`를 입출력·스키마에 쓴다.
- `reference`는 기본값 `Nominal`을 둔 선택 매개변수다.
- 기존과 같이 `[Description]`과 `///` XML 주석(한국어)을 단다.

## 계산

| 기준 | `ReferenceVrms` |
| --- | --- |
| `Nominal` | `0.775` |
| `Exact` | `Math.Sqrt(0.6)` |

| 필드 | 식 |
| --- | --- |
| `VrmsToDbuResult.Dbu` | `20·log10(vrms / ReferenceVrms)` |
| `DbuToVrmsResult.Vrms` | `ReferenceVrms · 10^(dbu / 20)` |

- 기준 전압은 private 헬퍼 `GetReferenceVrms(DbuReference)`에서 고른다.
- 입력값과 기준은 그대로 결과에 담는다. 반올림하지 않는다.

## 입력 검증

| 매개변수 | 조건 | 헬퍼 |
| --- | --- | --- |
| `vrms` | 양의 유한수 | private `EnsurePositiveFinite` |
| `dbu` | 유한수 | private `EnsureFinite` |
| `reference` | 정의된 값 (`Enum.IsDefined`) | `GetReferenceVrms` 안에서 검사 |

- 위반 시 `McpException`, 메시지 형식은 기존과 같다. 예: `vrms must be a positive finite number: 0`, `reference must be Nominal or Exact: 5`
- 헬퍼는 `SpeakerTools`와 같은 형태로 `AudioLevelTools`에 private로 둔다. 기존 Tool 클래스는 바꾸지 않는다.
- 매우 큰 dBu(예: 1e6)는 `Vrms`가 `∞`가 될 수 있다. 방안 비범위에 따라 별도 제한은 두지 않는다.

## 등록

```csharp
builder.WithTools<AmpTools>();
builder.WithTools<SpeakerTools>();
builder.WithTools<AudioLevelTools>();
```

## 의존성

추가 패키지 없음.

## 결정 목록

1. 클래스 `AudioLevelTools`, Tool `convert_vrms_to_dbu`/`convert_dbu_to_vrms`, 메서드 `ConvertVrmsToDbu`/`ConvertDbuToVrms`
2. 결과 `VrmsToDbuResult(Vrms, Reference, ReferenceVrms, Dbu)`, `DbuToVrmsResult(Dbu, Reference, ReferenceVrms, Vrms)`
3. 기준 전압 문자열 enum `DbuReference { Nominal, Exact }`, 선택 매개변수, 기본 `Nominal`
4. Nominal 0.775V, Exact `Math.Sqrt(0.6)`
5. 검증: Vrms 양의 유한수, dBu 유한수, enum 정의값 → `McpException`
6. 검증 헬퍼는 `AudioLevelTools`에 private로 둠, 기존 Tool 무변경
7. `WithBizadmToolKit`에 `WithTools<AudioLevelTools>()` 추가, 반올림 없음
