---
status: done
category: audio
feature: audio-dbfs
phase: 방안
---

# audio-dbfs 방안

관련: `docs/audio/0001-audio-level-방안.md` (`convert_vrms_to_dbu`, `convert_dbu_to_vrms`)

## 문제

아날로그 레벨(dBu)과 디지털 레벨(dBFS)을 맞출 때, 장비마다 0dBFS에 해당하는 dBu(예: +24dBu, +18dBu)가 달라 매번 손으로 환산한다.

## 목표

- MCP Tool로 dBu → dBFS, dBFS → dBu 환산을 제공한다.
- 0dBFS 기준 dBu를 입력으로 받는다.
- 0dBFS를 넘는 값(클리핑 영역)은 오류로 거부한다. 0dBFS 자체는 허용한다.
- 결과는 구조화 객체로 반환한다. 기준 dBu, 입력값, 환산값을 담는다.
- 값은 double 원값으로 반환한다(반올림 없음).

## 계산식

```
dBFS = dBu − FullScaleDbu
dBu  = FullScaleDbu + dBFS
```

예(기준 +24dBu): +4dBu → −20dBFS, +24dBu → 0dBFS, −20dBFS → +4dBu
거부 예(기준 +24dBu): +25dBu, +0.1dBFS

## 대안

| 안 | 내용 | 판단 |
| --- | --- | --- |
| A | 방향별 Tool 2개(`convert_dbu_to_dbfs`, `convert_dbfs_to_dbu`) | 채택: 사용자 결정, 기존 Vrms↔dBu와 같은 구성 |
| B | dBu → dBFS만 | 기각: 역방향 추가 요구 |
| C | 0dBFS 초과 값도 그대로 반환 | 기각: 사용자 결정(클리핑 거부) |
| D | Vrms ↔ dBFS 직접 환산 | 기각: 요청 범위 밖, 기존 Tool과 조합 가능 |

## 채택 방안

- 기존 `AudioLevelTools`에 Tool 메서드 2개를 추가한다.
- 등록은 이미 `WithTools<AudioLevelTools>()`로 되어 있어 바꾸지 않는다.

## 비범위

- Vrms ↔ dBFS 직접 환산
- 비트 심도·디더·True Peak 등 디지털 특성
- 결과 반올림
- 기존 Vrms↔dBu, amp·speaker Tool 변경
