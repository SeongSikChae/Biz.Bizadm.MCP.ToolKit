using Biz.Bizadm.MCP.ToolKit.Tests.Redmine.V7;
using Biz.Bizadm.MCP.ToolKit.Tools.Redmine;
using Biz.Bizadm.MCP.ToolKit.Tools.Redmine.V7;

namespace Biz.Bizadm.MCP.ToolKit.Tests.Redmine;

[TestClass]
public sealed class UserToolsApiKeyTests
{
    private const string UserWithKey = """{"user":{"id":37,"login":"tester","firstname":"성식","api_key":"secret"}}""";

    [TestMethod]
    public async Task GetCurrentUser_RemovesApiKey()
    {
        using V7RecordingHandler handler = new();
        handler.Enqueue(UserWithKey);
        UserTools tools = new(handler.CreateClient());

        string result = await tools.GetCurrentUser(CancellationToken.None);

        Assert.AreEqual("/users/current.json", handler.Last.Uri.AbsolutePath);
        AssertKeyRemoved(result);
    }

    [TestMethod]
    public async Task GetUser_RemovesApiKey()
    {
        using V7RecordingHandler handler = new();
        handler.Enqueue(UserWithKey);
        UserTools tools = new(handler.CreateClient());

        string result = await tools.GetUser(37, CancellationToken.None);

        Assert.AreEqual("/users/37.json", handler.Last.Uri.AbsolutePath);
        AssertKeyRemoved(result);
    }

    [TestMethod]
    public async Task GetMyAccount_RemovesApiKey()
    {
        using V7RecordingHandler handler = new();
        handler.Enqueue(UserWithKey);
        PrincipalTools tools = new(handler.CreateClient());

        string result = await tools.GetMyAccount(CancellationToken.None);

        Assert.AreEqual("/my/account.json", handler.Last.Uri.AbsolutePath);
        AssertKeyRemoved(result);
    }

    private static void AssertKeyRemoved(string result)
    {
        Assert.IsFalse(result.Contains("api_key", StringComparison.Ordinal));
        Assert.IsFalse(result.Contains("secret", StringComparison.Ordinal));
        StringAssert.Contains(result, "\"firstname\":\"성식\"", StringComparison.Ordinal);
    }
}
