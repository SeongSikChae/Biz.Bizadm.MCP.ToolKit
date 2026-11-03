using Microsoft.Extensions.DependencyInjection;

namespace Biz.Bizadm.MCP.ToolKit.Tests;

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
