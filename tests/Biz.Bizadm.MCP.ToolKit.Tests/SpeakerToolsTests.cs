using Biz.Bizadm.MCP.ToolKit.Models.Speaker;
using Biz.Bizadm.MCP.ToolKit.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Biz.Bizadm.MCP.ToolKit.Tests;

[TestClass]
public sealed class SpeakerToolsTests
{
    private const double Delta = 1e-9;

    [TestMethod]
    [DataRow(1d, 1d, SpeakerSourceType.Point, 90d)]
    [DataRow(100d, 10d, SpeakerSourceType.Point, 90d)] // +20 −20
    [DataRow(100d, 10d, SpeakerSourceType.Line, 100d)] // +20 −10
    [DataRow(1d, 2d, SpeakerSourceType.Point, 90 - 20 * 0.30102999566398120)] // ≈ 83.98
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
        ServiceCollection services = new();
        services.AddMcpServer().WithBizadmToolKit();
        using ServiceProvider provider = services.BuildServiceProvider();
        CollectionAssert.Contains(
            provider.GetServices<McpServerTool>().Select(t => t.ProtocolTool.Name).ToList(),
            "calculate_speaker_spl");
    }

    [TestMethod]
    public void CalculateSpeakerSpl_InputSchema_SourceTypeIsString()
    {
        ServiceCollection services = new();
        services.AddMcpServer().WithBizadmToolKit();
        using ServiceProvider provider = services.BuildServiceProvider();
        McpServerTool tool = provider.GetServices<McpServerTool>()
            .Single(t => t.ProtocolTool.Name == "calculate_speaker_spl");
        string schema = tool.ProtocolTool.InputSchema.GetRawText();
        StringAssert.Contains(schema, "\"Point\"");
        StringAssert.Contains(schema, "\"Line\"");
    }
}
