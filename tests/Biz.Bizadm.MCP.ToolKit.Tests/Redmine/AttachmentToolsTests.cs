using System.Net;
using System.Text;
using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.Redmine;
using Biz.Bizadm.MCP.ToolKit.Tools.Redmine;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tests.Redmine;

[TestClass]
public sealed class AttachmentToolsTests
{
    [TestMethod]
    public async Task DownloadAttachment_ByAttachmentId_ReturnsText()
    {
        using ScriptedHandler handler = new();
        handler.EnqueueJson(
            "https://redmine.example/attachments/9.json",
            """{"attachment":{"id":9,"filename":"note.md","filesize":5,"content_type":"text/plain","content_url":"https://redmine.example/attachments/download/9/note.md"}}""");
        handler.EnqueueBytes(
            "https://redmine.example/attachments/download/9/note.md",
            Encoding.UTF8.GetBytes("hello"));

        AttachmentTools tools = CreateTools(handler);
        string json = await tools.DownloadAttachment(attachmentId: 9, cancellationToken: CancellationToken.None);

        JsonNode node = JsonNode.Parse(json)!;
        Assert.AreEqual(9, node["id"]!.GetValue<int>());
        Assert.AreEqual("note.md", node["filename"]!.GetValue<string>());
        Assert.AreEqual("hello", node["text"]!.GetValue<string>());
        Assert.AreEqual(2, handler.RequestCount);
    }

    [TestMethod]
    public async Task DownloadAttachment_ByIssueIdAndFilename_ResolvesId()
    {
        using ScriptedHandler handler = new();
        handler.EnqueueJson(
            "https://redmine.example/issues/40.json?include=attachments",
            """{"issue":{"id":40,"attachments":[{"id":9,"filename":"note.md"}]}}""");
        handler.EnqueueJson(
            "https://redmine.example/attachments/9.json",
            """{"attachment":{"id":9,"filename":"note.md","filesize":2,"content_type":"text/plain","content_url":"https://redmine.example/attachments/download/9/note.md"}}""");
        handler.EnqueueBytes(
            "https://redmine.example/attachments/download/9/note.md",
            Encoding.UTF8.GetBytes("ok"));

        AttachmentTools tools = CreateTools(handler);
        string json = await tools.DownloadAttachment(
            issueId: 40,
            filename: "note.md",
            cancellationToken: CancellationToken.None);

        Assert.AreEqual("ok", JsonNode.Parse(json)!["text"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task DownloadAttachment_DuplicateFilename_Throws()
    {
        using ScriptedHandler handler = new();
        handler.EnqueueJson(
            "https://redmine.example/issues/40.json?include=attachments",
            """{"issue":{"id":40,"attachments":[{"id":9,"filename":"note.md"},{"id":10,"filename":"note.md"}]}}""");

        AttachmentTools tools = CreateTools(handler);
        await Assert.ThrowsExactlyAsync<McpException>(() =>
            tools.DownloadAttachment(issueId: 40, filename: "note.md", cancellationToken: CancellationToken.None));
    }

    [TestMethod]
    public async Task DownloadAttachment_ContentUrlDifferentOrigin_Throws()
    {
        using ScriptedHandler handler = new();
        handler.EnqueueJson(
            "https://redmine.example/attachments/9.json",
            """{"attachment":{"id":9,"filename":"note.md","filesize":1,"content_type":"text/plain","content_url":"https://redmine.example/attachments/download/9/note.md"}}""");

        AttachmentTools tools = CreateTools(handler);
        await Assert.ThrowsExactlyAsync<McpException>(() =>
            tools.DownloadAttachment(
                attachmentId: 9,
                contentUrl: "https://evil.example/attachments/download/9/note.md",
                cancellationToken: CancellationToken.None));
    }

    [TestMethod]
    public async Task DownloadAttachment_SmallFile_SavesAndReturnsText()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "redmine-mcp-attach-" + Guid.NewGuid().ToString("N"));
        string outputPath = Path.Combine(tempDir, "saved.md");
        try
        {
            using ScriptedHandler handler = new();
            handler.EnqueueJson(
                "https://redmine.example/attachments/9.json",
                """{"attachment":{"id":9,"filename":"note.md","filesize":4,"content_type":"text/plain","content_url":"https://redmine.example/attachments/download/9/note.md"}}""");
            handler.EnqueueBytes(
                "https://redmine.example/attachments/download/9/note.md",
                Encoding.UTF8.GetBytes("save"));

            AttachmentTools tools = CreateTools(handler);
            string json = await tools.DownloadAttachment(
                attachmentId: 9,
                outputPath: outputPath,
                cancellationToken: CancellationToken.None);

            JsonNode node = JsonNode.Parse(json)!;
            Assert.AreEqual("save", node["text"]!.GetValue<string>());
            Assert.AreEqual(Path.GetFullPath(outputPath), node["savedPath"]!.GetValue<string>());
            Assert.AreEqual("save", await File.ReadAllTextAsync(outputPath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task UploadFile_Content_PostsUtf8Bytes()
    {
        using ScriptedHandler handler = new();
        handler.EnqueueJson("https://redmine.example/uploads.json", """{"upload":{"token":"7.abc"}}""");
        AttachmentTools tools = CreateTools(handler);

        string json = await tools.UploadFile(content: "한글 log", cancellationToken: CancellationToken.None);

        Assert.AreEqual("7.abc", JsonNode.Parse(json)!["upload"]!["token"]!.GetValue<string>());
        Assert.AreEqual(HttpMethod.Post, handler.LastMethod);
        Assert.AreEqual("application/octet-stream", handler.LastContentType);
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("한글 log"), handler.LastBody);
    }

    [TestMethod]
    public async Task UploadFile_ContentBase64_PostsDecodedBytes()
    {
        byte[] payload = [0, 1, 2, 255];
        using ScriptedHandler handler = new();
        handler.EnqueueJson("https://redmine.example/uploads.json", """{"upload":{"token":"8.def"}}""");
        AttachmentTools tools = CreateTools(handler);

        await tools.UploadFile(contentBase64: Convert.ToBase64String(payload), cancellationToken: CancellationToken.None);

        CollectionAssert.AreEqual(payload, handler.LastBody);
    }

    [TestMethod]
    public async Task UploadFile_SourcePath_PostsFileBytes()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), "redmine-mcp-upload-" + Guid.NewGuid().ToString("N") + ".bin");
        byte[] payload = [9, 8, 7];
        try
        {
            await File.WriteAllBytesAsync(tempFile, payload);
            using ScriptedHandler handler = new();
            handler.EnqueueJson("https://redmine.example/uploads.json", """{"upload":{"token":"9.ghi"}}""");
            AttachmentTools tools = CreateTools(handler);

            await tools.UploadFile(sourcePath: tempFile, cancellationToken: CancellationToken.None);

            CollectionAssert.AreEqual(payload, handler.LastBody);
            Assert.AreEqual("application/octet-stream", handler.LastContentType);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [TestMethod]
    public async Task UploadFile_InvalidInputs_ThrowWithoutRequest()
    {
        using ScriptedHandler handler = new();
        AttachmentTools tools = CreateTools(handler);

        await Assert.ThrowsExactlyAsync<McpException>(() => tools.UploadFile(cancellationToken: CancellationToken.None));
        await Assert.ThrowsExactlyAsync<McpException>(() =>
            tools.UploadFile(content: "a", contentBase64: "YQ==", cancellationToken: CancellationToken.None));
        await Assert.ThrowsExactlyAsync<McpException>(() =>
            tools.UploadFile(contentBase64: "not base64!", cancellationToken: CancellationToken.None));
        await Assert.ThrowsExactlyAsync<McpException>(() =>
            tools.UploadFile(content: new string('a', AttachmentDownload.MaxInlineBytes + 1), cancellationToken: CancellationToken.None));
        await Assert.ThrowsExactlyAsync<FileNotFoundException>(() =>
            tools.UploadFile(sourcePath: Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".missing"), cancellationToken: CancellationToken.None));

        Assert.AreEqual(0, handler.RequestCount);
    }

    private static AttachmentTools CreateTools(ScriptedHandler handler)
    {
        HttpClient httpClient = new(handler, disposeHandler: false);
        RedmineClient.Configure(httpClient, new RedmineOptions { ApiEndpoint = "https://redmine.example/", ApiKey = "secret-key" });
        return new AttachmentTools(new RedmineClient(httpClient));
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<(string Uri, HttpStatusCode Status, byte[] Body, string? MediaType)> _responses = new();

        public int RequestCount { get; private set; }

        public HttpMethod? LastMethod { get; private set; }

        public string? LastContentType { get; private set; }

        public byte[]? LastBody { get; private set; }

        public void EnqueueJson(string uri, string json, HttpStatusCode status = HttpStatusCode.OK)
        {
            _responses.Enqueue((uri, status, Encoding.UTF8.GetBytes(json), "application/json"));
        }

        public void EnqueueBytes(string uri, byte[] body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _responses.Enqueue((uri, status, body, "application/octet-stream"));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastMethod = request.Method;
            LastContentType = request.Content?.Headers.ContentType?.MediaType;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            Assert.IsTrue(_responses.Count > 0, "Unexpected extra HTTP request: " + request.RequestUri);
            (string expectedUri, HttpStatusCode status, byte[] body, string? mediaType) = _responses.Dequeue();
            Assert.AreEqual(expectedUri, request.RequestUri!.AbsoluteUri);

            ByteArrayContent content = new(body);
            if (mediaType is not null)
            {
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
            }

            return new HttpResponseMessage(status)
            {
                Content = content
            };
        }
    }
}
