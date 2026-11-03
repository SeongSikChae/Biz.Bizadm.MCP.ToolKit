---
status: done
category: amp
feature: amp-calculator
phase: 방안
---

# amp-calculator 방안

## 문제

앰프와 소스·스피커를 매칭할 때 입력 감도, 최대 출력 전압, 전압 이득을 매번 손으로 계산한다. Vrms·Vpp 환산과 dB 계산에서 실수가 잦다.

## 목표

- MCP Tool 하나로 임피던스(Ω), 정격 출력(W), 입력 감도(Vrms)를 받아 다음 7개 값을 반환한다.
  - 임피던스(Ω), 전력(W), 입력 감도(Vrms), 입력 감도(Vpp), 최대 출력 전압(Vrms), 최대 출력 전압(Vpp), 전압 이득(dB)
- 결과는 구조화 객체로 반환해 클라이언트가 값을 바로 쓸 수 있게 한다.
- 값은 double 원값으로 반환한다(반올림 없음).

## 계산식

| 출력 | 식 |
| --- | --- |
| 입력 감도 Vpp | `Vin_rms × 2√2` |
| 최대 출력 Vrms | `√(P × R)` |
| 최대 출력 Vpp | `Vout_rms × 2√2` |
| 전압 이득 dB | `20 × log10(Vout_rms / Vin_rms)` |

예: 8Ω, 100W, 1Vrms → 입력 2.828Vpp, 출력 28.284Vrms / 80Vpp, 이득 29.03dB

## 대안

| 안 | 내용 | 판단 |
| --- | --- | --- |
| A | 단일 Tool, 7개 값을 담은 record 반환 | 채택 |
| B | 값마다 Tool 분리(Vpp 환산, 출력 전압, 이득) | 기각: 호출 수 증가, 1차 요구는 한 번에 전체 결과 |
| C | 단일 Tool, 사람이 읽는 텍스트 반환 | 기각: 구조화 객체로 결정 |

## 채택 방안

- `Biz.Bizadm.MCP.ToolKit.Tools`에 앰프 계산 Tool 클래스를 추가한다.
- `WithBizadmToolKit`에 명시 등록한다(scaffold 설계 원칙).
- 서버는 개별 `WithTools<T>()`로도 등록할 수 있다.

## 비범위

- 브리지·다채널 출력 계산
- dBu/dBV 등 단위 변환, 헤드룸·감쇠 계산
- 결과 반올림
- 추가 앰프 계산 Tool(2차 이후 별도 요청)
