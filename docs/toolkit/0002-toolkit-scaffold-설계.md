---
status: done
category: toolkit
feature: toolkit-scaffold
phase: 설계
---

# toolkit-scaffold 설계

선행: `0001-toolkit-scaffold-방안.md`

## 구조

```
Biz.Bizadm.MCP.ToolKit.slnx
Directory.Build.props
Directory.Packages.props
src/Biz.Bizadm.MCP.ToolKit/
  Biz.Bizadm.MCP.ToolKit.csproj
  BizadmToolKitMcpServerBuilderExtensions.cs
  Tools/                          # 이후 [McpServerToolType] public 클래스 추가 위치
tests/Biz.Bizadm.MCP.ToolKit.Tests/
  Biz.Bizadm.MCP.ToolKit.Tests.csproj
  BizadmToolKitMcpServerBuilderExtensionsTests.cs
```

참조: `Tests` → `Biz.Bizadm.MCP.ToolKit`

## 인터페이스

```csharp
namespace Microsoft.Extensions.DependencyInjection;

public static class BizadmToolKitMcpServerBuilderExtensions
{
    public static IMcpServerBuilder WithBizadmToolKit(this IMcpServerBuilder builder);
}
```

- 네임스페이스는 `Microsoft.Extensions.DependencyInjection` (MCP SDK 확장과 같은 관례). 서버는 using 추가 없이 사용한다.
- 클래스 이름은 `BizadmToolKitMcpServerBuilderExtensions`다. `ModelContextProtocol` 2.3.1에 public `Microsoft.Extensions.DependencyInjection.McpServerBuilderExtensions`가 있어 같은 이름을 쓰면 전체 이름이 충돌한다(정적 호출·`cref`에서 CS0433).
- 내부는 **명시 등록**이다. Tool 클래스를 추가할 때마다 `builder.WithTools<XxxTools>();`를 한 줄 추가한다. 어셈블리 스캔은 쓰지 않는다.
- Tool 클래스는 `public`이며 `Biz.Bizadm.MCP.ToolKit.Tools` 네임스페이스에 둔다. 서버는 개별 `.WithTools<XxxTools>()` 등록도 할 수 있다.
- `builder`가 null이면 `ArgumentNullException`.

서버 사용 예:

```csharp
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithBizadmToolKit();          // 전체
    // .WithTools<XxxTools>();     // 또는 개별
```

## 의존성 (CPM)

| 패키지 | 버전 | 사용처 |
| --- | --- | --- |
| `ModelContextProtocol` | 2.3.1 | 라이브러리 |
| `MSTest` | 4.5.2 | 테스트 |

설치 후 `--vulnerable --include-transitive`, `--deprecated`를 각각 확인한다.

## 빌드·패키지 설정

`Directory.Build.props`: `TargetFramework` net10.0, `Nullable` enable, `ImplicitUsings` enable

라이브러리 `.csproj`:

| 속성 | 값 |
| --- | --- |
| `PackageId` | `Biz.Bizadm.MCP.ToolKit` |
| `Version` | `1.0.0` |
| `Authors` | `SeongSikChae` |
| `Description` | ASP.NET Core MCP Server에 WithTools로 등록하는 Tool 모음 |
| `PackageLicenseExpression` | `MIT` |
| `RepositoryUrl` | `https://github.com/SeongSikChae/Biz.Bizadm.MCP.ToolKit` |
| `RepositoryType` | `git` |
| `GenerateDocumentationFile` | `true` (라이브러리만) |

public API에는 `///` XML 주석을 단다.

## 결정 목록

1. 단일 라이브러리 + MSTest 테스트
2. `ModelContextProtocol`만 참조, 전송 방식 무관
3. 확장 메서드 `WithBizadmToolKit`, 클래스 `BizadmToolKitMcpServerBuilderExtensions`, 네임스페이스 `Microsoft.Extensions.DependencyInjection` (검토 회귀로 클래스 이름 변경)
4. 확장 메서드 내부는 명시 등록
5. Tool 클래스 public, 개별 `WithTools<T>()` 허용
6. 패키지 메타 위 표 값, XML 문서 파일 생성
