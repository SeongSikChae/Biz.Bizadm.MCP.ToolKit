---
status: done
category: speaker
feature: speaker-spl
phase: 방안
---

# speaker-spl 방안

관련: `docs/amp/0004-amp-output-방안.md` (`calculate_amp_load_output`)

## 문제

`calculate_amp_load_output`은 스피커에 들어가는 전력(W)까지만 준다. 스피커 감도와 청취 거리를 적용한 실제 음압(SPL)은 다시 손으로 계산해야 한다.

## 목표

- MCP Tool 하나로 Nominal SPL(dB SPL, 1W/1m 감도), 입력 전력(W), 거리(m), 음원 유형(점음원/라인어레이)을 받아 다음 값을 반환한다.
  - Nominal SPL(dB SPL), 입력 전력(W), 거리(m), 음원 유형, Output SPL(dB SPL)
- 결과는 구조화 객체로 반환한다.
- 값은 double 원값으로 반환한다(반올림 없음).

## 계산식

```
Output SPL = NominalSpl + 10·log10(P / 1W) − k·log10(d / 1m)
```

| 음원 유형 | k | 거리 2배당 감쇠 |
| --- | --- | --- |
| 점음원(역제곱 법칙) | 20 | −6.02dB |
| 라인어레이(선음원) | 10 | −3.01dB |

- 입력 전력은 `calculate_amp_load_output`의 `outputPowerW`를 그대로 넣을 수 있다.
- 거리 1m 미만도 받는다(식 그대로 적용).

예: 90dB SPL(1W/1m), 100W, 10m
→ 점음원 90 + 20 − 20 = 90dB SPL, 라인어레이 90 + 20 − 10 = 100dB SPL

## 대안

| 안 | 내용 | 판단 |
| --- | --- | --- |
| A | 입력 신호를 전력(W)으로 받음, 감도 기준 1W/1m | 채택: 앰프 툴의 `outputPowerW`와 연계 |
| B | 입력 신호를 전압(Vrms)으로 받음, 감도 기준 2.83V/1m | 기각: 임피던스에 따라 기준 전력이 달라 혼동 |
| C | 입력 감쇠(dB)로 받고 Nominal SPL을 정격 최대 SPL로 봄 | 기각: 사용자 결정(A) |
| D | 점음원만 지원 | 기각: 라인어레이 선택 지원 요구 |
| E | 음원 유형별로 Tool 두 개 | 기각: 식이 계수 k만 달라 매개변수 하나로 충분 |

## 채택 방안

- 새 `SpeakerTools` 클래스에 Tool 메서드를 둔다. 이름은 amp 규칙을 따라 `calculate_speaker_{기능}` 형태로 설계 단계에서 확정한다.
- `WithBizadmToolKit`에 `SpeakerTools` 등록을 추가한다.

## 비범위

- 근거리장(라인어레이 임계 거리 등) 보정
- 다중 스피커 합산, 공기 흡수, 실내 반사
- 스피커 최대 SPL·정격 전력 제한
- 전압(V)·감쇠(dB) 입력 방식
- 결과 반올림
- 기존 amp Tool 변경
