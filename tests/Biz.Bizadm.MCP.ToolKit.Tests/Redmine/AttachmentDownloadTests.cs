using System.Text;
using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.Redmine;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tests.Redmine;

[TestClass]
public sealed class AttachmentDownloadTests
{
    [TestMethod]
    public void BuildResponseJson_Markdown_UsesText()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("# title");
        string json = AttachmentDownload.BuildResponseJson(
            id: 10,
            filename: "note.md",
            contentType: "application/octet-stream",
            bytes,
            includeBody: true,
            savedPath: null);

        JsonNode node = JsonNode.Parse(json)!;
        Assert.AreEqual("# title", node["text"]!.GetValue<string>());
        Assert.IsNull(node["contentBase64"]);
        Assert.AreEqual(bytes.Length, node["filesize"]!.GetValue<int>());
    }

    [TestMethod]
    public void BuildResponseJson_Binary_UsesBase64()
    {
        byte[] bytes = [0x00, 0x01, 0xFF];
        string json = AttachmentDownload.BuildResponseJson(
            id: 11,
            filename: "blob.bin",
            contentType: "application/octet-stream",
            bytes,
            includeBody: true,
            savedPath: null);

        JsonNode node = JsonNode.Parse(json)!;
        Assert.IsNull(node["text"]);
        Assert.AreEqual(Convert.ToBase64String(bytes), node["contentBase64"]!.GetValue<string>());
    }

    [TestMethod]
    public void BuildResponseJson_InvalidUtf8TextType_FallsBackToBase64()
    {
        byte[] bytes = [0xFF, 0xFE, 0xFD];
        string json = AttachmentDownload.BuildResponseJson(
            id: 12,
            filename: "broken.txt",
            contentType: "text/plain",
            bytes,
            includeBody: true,
            savedPath: null);

        JsonNode node = JsonNode.Parse(json)!;
        Assert.IsNull(node["text"]);
        Assert.AreEqual(Convert.ToBase64String(bytes), node["contentBase64"]!.GetValue<string>());
    }

    [TestMethod]
    public void ExceedsInlineLimit_Boundary()
    {
        Assert.IsFalse(AttachmentDownload.ExceedsInlineLimit(AttachmentDownload.MaxInlineBytes));
        Assert.IsTrue(AttachmentDownload.ExceedsInlineLimit(AttachmentDownload.MaxInlineBytes + 1));
    }

    [TestMethod]
    public void BuildResponseJson_IncludeBodyFalse_OmitsPayloadFields()
    {
        byte[] bytes = new byte[AttachmentDownload.MaxInlineBytes + 1];
        string json = AttachmentDownload.BuildResponseJson(
            id: 13,
            filename: "big.bin",
            contentType: "application/octet-stream",
            bytes,
            includeBody: false,
            savedPath: @"D:\tmp\big.bin");

        JsonNode node = JsonNode.Parse(json)!;
        Assert.IsNull(node["text"]);
        Assert.IsNull(node["contentBase64"]);
        Assert.AreEqual(@"D:\tmp\big.bin", node["savedPath"]!.GetValue<string>());
        Assert.AreEqual(bytes.Length, node["filesize"]!.GetValue<int>());
    }

    [TestMethod]
    public void BuildResponseJson_SmallFileWithSavedPath_IncludesTextAndPath()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("ok");
        string json = AttachmentDownload.BuildResponseJson(
            id: 14,
            filename: "a.txt",
            contentType: "text/plain",
            bytes,
            includeBody: true,
            savedPath: @"C:\out\a.txt");

        JsonNode node = JsonNode.Parse(json)!;
        Assert.AreEqual("ok", node["text"]!.GetValue<string>());
        Assert.AreEqual(@"C:\out\a.txt", node["savedPath"]!.GetValue<string>());
    }
}
