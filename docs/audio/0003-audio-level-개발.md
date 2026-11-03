---
status: done
category: audio
feature: audio-level
phase: 개발
---

# audio-level 개발

선행: `0002-audio-level-설계.md`

## U1. 기준 enum·결과 record

- **대상**: `src/Biz.Bizadm.MCP.ToolKit/Models/Audio/DbuReference.cs` / `DbuReference` (신규), `Models/Audio/VrmsToDbuResult.cs` / `VrmsToDbuResult` (신규), `Models/Audio/DbuToVrmsResult.cs` / `DbuToVrmsResult` (신규)
- **코드 초안**:

```csharp
// DbuReference.cs
/// <summary>0dBu 기준 전압</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DbuReference>))]
public enum DbuReference
{
    /// <summary>0dBu = 0.775Vrms (실무 관행값)</summary>
    Nominal,

    /// <summary>0dBu = √0.6 ≈ 0.7745967Vrms (1mW @ 600Ω 정의값)</summary>
    Exact,
}
```

```csharp
// VrmsToDbuResult.cs
/// <summary>Vrms → dBu 환산 결과</summary>
/// <param name="Vrms">전압(Vrms)</param>
/// <param name="Reference">0dBu 기준</param>
/// <param name="ReferenceVrms">0dBu 기준 전압(Vrms)</param>
/// <param name="Dbu">레벨(dBu)</param>
public sealed record VrmsToDbuResult(double Vrms, DbuReference Reference, double ReferenceVrms, double Dbu);

// DbuToVrmsResult.cs — 필드 순서 Dbu, Reference, ReferenceVrms, Vrms
```

- **검증**: `dotnet build src/Biz.Bizadm.MCP.ToolKit` 성공, 경고 0
- **선행 단위**: 없음

## U2. Tool 메서드·헬퍼

- **대상**: `src/Biz.Bizadm.MCP.ToolKit/Tools/AudioLevelTools.cs` / `AudioLevelTools.ConvertVrmsToDbu`, `ConvertDbuToVrms`, private `GetReferenceVrms`, `EnsurePositiveFinite`, `EnsureFinite` (신규)
- **코드 초안**:

```csharp
/// <summary>오디오 레벨 단위 환산 Tool</summary>
[McpServerToolType]
public sealed class AudioLevelTools
{
    [McpServerTool(Name = "convert_vrms_to_dbu", UseStructuredContent = true,
        ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("전압(Vrms)을 dBu로 환산한다. 0dBu 기준은 Nominal(0.775V, 기본) 또는 Exact(√0.6V)이다.")]
    public static VrmsToDbuResult ConvertVrmsToDbu(
        [Description("전압(Vrms)")] double vrms,
        [Description("0dBu 기준: Nominal(0.775V, 기본) 또는 Exact(√0.6V)")] DbuReference reference = DbuReference.Nominal)
    {
        EnsurePositiveFinite(vrms, nameof(vrms));
        double referenceVrms = GetReferenceVrms(reference);
        return new VrmsToDbuResult(vrms, reference, referenceVrms, 20 * Math.Log10(vrms / referenceVrms));
    }

    // ConvertDbuToVrms: EnsureFinite(dbu) → referenceVrms * Math.Pow(10, dbu / 20)

    private static double GetReferenceVrms(DbuReference reference) => reference switch
    {
        DbuReference.Nominal => 0.775,
        DbuReference.Exact => Math.Sqrt(0.6),
        _ => throw new McpException($"{nameof(reference)} must be Nominal or Exact: {reference}"),
    };

    // ... EnsurePositiveFinite, EnsureFinite: SpeakerTools와 같은 형태 ...
}
```

- **검증**: `dotnet build src/Biz.Bizadm.MCP.ToolKit` 성공, 경고 0
- **선행 단위**: U1

## U3. 등록

- **대상**: `src/Biz.Bizadm.MCP.ToolKit/BizadmToolKitMcpServerBuilderExtensions.cs` / `WithBizadmToolKit`
- **코드 초안**:

```csharp
        builder.WithTools<SpeakerTools>();
        builder.WithTools<AudioLevelTools>();
        return builder;
```

- **검증**: `dotnet build src/Biz.Bizadm.MCP.ToolKit` 성공, 경고 0
- **선행 단위**: U2

## U4. 테스트

- **대상**: `tests/Biz.Bizadm.MCP.ToolKit.Tests/AudioLevelToolsTests.cs` / `AudioLevelToolsTests` (신규, `SpeakerToolsTests` 형식)
- **코드 초안**:

```csharp
[TestClass]
public sealed class AudioLevelToolsTests
{
    private const double Delta = 1e-9;

    [TestMethod]
    [DataRow(0.775, 0d)]
    [DataRow(7.75, 20d)]
    [DataRow(0.0775, -20d)]
    public void ConvertVrmsToDbu_Nominal(double vrms, double expected)
    {
        VrmsToDbuResult r = AudioLevelTools.ConvertVrmsToDbu(vrms);
        Assert.AreEqual(vrms, r.Vrms);
        Assert.AreEqual(DbuReference.Nominal, r.Reference);
        Assert.AreEqual(0.775, r.ReferenceVrms);
        Assert.AreEqual(expected, r.Dbu, Delta);
    }

    [TestMethod]
    [DataRow(0d, 0.775)]
    [DataRow(20d, 7.75)]
    [DataRow(-20d, 0.0775)]
    [DataRow(4d, 1.2282922241573626)] // 0.775·10^0.2
    public void ConvertDbuToVrms_Nominal(double dbu, double expected) { /* 같은 형식 */ }

    [TestMethod]
    public void Exact_UsesSqrtPoint6()
    {
        Assert.AreEqual(0, AudioLevelTools.ConvertVrmsToDbu(Math.Sqrt(0.6), DbuReference.Exact).Dbu, Delta);
        Assert.AreEqual(Math.Sqrt(0.6), AudioLevelTools.ConvertDbuToVrms(0, DbuReference.Exact).Vrms, Delta);
        Assert.AreEqual(0.004521, AudioLevelTools.ConvertVrmsToDbu(0.775, DbuReference.Exact).Dbu, 1e-5);
    }

    [TestMethod]
    [DataRow(DbuReference.Nominal)]
    [DataRow(DbuReference.Exact)]
    public void RoundTrip(DbuReference reference)
    {
        double dbu = AudioLevelTools.ConvertVrmsToDbu(1.228, reference).Dbu;
        Assert.AreEqual(1.228, AudioLevelTools.ConvertDbuToVrms(dbu, reference).Vrms, Delta);
    }

    [TestMethod]
    [DataRow(0d, DbuReference.Nominal)]
    [DataRow(-1d, DbuReference.Nominal)]
    [DataRow(double.NaN, DbuReference.Exact)]
    [DataRow(double.PositiveInfinity, DbuReference.Nominal)]
    [DataRow(1d, (DbuReference)5)]
    public void ConvertVrmsToDbu_InvalidInput_Throws(double vrms, DbuReference reference) { /* McpException */ }

    [TestMethod]
    [DataRow(double.NaN, DbuReference.Nominal)]
    [DataRow(double.NegativeInfinity, DbuReference.Exact)]
    [DataRow(0d, (DbuReference)5)]
    public void ConvertDbuToVrms_InvalidInput_Throws(double dbu, DbuReference reference) { /* McpException */ }

    [TestMethod]
    public void WithBizadmToolKit_RegistersAudioLevelTools()
    {
        // ... "convert_vrms_to_dbu", "convert_dbu_to_vrms" 포함 확인 ...
    }

    [TestMethod]
    [DataRow("convert_vrms_to_dbu")]
    [DataRow("convert_dbu_to_vrms")]
    public void InputSchema_ReferenceIsString(string toolName)
    {
        // ... 스키마에 "\"Nominal\"", "\"Exact\"" 포함 확인 ...
    }
}
```

- **검증**: `dotnet test Biz.Bizadm.MCP.ToolKit.slnx` 기존 전체 + 신규 21건(3 + 4 + 1 + 2 + 5 + 3 + 1 + 2) 통과
- **선행 단위**: U3

## 완료 기준

- `dotnet build Biz.Bizadm.MCP.ToolKit.slnx` 성공, 경고 0
- `dotnet test Biz.Bizadm.MCP.ToolKit.slnx` 전체 통과 (기존 amp·speaker 테스트 회귀 없음)
