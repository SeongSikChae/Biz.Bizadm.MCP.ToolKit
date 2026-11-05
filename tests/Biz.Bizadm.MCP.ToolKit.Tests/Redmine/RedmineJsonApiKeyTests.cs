using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.Redmine;

namespace Biz.Bizadm.MCP.ToolKit.Tests.Redmine;

[TestClass]
public sealed class RedmineJsonApiKeyTests
{
    [TestMethod]
    public void RemoveUserApiKey_WithKey_RemovesOnlyKey()
    {
        const string json = """{"user":{"id":37,"login":"tester","firstname":"성식","api_key":"secret"}}""";

        string result = RedmineJson.RemoveUserApiKey(json);

        Assert.IsFalse(result.Contains("api_key", StringComparison.Ordinal));
        Assert.IsFalse(result.Contains("secret", StringComparison.Ordinal));
        Assert.IsFalse(result.Contains("\\u", StringComparison.Ordinal));
        JsonObject user = JsonNode.Parse(result)!["user"]!.AsObject();
        Assert.AreEqual(37, user["id"]!.GetValue<int>());
        Assert.AreEqual("tester", user["login"]!.GetValue<string>());
        Assert.AreEqual("성식", user["firstname"]!.GetValue<string>());
        Assert.HasCount(3, user);
    }

    [TestMethod]
    [DataRow("""{"user":{"id":2,"login":"other"}}""")]
    [DataRow("""{"users":[{"id":1,"api_key":"x"}]}""")]
    [DataRow("""[{"user":{"api_key":"x"}}]""")]
    [DataRow("{}")]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("<html><body>login</body></html>")]
    public void RemoveUserApiKey_NothingToRemove_ReturnsInput(string json)
    {
        Assert.AreSame(json, RedmineJson.RemoveUserApiKey(json));
    }
}
