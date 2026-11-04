using Biz.Bizadm.MCP.ToolKit.Models.Audio;
using Biz.Bizadm.MCP.ToolKit.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Biz.Bizadm.MCP.ToolKit.Tests;

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
    public void ConvertDbuToVrms_Nominal(double dbu, double expected)
    {
        DbuToVrmsResult r = AudioLevelTools.ConvertDbuToVrms(dbu);
        Assert.AreEqual(dbu, r.Dbu);
        Assert.AreEqual(DbuReference.Nominal, r.Reference);
        Assert.AreEqual(0.775, r.ReferenceVrms);
        Assert.AreEqual(expected, r.Vrms, Delta);
    }

    [TestMethod]
    public void Exact_UsesSqrtPoint6()
    {
        Assert.AreEqual(Math.Sqrt(0.6), AudioLevelTools.ConvertVrmsToDbu(1, DbuReference.Exact).ReferenceVrms);
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
    public void ConvertVrmsToDbu_InvalidInput_Throws(double vrms, DbuReference reference)
    {
        Assert.ThrowsExactly<McpException>(() => AudioLevelTools.ConvertVrmsToDbu(vrms, reference));
    }

    [TestMethod]
    [DataRow(double.NaN, DbuReference.Nominal)]
    [DataRow(double.NegativeInfinity, DbuReference.Exact)]
    [DataRow(0d, (DbuReference)5)]
    public void ConvertDbuToVrms_InvalidInput_Throws(double dbu, DbuReference reference)
    {
        Assert.ThrowsExactly<McpException>(() => AudioLevelTools.ConvertDbuToVrms(dbu, reference));
    }

    [TestMethod]
    public void WithTools_RegistersAudioLevelTools()
    {
        ServiceCollection services = new();
        services.AddMcpServer().WithTools<AudioLevelTools>();
        using ServiceProvider provider = services.BuildServiceProvider();
        List<string> names = provider.GetServices<McpServerTool>().Select(t => t.ProtocolTool.Name).ToList();
        CollectionAssert.Contains(names, "convert_vrms_to_dbu");
        CollectionAssert.Contains(names, "convert_dbu_to_vrms");
    }

    [TestMethod]
    [DataRow("convert_vrms_to_dbu")]
    [DataRow("convert_dbu_to_vrms")]
    public void InputSchema_ReferenceIsString(string toolName)
    {
        ServiceCollection services = new();
        services.AddMcpServer().WithTools<AudioLevelTools>();
        using ServiceProvider provider = services.BuildServiceProvider();
        McpServerTool tool = provider.GetServices<McpServerTool>()
            .Single(t => t.ProtocolTool.Name == toolName);
        string schema = tool.ProtocolTool.InputSchema.GetRawText();
        StringAssert.Contains(schema, "\"Nominal\"");
        StringAssert.Contains(schema, "\"Exact\"");
    }

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
    public void ConvertDbfsToDbu(double fullScaleDbu, double dbfs, double expected)
    {
        DbfsToDbuResult r = AudioLevelTools.ConvertDbfsToDbu(fullScaleDbu, dbfs);
        Assert.AreEqual(fullScaleDbu, r.FullScaleDbu);
        Assert.AreEqual(dbfs, r.Dbfs);
        Assert.AreEqual(expected, r.Dbu, Delta);
    }

    [TestMethod]
    public void Dbfs_RoundTrip()
    {
        double dbfs = AudioLevelTools.ConvertDbuToDbfs(24, 4.5).Dbfs;
        Assert.AreEqual(4.5, AudioLevelTools.ConvertDbfsToDbu(24, dbfs).Dbu, Delta);
    }

    [TestMethod]
    [DataRow(24d, 25d)] // 클리핑
    [DataRow(double.NaN, 0d)]
    [DataRow(24d, double.PositiveInfinity)]
    [DataRow(double.PositiveInfinity, 0d)]
    public void ConvertDbuToDbfs_InvalidInput_Throws(double fullScaleDbu, double dbu)
    {
        Assert.ThrowsExactly<McpException>(() => AudioLevelTools.ConvertDbuToDbfs(fullScaleDbu, dbu));
    }

    [TestMethod]
    [DataRow(24d, 0.1)] // 클리핑
    [DataRow(double.NaN, -1d)]
    [DataRow(24d, double.NaN)]
    [DataRow(24d, double.NegativeInfinity)]
    public void ConvertDbfsToDbu_InvalidInput_Throws(double fullScaleDbu, double dbfs)
    {
        Assert.ThrowsExactly<McpException>(() => AudioLevelTools.ConvertDbfsToDbu(fullScaleDbu, dbfs));
    }

    [TestMethod]
    public void WithTools_RegistersDbfsTools()
    {
        ServiceCollection services = new();
        services.AddMcpServer().WithTools<AudioLevelTools>();
        using ServiceProvider provider = services.BuildServiceProvider();
        List<string> names = provider.GetServices<McpServerTool>().Select(t => t.ProtocolTool.Name).ToList();
        CollectionAssert.Contains(names, "convert_dbu_to_dbfs");
        CollectionAssert.Contains(names, "convert_dbfs_to_dbu");
    }
}
