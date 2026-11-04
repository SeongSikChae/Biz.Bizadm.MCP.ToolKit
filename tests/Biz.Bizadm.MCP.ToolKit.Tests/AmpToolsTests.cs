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
    public void WithTools_RegistersCalculateAmpMaxOutput()
    {
        ServiceCollection services = new();
        services.AddMcpServer().WithTools<AmpTools>();
        using ServiceProvider provider = services.BuildServiceProvider();
        CollectionAssert.Contains(
            provider.GetServices<McpServerTool>().Select(t => t.ProtocolTool.Name).ToList(),
            "calculate_amp_max_output");
    }

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
    public void WithTools_RegistersCalculateAmpLoadOutput()
    {
        ServiceCollection services = new();
        services.AddMcpServer().WithTools<AmpTools>();
        using ServiceProvider provider = services.BuildServiceProvider();
        CollectionAssert.Contains(
            provider.GetServices<McpServerTool>().Select(t => t.ProtocolTool.Name).ToList(),
            "calculate_amp_load_output");
    }
}
