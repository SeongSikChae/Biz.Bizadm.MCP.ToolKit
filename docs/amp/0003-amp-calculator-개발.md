---
status: done
category: amp
feature: amp-calculator
phase: 개발
---

# amp-calculator 개발

선행: `0002-amp-calculator-설계.md`

## U1. 결과 record

- **대상**: `src/Biz.Bizadm.MCP.ToolKit/Models/Amp/AmpMaxOutputResult.cs` / `AmpMaxOutputResult`
- **코드 초안**:

```csharp
namespace Biz.Bizadm.MCP.ToolKit.Models.Amp;

/// <summary>앰프 최대 출력 계산 결과</summary>
/// <param name="ImpedanceOhm">스피커 임피던스(Ω)</param>
/// <param name="PowerW">정격 출력(W)</param>
/// <param name="InputSensitivityVrms">입력 감도(Vrms)</param>
/// <param name="InputSensitivityVpp">입력 감도(Vpp)</param>
/// <param name="MaxOutputVrms">최대 출력 전압(Vrms)</param>
/// <param name="MaxOutputVpp">최대 출력 전압(Vpp)</param>
/// <param name="VoltageGainDb">전압 이득(dB)</param>
public sealed record AmpMaxOutputResult(
    double ImpedanceOhm,
    double PowerW,
    double InputSensitivityVrms,
    double InputSensitivityVpp,
    double MaxOutputVrms,
    double MaxOutputVpp,
    double VoltageGainDb);
```

- **검증**: `dotnet build src/Biz.Bizadm.MCP.ToolKit` 성공, 경고 0 (CS1591 포함)
- **선행 단위**: 없음

## U2. Tool 클래스

- **대상**: `src/Biz.Bizadm.MCP.ToolKit/Tools/AmpTools.cs` / `AmpTools.CalculateMaxOutput`, private `EnsurePositiveFinite`
- **코드 초안**:

```csharp
namespace Biz.Bizadm.MCP.ToolKit.Tools;

/// <summary>앰프 계산 Tool</summary>
[McpServerToolType]
public sealed class AmpTools
{
    private static readonly double PeakToPeakPerRms = 2 * Math.Sqrt(2);

    /// <summary>정격 출력과 입력 감도로 최대 출력 전압과 전압 이득을 계산한다.</summary>
    /// <exception cref="McpException">입력이 0 이하이거나 유한수가 아니다.</exception>
    [McpServerTool(Name = "calculate_amp_max_output", UseStructuredContent = true,
        ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("앰프의 임피던스(Ω), 정격 출력(W), 입력 감도(Vrms)로 입력 감도(Vpp), 최대 출력 전압(Vrms/Vpp), 전압 이득(dB)을 계산한다.")]
    public static AmpMaxOutputResult CalculateMaxOutput(
        [Description("스피커 임피던스(Ω)")] double impedanceOhm,
        [Description("정격 출력(W)")] double powerW,
        [Description("입력 감도(Vrms)")] double inputSensitivityVrms)
    {
        EnsurePositiveFinite(impedanceOhm, nameof(impedanceOhm));
        EnsurePositiveFinite(powerW, nameof(powerW));
        EnsurePositiveFinite(inputSensitivityVrms, nameof(inputSensitivityVrms));

        double maxOutputVrms = Math.Sqrt(powerW * impedanceOhm);
        return new AmpMaxOutputResult(
            impedanceOhm,
            powerW,
            inputSensitivityVrms,
            inputSensitivityVrms * PeakToPeakPerRms,
            maxOutputVrms,
            maxOutputVrms * PeakToPeakPerRms,
            20 * Math.Log10(maxOutputVrms / inputSensitivityVrms));
    }

    private static void EnsurePositiveFinite(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new McpException($"{name} must be a positive finite number: {value}");
    }
}
```

- `using`: `System.ComponentModel`, `ModelContextProtocol`, `ModelContextProtocol.Server`, `Biz.Bizadm.MCP.ToolKit.Models.Amp`
- **검증**: `dotnet build src/Biz.Bizadm.MCP.ToolKit` 성공, 경고 0
- **선행 단위**: U1

## U3. 등록

- **대상**: `src/Biz.Bizadm.MCP.ToolKit/BizadmToolKitMcpServerBuilderExtensions.cs` / `WithBizadmToolKit`
- **코드 초안**:

```csharp
ArgumentNullException.ThrowIfNull(builder);
builder.WithTools<AmpTools>();
return builder;
```

- 기존 안내 주석 `// Tool 클래스 추가 시: ...`은 유지한다(다음 Tool 클래스 추가 안내).
- **검증**: `dotnet build` 성공, 기존 테스트 2건 통과
- **선행 단위**: U2

## U4. 테스트

- **대상**: `tests/Biz.Bizadm.MCP.ToolKit.Tests/AmpToolsTests.cs` / `AmpToolsTests`
- **코드 초안**:

```csharp
[TestClass]
public sealed class AmpToolsTests
{
    private const double Delta = 1e-9;

    [TestMethod]
    public void CalculateMaxOutput_8Ohm100W1Vrms()
    {
        AmpMaxOutputResult r = AmpTools.CalculateMaxOutput(8, 100, 1);
        Assert.AreEqual(8, r.ImpedanceOhm);
        Assert.AreEqual(100, r.PowerW);
        Assert.AreEqual(1, r.InputSensitivityVrms);
        Assert.AreEqual(2 * Math.Sqrt(2), r.InputSensitivityVpp, Delta);
        Assert.AreEqual(Math.Sqrt(800), r.MaxOutputVrms, Delta);
        Assert.AreEqual(80, r.MaxOutputVpp, Delta);
        Assert.AreEqual(20 * Math.Log10(Math.Sqrt(800)), r.VoltageGainDb, Delta); // ≈ 29.0309
    }

    [TestMethod]
    [DataRow(0d, 100d, 1d)]
    [DataRow(8d, -1d, 1d)]
    [DataRow(8d, 100d, double.NaN)]
    [DataRow(double.PositiveInfinity, 100d, 1d)]
    public void CalculateMaxOutput_InvalidInput_Throws(double ohm, double w, double vrms)
    {
        Assert.ThrowsExactly<McpException>(() => AmpTools.CalculateMaxOutput(ohm, w, vrms));
    }

    [TestMethod]
    public void WithBizadmToolKit_RegistersCalculateAmpMaxOutput()
    {
        ServiceCollection services = new();
        services.AddMcpServer().WithBizadmToolKit();
        using ServiceProvider provider = services.BuildServiceProvider();
        CollectionAssert.Contains(
            provider.GetServices<McpServerTool>().Select(t => t.ProtocolTool.Name).ToList(),
            "calculate_amp_max_output");
    }
}
```

- **의존성**: `ServiceProvider`/`BuildServiceProvider`는 DI 구현 패키지에 있다(`ModelContextProtocol`은 Abstractions만 전이). 테스트 프로젝트에만 `Microsoft.Extensions.DependencyInjection` 10.0.14를 CPM으로 추가한다(구현 중 사용자 결정). 라이브러리 의존성은 그대로다.

```xml
<!-- Directory.Packages.props -->
<PackageVersion Include="Microsoft.Extensions.DependencyInjection" Version="10.0.14" />
<!-- Tests.csproj -->
<PackageReference Include="Microsoft.Extensions.DependencyInjection" />
```

- **검증**: `dotnet test Biz.Bizadm.MCP.ToolKit.slnx` 기존 2건 + 신규 6건(1 + DataRow 4 + 1) 통과. 테스트 프로젝트 `--vulnerable --include-transitive`, `--deprecated` 이상 없음
- **선행 단위**: U3

## 완료 기준

- `dotnet build Biz.Bizadm.MCP.ToolKit.slnx` 성공, 경고 0
- `dotnet test Biz.Bizadm.MCP.ToolKit.slnx` 전체 통과
