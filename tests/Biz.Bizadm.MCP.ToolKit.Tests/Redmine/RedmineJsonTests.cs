using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.Redmine;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tests.Redmine;

[TestClass]
public sealed class RedmineJsonTests
{
    [TestMethod]
    public void SetUploads_NullOrEmpty_DoesNotAddKey()
    {
        JsonObject target = new();

        RedmineJson.SetUploads(target, null);
        RedmineJson.SetUploads(target, []);

        Assert.IsFalse(target.ContainsKey("uploads"));
    }

    [TestMethod]
    public void SetUploads_WritesSnakeCaseFieldsAndSkipsMissingOptionals()
    {
        JsonObject target = new();

        RedmineJson.SetUploads(target,
        [
            new UploadReference("7.abc", "log.txt", "text/plain", "server log"),
            new UploadReference("8.def", "image.png")
        ]);

        JsonArray uploads = target["uploads"]!.AsArray();
        Assert.HasCount(2, uploads);
        Assert.AreEqual("7.abc", uploads[0]!["token"]!.GetValue<string>());
        Assert.AreEqual("log.txt", uploads[0]!["filename"]!.GetValue<string>());
        Assert.AreEqual("text/plain", uploads[0]!["content_type"]!.GetValue<string>());
        Assert.AreEqual("server log", uploads[0]!["description"]!.GetValue<string>());
        Assert.AreEqual("8.def", uploads[1]!["token"]!.GetValue<string>());
        Assert.IsFalse(uploads[1]!.AsObject().ContainsKey("content_type"));
        Assert.IsFalse(uploads[1]!.AsObject().ContainsKey("description"));
    }

    [TestMethod]
    public void SetUploads_CustomName_UsesGivenKey()
    {
        JsonObject target = new();

        RedmineJson.SetUploads(target, [new UploadReference("7.abc", "log.txt")], "attachments");

        Assert.IsFalse(target.ContainsKey("uploads"));
        Assert.AreEqual("7.abc", target["attachments"]![0]!["token"]!.GetValue<string>());
    }

    [TestMethod]
    public void SetUploads_BlankTokenOrFilename_Throws()
    {
        Assert.ThrowsExactly<McpException>(() =>
            RedmineJson.SetUploads(new JsonObject(), [new UploadReference(" ", "a.txt")]));
        Assert.ThrowsExactly<McpException>(() =>
            RedmineJson.SetUploads(new JsonObject(), [new UploadReference("7.abc", "")]));
    }
}
