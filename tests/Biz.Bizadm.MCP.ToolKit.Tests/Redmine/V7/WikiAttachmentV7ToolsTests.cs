using System.Reflection;

using Biz.Bizadm.MCP.ToolKit.Tools.Redmine.V7;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tests.Redmine.V7;

[TestClass]
public sealed class WikiAttachmentV7ToolsTests
{
    [TestMethod]
    public async Task GetWikiPageVersion_EscapesTitle()
    {
        using V7RecordingHandler handler = new();
        WikiExtraTools tools = new(handler.CreateClient());

        await tools.GetWikiPageVersion("demo", " 배포 가이드 ", 3, CancellationToken.None);

        Assert.AreEqual(HttpMethod.Get, handler.Last.Method);
        Assert.AreEqual(
            V7RecordingHandler.Endpoint + "projects/demo/wiki/" + Uri.EscapeDataString("배포 가이드") + "/3.json",
            handler.Last.Uri.AbsoluteUri);
    }

    [TestMethod]
    public async Task DeleteWikiPage_UsesDeleteRoute()
    {
        using V7RecordingHandler handler = new();
        WikiExtraTools tools = new(handler.CreateClient());

        await tools.DeleteWikiPage("demo", "Old Page", CancellationToken.None);

        Assert.AreEqual(HttpMethod.Delete, handler.Last.Method);
        Assert.AreEqual(V7RecordingHandler.Endpoint + "projects/demo/wiki/Old%20Page.json", handler.Last.Uri.AbsoluteUri);
        string description = typeof(WikiExtraTools).GetMethod(nameof(WikiExtraTools.DeleteWikiPage))!
            .GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()!.Description;
        StringAssert.Contains(description, "하위 페이지는 삭제되지 않고 최상위 페이지가 된다", StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task Wiki_InvalidInput_ThrowsWithoutRequest()
    {
        using V7RecordingHandler handler = new();
        WikiExtraTools tools = new(handler.CreateClient());

        await Assert.ThrowsExactlyAsync<McpException>(() => tools.GetWikiPageVersion("demo", "Page", 0, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<McpException>(() => tools.GetWikiPageVersion("demo", " ", 1, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<McpException>(() => tools.DeleteWikiPage(" ", "Page", CancellationToken.None));
        Assert.IsEmpty(handler.Requests);
    }

    [TestMethod]
    public async Task AttachmentTools_UsePatchAndDelete()
    {
        using V7RecordingHandler handler = new();
        AttachmentManageTools tools = new(handler.CreateClient());

        await tools.UpdateAttachment(8, filename: "new.txt", description: "설명", cancellationToken: CancellationToken.None);
        Assert.AreEqual(HttpMethod.Patch, handler.Last.Method);
        Assert.AreEqual(V7RecordingHandler.Endpoint + "attachments/8.json", handler.Last.Uri.AbsoluteUri);
        System.Text.Json.Nodes.JsonNode attachment = System.Text.Json.Nodes.JsonNode.Parse(handler.Last.Body!)!["attachment"]!;
        Assert.AreEqual("new.txt", attachment["filename"]!.GetValue<string>());
        Assert.AreEqual("설명", attachment["description"]!.GetValue<string>());

        await tools.DeleteAttachment(8, CancellationToken.None);
        Assert.AreEqual(HttpMethod.Delete, handler.Last.Method);
        Assert.AreEqual(V7RecordingHandler.Endpoint + "attachments/8.json", handler.Last.Uri.AbsoluteUri);
    }

    [TestMethod]
    public async Task Attachment_InvalidInput_ThrowsWithoutRequest()
    {
        using V7RecordingHandler handler = new();
        AttachmentManageTools tools = new(handler.CreateClient());

        await Assert.ThrowsExactlyAsync<McpException>(() => tools.UpdateAttachment(8, cancellationToken: CancellationToken.None));
        await Assert.ThrowsExactlyAsync<McpException>(() => tools.DeleteAttachment(0, CancellationToken.None));
        Assert.IsEmpty(handler.Requests);
    }
}
