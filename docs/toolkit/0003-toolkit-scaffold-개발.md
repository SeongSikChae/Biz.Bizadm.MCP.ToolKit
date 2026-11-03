---
status: done
category: toolkit
feature: toolkit-scaffold
phase: 개발
---

# toolkit-scaffold 개발

선행: `0002-toolkit-scaffold-설계.md`

## U1. 루트 공통 설정

- **대상**: `Directory.Build.props`, `Directory.Packages.props`
- **코드 초안**:

```xml
<!-- Directory.Build.props -->
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
</PropertyGroup>

<!-- Directory.Packages.props -->
<PropertyGroup>
  <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
</PropertyGroup>
<ItemGroup>
  <PackageVersion Include="ModelContextProtocol" Version="2.3.1" />
  <PackageVersion Include="MSTest" Version="4.5.2" />
</ItemGroup>
```

- **검증**: 두 파일이 XML로 유효함 (U2 빌드에서 함께 확인)
- **선행 단위**: 없음

## U2. 라이브러리 프로젝트

- **대상**: `src/Biz.Bizadm.MCP.ToolKit/Biz.Bizadm.MCP.ToolKit.csproj`, `.slnx`
- **방법**: `dotnet new classlib -n Biz.Bizadm.MCP.ToolKit -f net10.0 -o src/Biz.Bizadm.MCP.ToolKit` → 템플릿 `Class1.cs` 삭제 → `.csproj`에서 Directory.Build.props와 중복되는 속성 제거 → `dotnet sln add`
- **코드 초안**:

```xml
<PropertyGroup>
  <PackageId>Biz.Bizadm.MCP.ToolKit</PackageId>
  <Version>1.0.0</Version>
  <Authors>SeongSikChae</Authors>
  <Description>ASP.NET Core MCP Server에 WithTools로 등록하는 Tool 모음</Description>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
  <RepositoryUrl>https://github.com/SeongSikChae/Biz.Bizadm.MCP.ToolKit</RepositoryUrl>
  <RepositoryType>git</RepositoryType>
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="ModelContextProtocol" />
</ItemGroup>
```

- **검증**: `dotnet build src/Biz.Bizadm.MCP.ToolKit` 성공. `dotnet package list --vulnerable --include-transitive`, `--deprecated` 각각 이상 없음
- **선행 단위**: U1

## U3. 확장 메서드

- **대상**: `src/Biz.Bizadm.MCP.ToolKit/BizadmToolKitMcpServerBuilderExtensions.cs`, `Tools/` 폴더
- **코드 초안**:

```csharp
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Biz.Bizadm.MCP.ToolKit의 Tool을 MCP Server에 등록하는 확장 메서드</summary>
public static class BizadmToolKitMcpServerBuilderExtensions // SDK McpServerBuilderExtensions와 이름 충돌 회피
{
    /// <summary>Biz.Bizadm.MCP.ToolKit의 모든 Tool을 등록한다.</summary>
    public static IMcpServerBuilder WithBizadmToolKit(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        // Tool 클래스 추가 시: builder.WithTools<XxxTools>();
        return builder;
    }
}
```

- `Tools/`는 빈 폴더라 git에 남지 않는다. Tool 첫 추가 시 생성한다(지금은 만들지 않음).
- **검증**: `dotnet build` 경고 0 (CS1591 포함)
- **선행 단위**: U2

## U4. 테스트 프로젝트

- **대상**: `tests/Biz.Bizadm.MCP.ToolKit.Tests/`
- **방법**: `dotnet new mstest -n Biz.Bizadm.MCP.ToolKit.Tests -f net10.0 -o tests/Biz.Bizadm.MCP.ToolKit.Tests` → `PackageReference` 버전을 CPM으로 이동 → 템플릿 속성(`TargetFramework`·`Nullable`·`ImplicitUsings`·`LangVersion`) 제거 → `Test1.cs`만 삭제(`MSTestSettings.cs` 유지) → `dotnet sln add`, 라이브러리 참조 추가
- **대상 파일**: `BizadmToolKitMcpServerBuilderExtensionsTests.cs`
- **코드 초안**:

```csharp
[TestClass]
public sealed class BizadmToolKitMcpServerBuilderExtensionsTests
{
    [TestMethod]
    public void WithBizadmToolKit_ReturnsSameBuilder()
    {
        IMcpServerBuilder builder = new ServiceCollection().AddMcpServer();
        Assert.AreSame(builder, builder.WithBizadmToolKit());
    }

    [TestMethod]
    public void WithBizadmToolKit_NullBuilder_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => ((IMcpServerBuilder)null!).WithBizadmToolKit());
    }
}
```

- **검증**: `dotnet test Biz.Bizadm.MCP.ToolKit.slnx` 2건 통과, 테스트 프로젝트 패키지 vulnerable/deprecated 확인
- **선행 단위**: U3

## 완료 기준

- `dotnet build Biz.Bizadm.MCP.ToolKit.slnx` 성공, 경고 0
- `dotnet test` 통과
- 패키지 vulnerable/deprecated 이상 없음
