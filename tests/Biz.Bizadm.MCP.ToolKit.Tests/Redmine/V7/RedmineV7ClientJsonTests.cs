using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.Redmine;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tests.Redmine.V7;

[TestClass]
public sealed class RedmineV7ClientJsonTests
{
    [TestMethod]
    public async Task PatchAsync_SendsWrappedBody()
    {
        using V7RecordingHandler handler = new();
        RedmineClient client = handler.CreateClient();

        await client.PatchAsync("attachments/3.json", "attachment", new JsonObject { ["filename"] = "a.txt" }, CancellationToken.None);

        Assert.AreEqual(HttpMethod.Patch, handler.Last.Method);
        Assert.AreEqual("https://redmine.example/attachments/3.json", handler.Last.Uri.ToString());
        Assert.AreEqual("""{"attachment":{"filename":"a.txt"}}""", handler.Last.Body);
    }

    [TestMethod]
    public async Task PostBodyAsync_SendsBodyAsIs()
    {
        using V7RecordingHandler handler = new();
        RedmineClient client = handler.CreateClient();

        await client.PostBodyAsync("issues/1/watchers.json", new JsonObject { ["user_id"] = 5 }, CancellationToken.None);

        Assert.AreEqual(HttpMethod.Post, handler.Last.Method);
        Assert.AreEqual("""{"user_id":5}""", handler.Last.Body);
    }

    [TestMethod]
    public void SetBool_WritesFalse()
    {
        JsonObject target = new();
        RedmineJson.Set(target, "is_private", false);
        RedmineJson.Set(target, "skip", (bool?)null);
        Assert.AreEqual("""{"is_private":false}""", target.ToJsonString());
    }

    [TestMethod]
    public void SetIdArray_WritesArrayAndRejectsNonPositive()
    {
        JsonObject target = new();
        RedmineJson.SetIdArray(target, "watcher_user_ids", [3, 4]);
        RedmineJson.SetIdArray(target, "empty", []);
        Assert.AreEqual("""{"watcher_user_ids":[3,4]}""", target.ToJsonString());
        Assert.ThrowsExactly<McpException>(() => RedmineJson.SetIdArray(new JsonObject(), "ids", [0]));
    }

    [TestMethod]
    public void SetCustomFields_WritesSingleAndMultipleValues()
    {
        JsonObject target = new();
        RedmineJson.SetCustomFields(target, [new CustomFieldValue(1, Value: "x"), new CustomFieldValue(2, Values: ["a", "b"])]);
        Assert.AreEqual("""{"custom_fields":[{"id":1,"value":"x"},{"id":2,"value":["a","b"]}]}""", target.ToJsonString());
    }

    [TestMethod]
    public void SetCustomFields_InvalidItems_Throw()
    {
        Assert.ThrowsExactly<McpException>(() => RedmineJson.SetCustomFields(new JsonObject(), [new CustomFieldValue(1)]));
        Assert.ThrowsExactly<McpException>(() => RedmineJson.SetCustomFields(new JsonObject(), [new CustomFieldValue(1, "x", ["y"])]));
        Assert.ThrowsExactly<McpException>(() => RedmineJson.SetCustomFields(new JsonObject(), [new CustomFieldValue(0, "x")]));
        Assert.ThrowsExactly<McpException>(() => RedmineJson.SetCustomFields(new JsonObject(), [null!]));
    }

    [TestMethod]
    public void RedminePaths_EscapesAndValidates()
    {
        Assert.AreEqual("projects/my%20proj/versions.json", RedminePaths.Project(" my proj ", "versions.json"));
        Assert.AreEqual("%ED%95%9C", RedminePaths.Segment("한", "title"));
        Assert.AreEqual("7", RedminePaths.Id(7, "id"));
        Assert.ThrowsExactly<McpException>(() => RedminePaths.Project(" ", "x"));
        Assert.ThrowsExactly<McpException>(() => RedminePaths.Segment("", "title"));
        Assert.ThrowsExactly<McpException>(() => RedminePaths.Id(0, "id"));
    }
}
