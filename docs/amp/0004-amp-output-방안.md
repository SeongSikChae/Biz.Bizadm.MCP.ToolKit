---
status: done
category: amp
feature: amp-output
phase: 방안
---

# amp-output 방안

관련: `0001-amp-calculator-방안.md` (`calculate_amp_max_output`)

## 문제

`calculate_amp_max_output`은 앰프의 최대 출력 전압과 전압 이득만 준다. 실제 소스 전압과 입력 감쇠(볼륨·패드)를 적용했을 때 스피커에 걸리는 전압과 전력은 다시 손으로 계산해야 한다.

## 목표

- MCP Tool 하나로 입력 전압(Vrms), 입력 감쇠(dB), 임피던스(Ω), 그리고 `calculate_amp_max_output` 결과 중 전압 이득(dB)·최대 출력 전압(Vrms)을 받아 다음 9개 값을 반환한다.
  - 입력 전압(Vpp), 입력 전압(Vrms), 입력 감쇠(dB), 실질 전압(Vpp), 실질 전압(Vrms), 임피던스(Ω), 출력 전압(Vpp), 출력 전압(Vrms), 출력 전력(W)
- 결과는 구조화 객체로 반환한다.
- 값은 double 원값으로 반환한다(반올림 없음).

## 계산식

| 출력 | 식 |
| --- | --- |
| 입력 전압 Vpp | `Vin_rms × 2√2` |
| 실질 전압 Vrms | `Vin_rms × 10^(−att/20)` |
| 실질 전압 Vpp | `Veff_rms × 2√2` |
| 출력 전압 Vrms | `min(Veff_rms × 10^(gain/20), Vmax_rms)` |
| 출력 전압 Vpp | `Vout_rms × 2√2` |
| 출력 전력 W | `Vout_rms² / R` |

- 감쇠는 양수가 감쇠다(6dB → 약 0.5배). 0 이상만 받는다.
- 출력 전압이 앰프 최대 출력 전압을 넘으면 최대값으로 제한한다(클리핑). 출력 전력도 제한된 전압으로 계산한다.

예: 1Vrms, 감쇠 6dB, 8Ω, 이득 29.03dB·최대 28.284Vrms(8Ω·100W·1Vrms의 `calculate_amp_max_output` 결과)
→ 입력 2.828Vpp, 실질 0.501Vrms / 1.418Vpp, 출력 14.176Vrms / 40.095Vpp, 25.12W

## 대안

| 안 | 내용 | 판단 |
| --- | --- | --- |
| A | 단일 Tool, 앰프 값은 이득·최대 출력 Vrms 두 double로 받음 | 채택 |
| B | `AmpMaxOutputResult` record 전체를 매개변수로 받음 | 기각: 입력 스키마가 중첩 객체가 되고 쓰지 않는 값까지 요구 |
| C | 전압 이득만 받고 클리핑하지 않음 | 기각: 최대 출력을 넘는 비현실적 값이 나옴 |
| D | 최대 출력 초과 시 예외 | 기각: 클리핑 값이 더 유용하다는 사용자 결정 |

## 채택 방안

- 기존 `AmpTools` 클래스에 Tool 메서드를 추가한다. 이름은 `0002` 설계의 규칙(`calculate_amp_{기능}` / `Calculate{기능}` / `Amp{기능}Result`)을 따르며 설계 단계에서 확정한다.
- `WithBizadmToolKit`은 이미 `WithTools<AmpTools>()`를 등록하므로 등록 코드는 바뀌지 않는다.

## 비범위

- 클리핑 여부 플래그
- 결과 반올림
- dBu/dBV 등 단위 변환
- `AmpMaxOutputResult` record를 받는 오버로드
- 기존 `calculate_amp_max_output` 변경
