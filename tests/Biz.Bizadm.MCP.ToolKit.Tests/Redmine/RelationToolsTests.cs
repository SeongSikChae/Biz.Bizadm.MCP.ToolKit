using System.Net;
using System.Text;
using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.Redmine;
using Biz.Bizadm.MCP.ToolKit.Tools.Redmine;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tests.Redmine;

[TestClass]
public sealed class RelationToolsTests
{
    [TestMethod]
    public async Task ListIssueRelations_GetsIssueRelations()
    {
        using RecordingHandler handler = new("""{"relations":[]}""");
        RelationTools tools = CreateTools(handler);

        string json = await tools.ListIssueRelations(40, CancellationToken.None);

        Assert.AreEqual("""{"relations":[]}""", json);
        Assert.AreEqual(HttpMethod.Get, handler.Method);
        Assert.AreEqual("https://redmine.example/issues/40/relations.json", handler.RequestUri!.AbsoluteUri);
    }

    [TestMethod]
    public async Task CreateIssueRelation_PostsRelationWithoutDelay()
    {
        using RecordingHandler handler = new("""{"relation":{"id":5}}""");
        RelationTools tools = CreateTools(handler);

        await tools.CreateIssueRelation(40, 41, " relates ", cancellationToken: CancellationToken.None);

        Assert.AreEqual(HttpMethod.Post, handler.Method);
        Assert.AreEqual("https://redmine.example/issues/40/relations.json", handler.RequestUri!.AbsoluteUri);
        JsonObject relation = JsonNode.Parse(handler.Body!)!["relation"]!.AsObject();
        Assert.AreEqual(41, relation["issue_to_id"]!.GetValue<int>());
        Assert.AreEqual("relates", relation["relation_type"]!.GetValue<string>());
        Assert.IsFalse(relation.ContainsKey("delay"));
    }

    [TestMethod]
    public async Task CreateIssueRelation_PrecedesWithDelay_SendsDelay()
    {
        using RecordingHandler handler = new("""{"relation":{"id":6}}""");
        RelationTools tools = CreateTools(handler);

        await tools.CreateIssueRelation(40, 41, "precedes", delay: 3, cancellationToken: CancellationToken.None);

        JsonObject relation = JsonNode.Parse(handler.Body!)!["relation"]!.AsObject();
        Assert.AreEqual("precedes", relation["relation_type"]!.GetValue<string>());
        Assert.AreEqual(3, relation["delay"]!.GetValue<int>());
    }

    [TestMethod]
    [DataRow("copied_to")]
    [DataRow("relate")]
    [DataRow("RELATES")]
    [DataRow("")]
    public async Task CreateIssueRelation_UnsupportedType_ThrowsWithoutRequest(string relationType)
    {
        using RecordingHandler handler = new("{}");
        RelationTools tools = CreateTools(handler);

        await Assert.ThrowsExactlyAsync<McpException>(() =>
            tools.CreateIssueRelation(40, 41, relationType, cancellationToken: CancellationToken.None));

        Assert.AreEqual(0, handler.RequestCount);
    }

    [TestMethod]
    public async Task CreateIssueRelation_DelayOnRelates_ThrowsWithoutRequest()
    {
        using RecordingHandler handler = new("{}");
        RelationTools tools = CreateTools(handler);

        await Assert.ThrowsExactlyAsync<McpException>(() =>
            tools.CreateIssueRelation(40, 41, "relates", delay: 1, cancellationToken: CancellationToken.None));

        Assert.AreEqual(0, handler.RequestCount);
    }

    [TestMethod]
    public async Task RelationTools_NonPositiveIds_ThrowWithoutRequest()
    {
        using RecordingHandler handler = new("{}");
        RelationTools tools = CreateTools(handler);

        await Assert.ThrowsExactlyAsync<McpException>(() => tools.ListIssueRelations(0, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<McpException>(() =>
            tools.CreateIssueRelation(0, 41, "relates", cancellationToken: CancellationToken.None));
        await Assert.ThrowsExactlyAsync<McpException>(() =>
            tools.CreateIssueRelation(40, -1, "relates", cancellationToken: CancellationToken.None));
        await Assert.ThrowsExactlyAsync<McpException>(() => tools.DeleteIssueRelation(0, CancellationToken.None));

        Assert.AreEqual(0, handler.RequestCount);
    }

    [TestMethod]
    public async Task DeleteIssueRelation_SendsDelete()
    {
        using RecordingHandler handler = new(string.Empty);
        RelationTools tools = CreateTools(handler);

        string json = await tools.DeleteIssueRelation(5, CancellationToken.None);

        Assert.AreEqual(string.Empty, json);
        Assert.AreEqual(HttpMethod.Delete, handler.Method);
        Assert.AreEqual("https://redmine.example/relations/5.json", handler.RequestUri!.AbsoluteUri);
    }

    private static RelationTools CreateTools(RecordingHandler handler)
    {
        HttpClient httpClient = new(handler, disposeHandler: false);
        RedmineClient.Configure(httpClient, new RedmineOptions { ApiEndpoint = "https://redmine.example/", ApiKey = "secret-key" });
        return new RelationTools(new RedmineClient(httpClient));
    }

    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        public HttpMethod? Method { get; private set; }

        public Uri? RequestUri { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Method = request.Method;
            RequestUri = request.RequestUri;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
