# Biz.Bizadm.MCP.ToolKit

ASP.NET Core MCP Server에 `WithTools`로 등록하는 Tool 모음 클래스 라이브러리입니다.

- 대상 프레임워크: `net10.0`
- 의존 패키지: [`ModelContextProtocol`](https://www.nuget.org/packages/ModelContextProtocol) 2.3.1
- 전송 방식(HTTP, stdio)과 무관하며, 전송 설정은 서버 쪽에서 합니다.

## 사용법

### 패키지 참조

```xml
<PackageReference Include="Biz.Bizadm.MCP.ToolKit" Version="1.0.0" />
```

### 전체 Tool 등록

```csharp
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithBizadmToolKit();
```

`WithBizadmToolKit()`은 `Microsoft.Extensions.DependencyInjection` 네임스페이스에 있어 별도 `using`이 필요 없습니다.

### 개별 Tool 등록

Tool 클래스는 `public`이므로 필요한 것만 골라 등록할 수 있습니다.

```csharp
using Biz.Bizadm.MCP.ToolKit.Tools;

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<XxxTools>();
```

## 제공 Tool

| Tool 클래스 | Tool | 설명 |
| --- | --- | --- |
| - | - | 아직 없음 |

## 빌드·테스트

요구 사항: .NET 10 SDK

```bash
dotnet build Biz.Bizadm.MCP.ToolKit.slnx
```

```bash
dotnet test Biz.Bizadm.MCP.ToolKit.slnx
```

폴더 구조:

```
Biz.Bizadm.MCP.ToolKit.slnx
Directory.Build.props        # 공통 TFM·Nullable·ImplicitUsings
Directory.Packages.props     # 중앙 패키지 버전 관리
src/Biz.Bizadm.MCP.ToolKit/  # 라이브러리
tests/Biz.Bizadm.MCP.ToolKit.Tests/  # MSTest
docs/toolkit/                # 방안·설계·개발 문서
```

## 라이선스

[MIT](LICENSE)
