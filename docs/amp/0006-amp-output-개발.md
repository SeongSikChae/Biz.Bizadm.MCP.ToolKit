---
status: done
category: amp
feature: amp-output
phase: 개발
---

# amp-output 개발

선행: `0005-amp-output-설계.md`

## U1. 결과 record

- **대상**: `src/Biz.Bizadm.MCP.ToolKit/Models/Amp/AmpLoadOutputResult.cs` / `AmpLoadOutputResult` (신규)
- **코드 초안**:

```csharp
/// <summary>앰프 부하 출력 계산 결과</summary>
/// <param name="InputVpp">입력 전압(Vpp)</param>
/// <param name="InputVrms">입력 전압(Vrms)</param>
/// <param name="InputAttenuationDb">입력 감쇠(dB)</param>
/// <param name="EffectiveVpp">실질 전압(Vpp)</param>
/// <param name="EffectiveVrms">실질 전압(Vrms)</param>
/// <param name="ImpedanceOhm">스피커 임피던스(Ω)</param>
/// <param name="OutputVpp">출력 전압(Vpp)</param>
/// <param name="OutputVrms">출력 전압(Vrms)</param>
/// <param name="OutputPowerW">출력 전력(W)</param>
public sealed record AmpLoadOutputResult(
    double InputVpp,
    double InputVrms,
    double InputAttenuationDb,
    double EffectiveVpp,
    double EffectiveVrms,
    double ImpedanceOhm,
    double OutputVpp,
    double OutputVrms,
    double OutputPowerW);
```

- 네임스페이스 `Biz.Bizadm.MCP.ToolKit.Models.Amp` (file-scoped, `AmpMaxOutputResult.cs`와 동일)
- **검증**: `dotnet build src/Biz.Bizadm.MCP.ToolKit` 성공, 경고 0
- **선행 단위**: 없음

## U2. Tool 메서드·검증 헬퍼

- **대상**: `src/Biz.Bizadm.MCP.ToolKit/Tools/AmpTools.cs` / `AmpTools.CalculateLoadOutput`, private `EnsureNonNegativeFinite`, `EnsureFinite`
- **코드 초안**:

```csharp
    // ... CalculateMaxOutput (변경 없음) ...

    /// <summary>입력 전압과 감쇠, 앰프 이득으로 부하에 걸리는 출력 전압과 전력을 계산한다.</summary>
    /// <param name="inputVrms">입력 전압(Vrms)</param>
    /// <param name="inputAttenuationDb">입력 감쇠(dB), 양수가 감쇠</param>
    /// <param name="impedanceOhm">스피커 임피던스(Ω)</param>
    /// <param name="voltageGainDb">앰프 전압 이득(dB)</param>
    /// <param name="maxOutputVrms">앰프 최대 출력 전압(Vrms)</param>
    /// <returns>입력·실질·출력 전압(Vrms/Vpp), 입력 감쇠(dB), 임피던스, 출력 전력(W)</returns>
    /// <exception cref="McpException">입력이 허용 범위 밖이거나 유한수가 아니다.</exception>
    [McpServerTool(Name = "calculate_amp_load_output", UseStructuredContent = true,
        ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("입력 전압(Vrms), 입력 감쇠(dB), 스피커 임피던스(Ω), 앰프 전압 이득(dB), 최대 출력 전압(Vrms)으로 실질 입력 전압, 출력 전압(Vrms/Vpp), 출력 전력(W)을 계산한다. 출력 전압은 최대 출력 전압으로 제한한다.")]
    public static AmpLoadOutputResult CalculateLoadOutput(
        [Description("입력 전압(Vrms)")] double inputVrms,
        [Description("입력 감쇠(dB), 0 이상, 양수가 감쇠")] double inputAttenuationDb,
        [Description("스피커 임피던스(Ω)")] double impedanceOhm,
        [Description("앰프 전압 이득(dB), calculate_amp_max_output의 voltageGainDb")] double voltageGainDb,
        [Description("앰프 최대 출력 전압(Vrms), calculate_amp_max_output의 maxOutputVrms")] double maxOutputVrms)
    {
        EnsurePositiveFinite(inputVrms, nameof(inputVrms));
        EnsureNonNegativeFinite(inputAttenuationDb, nameof(inputAttenuationDb));
        EnsurePositiveFinite(impedanceOhm, nameof(impedanceOhm));
        EnsureFinite(voltageGainDb, nameof(voltageGainDb));
        EnsurePositiveFinite(maxOutputVrms, nameof(maxOutputVrms));

        double effectiveVrms = inputVrms * Math.Pow(10, -inputAttenuationDb / 20);
        double outputVrms = Math.Min(effectiveVrms * Math.Pow(10, voltageGainDb / 20), maxOutputVrms);
        return new AmpLoadOutputResult(
            inputVrms * PeakToPeakPerRms,
            inputVrms,
            inputAttenuationDb,
            effectiveVrms * PeakToPeakPerRms,
            effectiveVrms,
            impedanceOhm,
            outputVrms * PeakToPeakPerRms,
            outputVrms,
            outputVrms * outputVrms / impedanceOhm);
    }

    // ... EnsurePositiveFinite (변경 없음) ...

    private static void EnsureNonNegativeFinite(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new McpException($"{name} must be a non-negative finite number: {value}");
    }

    private static void EnsureFinite(double value, string name)
    {
        if (!double.IsFinite(value))
            throw new McpException($"{name} must be a finite number: {value}");
    }
```

- **검증**: `dotnet build src/Biz.Bizadm.MCP.ToolKit` 성공, 경고 0
- **선행 단위**: U1

## U3. 테스트

- **대상**: `tests/Biz.Bizadm.MCP.ToolKit.Tests/AmpToolsTests.cs` / `AmpToolsTests` (메서드 추가, 기존 테스트 변경 없음)
- **코드 초안**:

```csharp
    [TestMethod]
    public void CalculateLoadOutput_1Vrms6Db_8Ohm100W1VrmsAmp()
    {
        AmpMaxOutputResult amp = AmpTools.CalculateMaxOutput(8, 100, 1);
        AmpLoadOutputResult r = AmpTools.CalculateLoadOutput(1, 6, 8, amp.VoltageGainDb, amp.MaxOutputVrms);
        double effectiveVrms = Math.Pow(10, -6d / 20); // ≈ 0.5012
        double outputVrms = effectiveVrms * Math.Sqrt(800); // ≈ 14.176
        Assert.AreEqual(2 * Math.Sqrt(2), r.InputVpp, Delta);
        Assert.AreEqual(1, r.InputVrms);
        Assert.AreEqual(6, r.InputAttenuationDb);
        Assert.AreEqual(effectiveVrms * 2 * Math.Sqrt(2), r.EffectiveVpp, Delta);
        Assert.AreEqual(effectiveVrms, r.EffectiveVrms, Delta);
        Assert.AreEqual(8, r.ImpedanceOhm);
        Assert.AreEqual(outputVrms * 2 * Math.Sqrt(2), r.OutputVpp, Delta);
        Assert.AreEqual(outputVrms, r.OutputVrms, Delta);
        Assert.AreEqual(outputVrms * outputVrms / 8, r.OutputPowerW, Delta); // ≈ 25.12
    }

    [TestMethod]
    public void CalculateLoadOutput_ExceedsMaxOutput_Clips()
    {
        AmpLoadOutputResult r = AmpTools.CalculateLoadOutput(1, 0, 8, 40, Math.Sqrt(800));
        Assert.AreEqual(1, r.EffectiveVrms, Delta);
        Assert.AreEqual(Math.Sqrt(800), r.OutputVrms, Delta);
        Assert.AreEqual(80, r.OutputVpp, Delta);
        Assert.AreEqual(100, r.OutputPowerW, Delta);
    }

    [TestMethod]
    [DataRow(0d, 6d, 8d, 29d, 28d)]
    [DataRow(1d, -1d, 8d, 29d, 28d)]
    [DataRow(1d, 6d, double.PositiveInfinity, 29d, 28d)]
    [DataRow(1d, 6d, 8d, double.NaN, 28d)]
    [DataRow(1d, 6d, 8d, 29d, 0d)]
    public void CalculateLoadOutput_InvalidInput_Throws(double vrms, double att, double ohm, double gain, double max)
    {
        Assert.ThrowsExactly<McpException>(() => AmpTools.CalculateLoadOutput(vrms, att, ohm, gain, max));
    }

    [TestMethod]
    public void WithBizadmToolKit_RegistersCalculateAmpLoadOutput()
    {
        ServiceCollection services = new();
        services.AddMcpServer().WithBizadmToolKit();
        using ServiceProvider provider = services.BuildServiceProvider();
        CollectionAssert.Contains(
            provider.GetServices<McpServerTool>().Select(t => t.ProtocolTool.Name).ToList(),
            "calculate_amp_load_output");
    }
```

- **검증**: `dotnet test Biz.Bizadm.MCP.ToolKit.slnx` 기존 전체 + 신규 8건(1 + 1 + DataRow 5 + 1) 통과
- **선행 단위**: U2

## 완료 기준

- `dotnet build Biz.Bizadm.MCP.ToolKit.slnx` 성공, 경고 0
- `dotnet test Biz.Bizadm.MCP.ToolKit.slnx` 전체 통과 (기존 `calculate_amp_max_output` 테스트 회귀 없음)
