---
status: done
category: speaker
feature: speaker-spl
phase: 개발
---

# speaker-spl 개발

선행: `0002-speaker-spl-설계.md`

## U1. 음원 유형 enum·결과 record

- **대상**: `src/Biz.Bizadm.MCP.ToolKit/Models/Speaker/SpeakerSourceType.cs` / `SpeakerSourceType` (신규), `src/Biz.Bizadm.MCP.ToolKit/Models/Speaker/SpeakerSplResult.cs` / `SpeakerSplResult` (신규)
- **코드 초안**:

```csharp
// SpeakerSourceType.cs
using System.Text.Json.Serialization;

namespace Biz.Bizadm.MCP.ToolKit.Models.Speaker;

/// <summary>스피커 음원 유형</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SpeakerSourceType>))]
public enum SpeakerSourceType
{
    /// <summary>점음원, 거리 2배당 −6dB (역제곱 법칙)</summary>
    Point,

    /// <summary>선음원(라인어레이), 거리 2배당 −3dB</summary>
    Line,
}
```

```csharp
// SpeakerSplResult.cs
/// <summary>스피커 SPL 계산 결과</summary>
/// <param name="NominalSplDb">Nominal SPL(dB SPL, 1W/1m)</param>
/// <param name="InputPowerW">입력 전력(W)</param>
/// <param name="DistanceM">거리(m)</param>
/// <param name="SourceType">음원 유형</param>
/// <param name="OutputSplDb">Output SPL(dB SPL)</param>
public sealed record SpeakerSplResult(
    double NominalSplDb,
    double InputPowerW,
    double DistanceM,
    SpeakerSourceType SourceType,
    double OutputSplDb);
```

- **검증**: `dotnet build src/Biz.Bizadm.MCP.ToolKit` 성공, 경고 0
- **선행 단위**: 없음

## U2. Tool 메서드·검증 헬퍼

- **대상**: `src/Biz.Bizadm.MCP.ToolKit/Tools/SpeakerTools.cs` / `SpeakerTools.CalculateSpl`, private `EnsurePositiveFinite`, `EnsureFinite` (신규)
- **코드 초안**:

```csharp
/// <summary>스피커 SPL 계산 Tool</summary>
[McpServerToolType]
public sealed class SpeakerTools
{
    /// <summary>Nominal SPL과 입력 전력, 거리, 음원 유형으로 해당 거리의 SPL을 계산한다.</summary>
    /// ... param/returns/exception 주석 (AmpTools 형식) ...
    [McpServerTool(Name = "calculate_speaker_spl", UseStructuredContent = true,
        ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("스피커 Nominal SPL(dB SPL, 1W/1m 감도), 입력 전력(W), 거리(m), 음원 유형(Point/Line)으로 해당 거리의 Output SPL(dB SPL)을 계산한다. Point는 거리 2배당 −6dB, Line은 −3dB 감쇠한다.")]
    public static SpeakerSplResult CalculateSpl(
        [Description("Nominal SPL(dB SPL), 1W/1m 감도")] double nominalSplDb,
        [Description("입력 전력(W), calculate_amp_load_output의 outputPowerW")] double inputPowerW,
        [Description("거리(m)")] double distanceM,
        [Description("음원 유형: Point(점음원, 역제곱) 또는 Line(라인어레이)")] SpeakerSourceType sourceType)
    {
        EnsureFinite(nominalSplDb, nameof(nominalSplDb));
        EnsurePositiveFinite(inputPowerW, nameof(inputPowerW));
        EnsurePositiveFinite(distanceM, nameof(distanceM));
        if (!Enum.IsDefined(sourceType))
            throw new McpException($"{nameof(sourceType)} must be Point or Line: {sourceType}");

        double distanceFactor = sourceType == SpeakerSourceType.Line ? 10 : 20;
        return new SpeakerSplResult(
            nominalSplDb,
            inputPowerW,
            distanceM,
            sourceType,
            nominalSplDb + 10 * Math.Log10(inputPowerW) - distanceFactor * Math.Log10(distanceM));
    }

    // EnsurePositiveFinite, EnsureFinite: AmpTools와 같은 본문
}
```

- **검증**: `dotnet build src/Biz.Bizadm.MCP.ToolKit` 성공, 경고 0
- **선행 단위**: U1

## U3. 등록

- **대상**: `src/Biz.Bizadm.MCP.ToolKit/BizadmToolKitMcpServerBuilderExtensions.cs` / `WithBizadmToolKit`
- **코드 초안**:

```csharp
        builder.WithTools<AmpTools>();
        builder.WithTools<SpeakerTools>();
        return builder;
```

- **검증**: `dotnet build src/Biz.Bizadm.MCP.ToolKit` 성공, 경고 0
- **선행 단위**: U2

## U4. 테스트

- **대상**: `tests/Biz.Bizadm.MCP.ToolKit.Tests/SpeakerToolsTests.cs` / `SpeakerToolsTests` (신규, `AmpToolsTests` 형식)
- **코드 초안**:

```csharp
[TestClass]
public sealed class SpeakerToolsTests
{
    private const double Delta = 1e-9;

    [TestMethod]
    [DataRow(1d, 1d, SpeakerSourceType.Point, 90d)]
    [DataRow(100d, 10d, SpeakerSourceType.Point, 90d)]   // +20 −20
    [DataRow(100d, 10d, SpeakerSourceType.Line, 100d)]   // +20 −10
    [DataRow(1d, 2d, SpeakerSourceType.Point, 90 - 20 * 0.30102999566398120)]
    public void CalculateSpl_90Db(double w, double m, SpeakerSourceType type, double expected)
    {
        SpeakerSplResult r = SpeakerTools.CalculateSpl(90, w, m, type);
        Assert.AreEqual(90, r.NominalSplDb);
        Assert.AreEqual(w, r.InputPowerW);
        Assert.AreEqual(m, r.DistanceM);
        Assert.AreEqual(type, r.SourceType);
        Assert.AreEqual(expected, r.OutputSplDb, Delta);
    }

    [TestMethod]
    [DataRow(double.NaN, 1d, 1d, SpeakerSourceType.Point)]
    [DataRow(90d, 0d, 1d, SpeakerSourceType.Point)]
    [DataRow(90d, 1d, -1d, SpeakerSourceType.Line)]
    [DataRow(90d, 1d, double.PositiveInfinity, SpeakerSourceType.Point)]
    [DataRow(90d, 1d, 1d, (SpeakerSourceType)5)]
    public void CalculateSpl_InvalidInput_Throws(double db, double w, double m, SpeakerSourceType type)
    {
        Assert.ThrowsExactly<McpException>(() => SpeakerTools.CalculateSpl(db, w, m, type));
    }

    [TestMethod]
    public void WithBizadmToolKit_RegistersCalculateSpeakerSpl()
    {
        // AmpToolsTests.WithBizadmToolKit_RegistersCalculateAmpMaxOutput와 같은 형식
        // ... "calculate_speaker_spl" 포함 확인 ...
    }

    [TestMethod]
    public void CalculateSpeakerSpl_InputSchema_SourceTypeIsString()
    {
        // ... WithBizadmToolKit으로 provider 구성 ...
        McpServerTool tool = provider.GetServices<McpServerTool>()
            .Single(t => t.ProtocolTool.Name == "calculate_speaker_spl");
        string schema = tool.ProtocolTool.InputSchema.GetRawText();
        StringAssert.Contains(schema, "\"Point\"");
        StringAssert.Contains(schema, "\"Line\"");
    }
}
```

- **검증**: `dotnet test Biz.Bizadm.MCP.ToolKit.slnx` 기존 전체 + 신규 11건(DataRow 4 + DataRow 5 + 등록 1 + 스키마 1) 통과
- **선행 단위**: U3

## 완료 기준

- `dotnet build Biz.Bizadm.MCP.ToolKit.slnx` 성공, 경고 0
- `dotnet test Biz.Bizadm.MCP.ToolKit.slnx` 전체 통과 (기존 amp 테스트 회귀 없음)
