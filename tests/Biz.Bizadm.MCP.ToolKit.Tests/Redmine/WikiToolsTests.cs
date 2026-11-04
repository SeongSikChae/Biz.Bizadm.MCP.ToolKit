using System.Net;
using System.Text;
using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.Redmine;
using Biz.Bizadm.MCP.ToolKit.Tools.Redmine;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tests.Redmine;

[TestClass]
public sealed class WikiToolsTests
{
    [TestMethod]
    public async Task PutWikiPage_WithUploads_SendsUploadsAndText()
    {
        using RecordingHandler handler = new();
        WikiTools tools = CreateTools(handler);

        await tools.PutWikiPage(
            "ops",
            "Runbook",
            "h1. Runbook",
            uploads: [new UploadReference("7.abc", "diagram.png", "image/png")],
            cancellationToken: CancellationToken.None);

        Assert.AreEqual(HttpMethod.Put, handler.Method);
        Assert.AreEqual("https://redmine.example/projects/ops/wiki/Runbook.json", handler.RequestUri!.AbsoluteUri);
        Assert.AreEqual("application/json", handler.ContentType);
        JsonObject root = JsonNode.Parse(handler.Body!)!.AsObject();
        JsonObject wikiPage = root["wiki_page"]!.AsObject();
        Assert.AreEqual("h1. Runbook", wikiPage["text"]!.GetValue<string>());
        Assert.IsFalse(wikiPage.ContainsKey("uploads"));
        JsonObject upload = root["attachments"]![0]!.AsObject();
        Assert.AreEqual("7.abc", upload["token"]!.GetValue<string>());
        Assert.AreEqual("diagram.png", upload["filename"]!.GetValue<string>());
        Assert.AreEqual("image/png", upload["content_type"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task PutWikiPage_WithoutUploads_OmitsUploadsKey()
    {
        using RecordingHandler handler = new();
        WikiTools tools = CreateTools(handler);

        await tools.PutWikiPage("ops", "Runbook", "h1. Runbook", "edit", cancellationToken: CancellationToken.None);

        JsonObject root = JsonNode.Parse(handler.Body!)!.AsObject();
        Assert.HasCount(1, root);
        JsonObject wikiPage = root["wiki_page"]!.AsObject();
        Assert.IsFalse(wikiPage.ContainsKey("uploads"));
        Assert.AreEqual("edit", wikiPage["comments"]!.GetValue<string>());
    }

    private static WikiTools CreateTools(RecordingHandler handler)
    {
        HttpClient httpClient = new(handler, disposeHandler: false);
        RedmineClient.Configure(httpClient, new RedmineOptions { ApiEndpoint = "https://redmine.example/", ApiKey = "secret-key" });
        return new WikiTools(new RedmineClient(httpClient));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }

        public Uri? RequestUri { get; private set; }

        public string? Body { get; private set; }

        public string? ContentType { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            RequestUri = request.RequestUri;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
            };
        }
    }
}
