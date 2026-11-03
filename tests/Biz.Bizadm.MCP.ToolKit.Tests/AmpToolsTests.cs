using Biz.Bizadm.MCP.ToolKit.Models.Amp;
using Biz.Bizadm.MCP.ToolKit.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Biz.Bizadm.MCP.ToolKit.Tests;

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
