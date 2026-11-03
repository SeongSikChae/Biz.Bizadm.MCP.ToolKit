---
status: done
category: audio
feature: audio-dbfs
phase: 개발
---

# audio-dbfs 개발

선행: `0005-audio-dbfs-설계.md`

## U1. 결과 record

- **대상**: `src/Biz.Bizadm.MCP.ToolKit/Models/Audio/DbuToDbfsResult.cs` / `DbuToDbfsResult` (신규), `Models/Audio/DbfsToDbuResult.cs` / `DbfsToDbuResult` (신규)
- **코드 초안**:

```csharp
// DbuToDbfsResult.cs
/// <summary>dBu → dBFS 환산 결과</summary>
/// <param name="FullScaleDbu">0dBFS에 해당하는 기준 레벨(dBu)</param>
/// <param name="Dbu">레벨(dBu)</param>
/// <param name="Dbfs">디지털 레벨(dBFS)</param>
public sealed record DbuToDbfsResult(
    double FullScaleDbu,
    double Dbu,
    double Dbfs);

// DbfsToDbuResult.cs — 필드 순서 FullScaleDbu, Dbfs, Dbu
```

- **검증**: `dotnet build src/Biz.Bizadm.MCP.ToolKit` 성공, 경고 0
- **선행 단위**: 없음

## U2. Tool 메서드

- **대상**: `src/Biz.Bizadm.MCP.ToolKit/Tools/AudioLevelTools.cs` / `AudioLevelTools.ConvertDbuToDbfs`, `ConvertDbfsToDbu` (추가, `ConvertDbuToVrms` 뒤·`GetReferenceVrms` 앞)
- **코드 초안**:

```csharp
    // ... existing ConvertDbuToVrms ...

    /// <summary>0dBFS 기준 dBu로 레벨(dBu)을 dBFS로 환산한다.</summary>
    /// ... param/returns/exception 주석 (기존 형식) ...
    [McpServerTool(Name = "convert_dbu_to_dbfs", UseStructuredContent = true,
        ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("0dBFS에 해당하는 기준 dBu로 레벨(dBu)을 dBFS로 환산한다. 0dBFS를 넘는 레벨(클리핑)은 거부한다.")]
    public static DbuToDbfsResult ConvertDbuToDbfs(
        [Description("0dBFS에 해당하는 기준 레벨(dBu), 예: +24")] double fullScaleDbu,
        [Description("환산할 레벨(dBu), fullScaleDbu 이하")] double dbu)
    {
        EnsureFinite(fullScaleDbu, nameof(fullScaleDbu));
        EnsureFinite(dbu, nameof(dbu));
        if (dbu > fullScaleDbu)
            throw new McpException($"{nameof(dbu)} must not exceed {nameof(fullScaleDbu)} (0dBFS): {dbu} > {fullScaleDbu}");

        return new DbuToDbfsResult(fullScaleDbu, dbu, dbu - fullScaleDbu);
    }

    // ConvertDbfsToDbu: EnsureFinite(fullScaleDbu), EnsureFinite(dbfs),
    //   if (dbfs > 0) throw new McpException($"{nameof(dbfs)} must not exceed 0 (0dBFS): {dbfs}");
    //   return new DbfsToDbuResult(fullScaleDbu, dbfs, fullScaleDbu + dbfs);
```

- **검증**: `dotnet build src/Biz.Bizadm.MCP.ToolKit` 성공, 경고 0
- **선행 단위**: U1

## U3. 테스트

- **대상**: `tests/Biz.Bizadm.MCP.ToolKit.Tests/AudioLevelToolsTests.cs` / `AudioLevelToolsTests` (메서드 추가)
- **코드 초안**:

```csharp
    [TestMethod]
    [DataRow(24d, 4d, -20d)]
    [DataRow(24d, 24d, 0d)]
    [DataRow(18d, -2d, -20d)]
    [DataRow(24d, -100d, -124d)]
    public void ConvertDbuToDbfs(double fullScaleDbu, double dbu, double expected)
    {
        DbuToDbfsResult r = AudioLevelTools.ConvertDbuToDbfs(fullScaleDbu, dbu);
        Assert.AreEqual(fullScaleDbu, r.FullScaleDbu);
        Assert.AreEqual(dbu, r.Dbu);
        Assert.AreEqual(expected, r.Dbfs, Delta);
    }

    [TestMethod]
    [DataRow(24d, -20d, 4d)]
    [DataRow(24d, 0d, 24d)]
    [DataRow(18d, -20d, -2d)]
    public void ConvertDbfsToDbu(double fullScaleDbu, double dbfs, double expected) { /* 같은 형식 */ }

    [TestMethod]
    public void Dbfs_RoundTrip()
    {
        double dbfs = AudioLevelTools.ConvertDbuToDbfs(24, 4.5).Dbfs;
        Assert.AreEqual(4.5, AudioLevelTools.ConvertDbfsToDbu(24, dbfs).Dbu, Delta);
    }

    [TestMethod]
    [DataRow(24d, 25d)]                       // 클리핑
    [DataRow(double.NaN, 0d)]
    [DataRow(24d, double.PositiveInfinity)]
    [DataRow(double.PositiveInfinity, 0d)]
    public void ConvertDbuToDbfs_InvalidInput_Throws(double fullScaleDbu, double dbu) { /* McpException */ }

    [TestMethod]
    [DataRow(24d, 0.1)]                       // 클리핑
    [DataRow(double.NaN, -1d)]
    [DataRow(24d, double.NaN)]
    [DataRow(24d, double.NegativeInfinity)]
    public void ConvertDbfsToDbu_InvalidInput_Throws(double fullScaleDbu, double dbfs) { /* McpException */ }

    [TestMethod]
    public void WithBizadmToolKit_RegistersDbfsTools()
    {
        // ... "convert_dbu_to_dbfs", "convert_dbfs_to_dbu" 포함 확인 ...
    }
```

- **검증**: `dotnet test Biz.Bizadm.MCP.ToolKit.slnx` 기존 48건 + 신규 17건(4 + 3 + 1 + 4 + 4 + 1) = 65건 통과
- **선행 단위**: U2

## 완료 기준

- `dotnet build Biz.Bizadm.MCP.ToolKit.slnx` 성공, 경고 0
- `dotnet test Biz.Bizadm.MCP.ToolKit.slnx` 전체 65건 통과 (기존 테스트 회귀 없음)
