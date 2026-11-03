---
status: done
category: amp
feature: amp-output
phase: 설계
---

# amp-output 설계

선행: `0004-amp-output-방안.md`
관련: `0002-amp-calculator-설계.md` (구조·이름 규칙)

## 구조

```
src/Biz.Bizadm.MCP.ToolKit/
  Tools/
    AmpTools.cs                                # CalculateLoadOutput 메서드·검증 헬퍼 추가
  Models/
    Amp/
      AmpLoadOutputResult.cs                   # calculate_amp_load_output 결과 record (신규)
tests/Biz.Bizadm.MCP.ToolKit.Tests/
  AmpToolsTests.cs                             # 테스트 추가
```

- `0002`에 예약된 이름을 쓴다: Tool `calculate_amp_load_output` / 메서드 `CalculateLoadOutput` / 결과 `AmpLoadOutputResult`.
- `BizadmToolKitMcpServerBuilderExtensions.cs`는 바뀌지 않는다(`WithTools<AmpTools>()`가 새 메서드도 등록).

## 인터페이스

```csharp
// Tools/AmpTools.cs
[McpServerTool(Name = "calculate_amp_load_output", UseStructuredContent = true,
    ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
[Description("입력 전압(Vrms), 입력 감쇠(dB), 스피커 임피던스(Ω), 앰프 전압 이득(dB), 최대 출력 전압(Vrms)으로 실질 입력 전압, 출력 전압(Vrms/Vpp), 출력 전력(W)을 계산한다. 출력 전압은 최대 출력 전압으로 제한한다.")]
public static AmpLoadOutputResult CalculateLoadOutput(
    [Description("입력 전압(Vrms)")] double inputVrms,
    [Description("입력 감쇠(dB), 0 이상, 양수가 감쇠")] double inputAttenuationDb,
    [Description("스피커 임피던스(Ω)")] double impedanceOhm,
    [Description("앰프 전압 이득(dB), calculate_amp_max_output의 voltageGainDb")] double voltageGainDb,
    [Description("앰프 최대 출력 전압(Vrms), calculate_amp_max_output의 maxOutputVrms")] double maxOutputVrms);

// Models/Amp/AmpLoadOutputResult.cs
public sealed record AmpLoadOutputResult(
    double InputVpp,
    double InputVrms,
    double InputAttenuationDb,
    double EffectiveVpp,
    double EffectiveVrms,
    double ImpedanceOhm,
    double OutputVpp,
    double OutputVrms,
    double OutputPowerW);
```

- 필드 순서는 사용자 요청 순서다.
- 기존과 같이 `[Description]`과 `///` XML 주석(한국어)을 단다.

## 계산

| 필드 | 식 |
| --- | --- |
| `InputVpp` | `inputVrms × 2√2` |
| `EffectiveVrms` | `inputVrms × 10^(−inputAttenuationDb/20)` |
| `EffectiveVpp` | `EffectiveVrms × 2√2` |
| `OutputVrms` | `min(EffectiveVrms × 10^(voltageGainDb/20), maxOutputVrms)` |
| `OutputVpp` | `OutputVrms × 2√2` |
| `OutputPowerW` | `OutputVrms² / impedanceOhm` |

`InputVrms`, `InputAttenuationDb`, `ImpedanceOhm`은 입력값을 그대로 담는다. 반올림하지 않는다.

## 입력 검증

| 매개변수 | 조건 | 헬퍼 |
| --- | --- | --- |
| `inputVrms`, `impedanceOhm`, `maxOutputVrms` | 양의 유한수 | 기존 `EnsurePositiveFinite` |
| `inputAttenuationDb` | 0 이상 유한수 | 신규 private `EnsureNonNegativeFinite` |
| `voltageGainDb` | 유한수(음수 허용) | 신규 private `EnsureFinite` |

- 위반 시 `McpException`, 메시지 형식은 기존과 같다. 예: `inputAttenuationDb must be a non-negative finite number: -1`, `voltageGainDb must be a finite number: NaN`

## 의존성

추가 패키지 없음.

## 결정 목록

1. Tool `calculate_amp_load_output`, 메서드 `CalculateLoadOutput`, 결과 `AmpLoadOutputResult` (`0002` 예약 이름)
2. 앰프 값은 `voltageGainDb`, `maxOutputVrms` 두 double 매개변수
3. 출력 전압은 `maxOutputVrms`로 클리핑, 전력도 클리핑된 전압 기준
4. 감쇠는 양수=감쇠, 0 이상
5. 검증: 양의 유한수 3개, 감쇠 0 이상, 이득 유한수 → `McpException`
6. 기존 `PeakToPeakPerRms`, `EnsurePositiveFinite` 재사용, 헬퍼 2개 추가
7. 등록 코드 변경 없음, 반올림 없음
