---
status: done
category: audio
feature: audio-level
phase: 방안
---

# audio-level 방안

## 문제

라인 레벨 신호를 다룰 때 Vrms와 dBu를 오가는 환산을 매번 손으로 계산한다. 0dBu 기준 전압도 실무 관행값(0.775V)과 정의값(√0.6V)이 섞여 쓰인다.

## 목표

- MCP Tool로 Vrms → dBu, dBu → Vrms 환산을 제공한다.
- 0dBu 기준 전압을 선택할 수 있다.
  - Nominal: 0.775Vrms (기본값)
  - Exact: √0.6 ≈ 0.7745967Vrms (1mW @ 600Ω)
- 결과는 구조화 객체로 반환한다. 입력값, 기준, 기준 전압(V), 환산값을 담는다.
- 값은 double 원값으로 반환한다(반올림 없음).

## 계산식

```
dBu  = 20·log10(Vrms / Vref)
Vrms = Vref · 10^(dBu / 20)
```

예(Nominal): 0.775V → 0dBu, 7.75V → +20dBu, +4dBu → ≈1.2283V
예(Exact): 0.775V → ≈+0.0045dBu

## 대안

| 안 | 내용 | 판단 |
| --- | --- | --- |
| A | 방향별 Tool 2개(`convert_vrms_to_dbu`, `convert_dbu_to_vrms`) | 채택: 사용자 결정, 기존 Tool처럼 단일 목적 |
| B | Tool 1개 + 방향 enum | 기각: 사용자 결정(A) |
| C | 기준 전압 0.775V 고정 | 기각: 정의값 선택 지원 요구 |
| D | 기준 전압 √0.6V 고정 | 기각: 요청 기준은 0.775V |

## 채택 방안

- 새 `AudioLevelTools` 클래스에 Tool 메서드 2개를 둔다.
- 기준 전압은 enum 매개변수로 고르고 생략 시 Nominal(0.775V)을 쓴다.
- `WithBizadmToolKit`에 `AudioLevelTools` 등록을 추가한다.

## 비범위

- dBV, dBFS, dBm 등 다른 단위
- 피크/피크-피크 전압 환산
- 결과 반올림
- 기존 amp·speaker Tool 변경
