---
status: done
category: toolkit
feature: toolkit-scaffold
phase: 방안
---

# toolkit-scaffold 방안

## 문제

ASP.NET Core MCP Server 프로젝트마다 Tool 클래스를 직접 두면 여러 서버에서 재사용하거나 배포하기 어렵다.

## 목표

- `WithTools`로 등록할 Tool 클래스만 담는 net10.0 클래스 라이브러리를 만든다.
- NuGet 패키지로 배포할 수 있게 패키지 메타를 포함한다.
- 서버는 라이브러리가 제공하는 확장 메서드 한 줄로 전체 Tool을 등록하거나, 개별 `WithTools<T>()`로 필요한 Tool만 등록할 수 있다.

## 대안

| 안 | 내용 | 판단 |
| --- | --- | --- |
| A | `ModelContextProtocol` 패키지 참조 classlib | 채택 |
| B | `ModelContextProtocol.Core`만 참조 | 기각: `IMcpServerBuilder` 확장을 제공할 수 없음 |
| C | `ModelContextProtocol.AspNetCore` 참조 | 기각: 전송 방식(HTTP) 의존은 서버 책임 |

## 채택 방안

- 프로젝트: `src/Biz.Bizadm.MCP.ToolKit`(classlib) + `tests/Biz.Bizadm.MCP.ToolKit.Tests`(MSTest)
- 의존: `ModelContextProtocol`만 참조한다. 라이브러리는 전송 방식과 무관하다.
- 등록: 확장 메서드 제공 + Tool 클래스를 `public`으로 두어 개별 `WithTools<T>()`도 허용한다.
- 루트: `Directory.Build.props`, `Directory.Packages.props`(CPM)

## 비범위

- 실제 Tool 구현 (별도 요청)
- MCP Server 프로젝트 생성
- `dotnet pack` 실행, CI
