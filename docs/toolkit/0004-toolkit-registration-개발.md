---
status: done
category: toolkit
feature: toolkit-registration
phase: 개발
---

# toolkit-registration 개발

단축(바로 구현): 방안·설계를 생략한다.

## 요구

- `BizadmToolKitMcpServerBuilderExtensions`(`WithBizadmToolKit()`)를 제거한다. Tool을 한꺼번에 등록할 필요가 없어서다(사용자 요구).
- 호스트는 필요한 Tool 클래스를 `WithTools<XxxTools>()`로 골라 등록한다.
- `AgentResourceMcpServerBuilderExtensions.WithAgentResourceTools(...)`는 유지한다(사용자 결정). 옵션 검증과 DI 등록을 맡기 때문이다.
- 과거 방안·설계·개발 문서는 당시 기록이므로 고치지 않는다.

## 단위 1. 확장 메서드 제거와 참조 정리

**대상**
- `src/Biz.Bizadm.MCP.ToolKit/BizadmToolKitMcpServerBuilderExtensions.cs`: 삭제
- `tests/Biz.Bizadm.MCP.ToolKit.Tests/BizadmToolKitMcpServerBuilderExtensionsTests.cs`: 삭제(메서드 자체 테스트)
- `src/.../AgentResourceMcpServerBuilderExtensions.cs`: XML 주석의 `WithBizadmToolKit` cref 제거
- 테스트 `AmpToolsTests`, `SpeakerToolsTests`, `AudioLevelToolsTests`, `StorageToolsTests`: `WithBizadmToolKit()`을 해당 Tool 클래스의 `WithTools<T>()`로 교체하고, 테스트 이름의 `WithBizadmToolKit_` 접두어를 `WithTools_`로 바꾼다
- `AgentResourceRegistrationTests.WithBizadmToolKit_DoesNotRegisterAgentResourceTools`: 삭제(대상 메서드가 없어짐)
- `ReadMe.md`: "전체 Tool 등록" 절을 삭제하고 "개별 Tool 등록"만 남긴다

**코드 초안**

```csharp
// StorageToolsTests.cs (다른 Tool 테스트도 같은 방식)
[TestMethod]
public void WithTools_RegistersStorageTools()
{
    ServiceCollection services = new();
    services.AddMcpServer().WithTools<StorageTools>();
    using ServiceProvider provider = services.BuildServiceProvider();
    // ... existing assertions ...
}
```

```csharp
// AgentResourceMcpServerBuilderExtensions.cs
/// <summary>AgentResource Tool(rule·skill·tool·docs 4개 클래스, 21개)을 옵션과 함께 등록한다.</summary>
```

**검증**
- `dotnet build Biz.Bizadm.MCP.ToolKit.slnx --no-incremental`: 경고 0개, 오류 0개
- `dotnet test Biz.Bizadm.MCP.ToolKit.slnx`: 실패 0개. 테스트 수는 삭제한 3개(확장 메서드 테스트 2개, AgentResource 비등록 테스트 1개)만큼 줄어든다
- src·tests·ReadMe에서 `WithBizadmToolKit`를 Grep하면 결과가 0건

**선행 단위**: 없음

## 순서

1
