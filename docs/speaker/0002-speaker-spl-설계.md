---
status: done
category: speaker
feature: speaker-spl
phase: 설계
---

# speaker-spl 설계

선행: `0001-speaker-spl-방안.md`
관련: `docs/amp/0002-amp-calculator-설계.md` (구조·이름 규칙)

## 구조

```
src/Biz.Bizadm.MCP.ToolKit/
  Tools/
    SpeakerTools.cs                            # calculate_speaker_spl Tool (신규)
  Models/
    Speaker/
      SpeakerSourceType.cs                     # 음원 유형 enum (신규)
      SpeakerSplResult.cs                      # calculate_speaker_spl 결과 record (신규)
  BizadmToolKitMcpServerBuilderExtensions.cs   # WithTools<SpeakerTools>() 추가
tests/Biz.Bizadm.MCP.ToolKit.Tests/
  SpeakerToolsTests.cs                         # 신규
```

- 이름 규칙은 amp와 같다: Tool `calculate_speaker_spl` / 클래스 `SpeakerTools` / 메서드 `CalculateSpl` / 결과 `SpeakerSplResult`.
- 네임스페이스: `Biz.Bizadm.MCP.ToolKit.Tools`, `Biz.Bizadm.MCP.ToolKit.Models.Speaker` (file-scoped).

## 인터페이스

```csharp
// Models/Speaker/SpeakerSourceType.cs
[JsonConverter(typeof(JsonStringEnumConverter<SpeakerSourceType>))]
public enum SpeakerSourceType
{
    /// <summary>점음원, 거리 2배당 −6dB (역제곱 법칙)</summary>
    Point,
    /// <summary>선음원(라인어레이), 거리 2배당 −3dB</summary>
    Line,
}

// Models/Speaker/SpeakerSplResult.cs
public sealed record SpeakerSplResult(
    double NominalSplDb,
    double InputPowerW,
    double DistanceM,
    SpeakerSourceType SourceType,
    double OutputSplDb);

// Tools/SpeakerTools.cs
[McpServerToolType]
public sealed class SpeakerTools
{
    [McpServerTool(Name = "calculate_speaker_spl", UseStructuredContent = true,
        ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("스피커 Nominal SPL(dB SPL, 1W/1m 감도), 입력 전력(W), 거리(m), 음원 유형(Point/Line)으로 해당 거리의 Output SPL(dB SPL)을 계산한다. Point는 거리 2배당 −6dB, Line은 −3dB 감쇠한다.")]
    public static SpeakerSplResult CalculateSpl(
        [Description("Nominal SPL(dB SPL), 1W/1m 감도")] double nominalSplDb,
        [Description("입력 전력(W), calculate_amp_load_output의 outputPowerW")] double inputPowerW,
        [Description("거리(m)")] double distanceM,
        [Description("음원 유형: Point(점음원, 역제곱) 또는 Line(라인어레이)")] SpeakerSourceType sourceType);
}
```

- enum은 `JsonStringEnumConverter<T>`로 문자열 `"Point"`/`"Line"`을 입출력·스키마에 쓴다.
- `sourceType`은 기본값을 두지 않는다(필수).
- 기존과 같이 `[Description]`과 `///` XML 주석(한국어)을 단다.

## 계산

| 필드 | 식 |
| --- | --- |
| `OutputSplDb` | `nominalSplDb + 10·log10(inputPowerW) − k·log10(distanceM)` |

- `k`: `Point` 20, `Line` 10
- `NominalSplDb`, `InputPowerW`, `DistanceM`, `SourceType`은 입력값을 그대로 담는다. 반올림하지 않는다.

## 입력 검증

| 매개변수 | 조건 | 헬퍼 |
| --- | --- | --- |
| `nominalSplDb` | 유한수 | private `EnsureFinite` |
| `inputPowerW`, `distanceM` | 양의 유한수 | private `EnsurePositiveFinite` |
| `sourceType` | 정의된 값 (`Enum.IsDefined`) | 메서드 안에서 검사 |

- 위반 시 `McpException`, 메시지 형식은 amp와 같다. 예: `distanceM must be a positive finite number: 0`, `sourceType must be Point or Line: 5`
- 헬퍼는 `AmpTools`의 private 헬퍼와 같은 형태로 `SpeakerTools`에 private로 둔다. `AmpTools`는 바꾸지 않는다.

## 등록

```csharp
builder.WithTools<AmpTools>();
builder.WithTools<SpeakerTools>();
```

## 의존성

추가 패키지 없음 (`System.Text.Json`은 프레임워크 포함).

## 결정 목록

1. Tool `calculate_speaker_spl`, 클래스 `SpeakerTools`, 메서드 `CalculateSpl`, 결과 `SpeakerSplResult`
2. 음원 유형은 문자열 enum `SpeakerSourceType { Point, Line }`, 필수 매개변수
3. 입력 신호는 전력(W), Nominal SPL은 1W/1m 감도
4. 결과 필드 순서: NominalSplDb, InputPowerW, DistanceM, SourceType, OutputSplDb
5. 검증: Nominal 유한수, 전력·거리 양의 유한수, enum 정의값 → `McpException`
6. 검증 헬퍼는 `SpeakerTools`에 private로 둠, `AmpTools` 무변경
7. `WithBizadmToolKit`에 `WithTools<SpeakerTools>()` 추가, 반올림 없음
