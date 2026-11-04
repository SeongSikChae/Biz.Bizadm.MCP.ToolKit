using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.IO.Pipes;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.Redmine;
using Biz.Bizadm.MCP.ToolKit.Tools.Redmine;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tests.Redmine;

[TestClass]
public sealed class IssueToolsTests
{
    [TestMethod]
    public async Task CreateSubtask_WithoutProjectId_InheritsParentProject()
    {
        using ScriptedHandler handler = new();
        handler.Enqueue("https://redmine.example/issues/40.json", """{"issue":{"id":40,"project":{"id":7,"name":"Ops"}}}""");
        handler.Enqueue("https://redmine.example/issues.json", """{"issue":{"id":41}}""");
        IssueTools tools = CreateTools(handler);

        string json = await tools.CreateSubtask(40, "Child", cancellationToken: CancellationToken.None);

        Assert.AreEqual("""{"issue":{"id":41}}""", json);
        Assert.HasCount(2, handler.Requests);
        Assert.AreEqual(HttpMethod.Get, handler.Requests[0].Method);
        Assert.AreEqual(HttpMethod.Post, handler.Requests[1].Method);
        JsonObject issue = JsonNode.Parse(handler.Requests[1].Body!)!["issue"]!.AsObject();
        Assert.AreEqual(7, issue["project_id"]!.GetValue<int>());
        Assert.AreEqual(40, issue["parent_issue_id"]!.GetValue<int>());
        Assert.AreEqual("Child", issue["subject"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task CreateSubtask_WithProjectId_SkipsParentLookup()
    {
        using ScriptedHandler handler = new();
        handler.Enqueue("https://redmine.example/issues.json", """{"issue":{"id":41}}""");
        IssueTools tools = CreateTools(handler);

        await tools.CreateSubtask(
            40,
            "Child",
            projectId: "ops",
            uploads: [new UploadReference("7.abc", "log.txt")],
            cancellationToken: CancellationToken.None);

        Assert.HasCount(1, handler.Requests);
        JsonObject issue = JsonNode.Parse(handler.Requests[0].Body!)!["issue"]!.AsObject();
        Assert.AreEqual("ops", issue["project_id"]!.GetValue<string>());
        Assert.AreEqual("7.abc", issue["uploads"]![0]!["token"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task CreateSubtask_ParentWithoutProject_Throws()
    {
        using ScriptedHandler handler = new();
        handler.Enqueue("https://redmine.example/issues/40.json", """{"issue":{"id":40}}""");
        IssueTools tools = CreateTools(handler);

        await Assert.ThrowsExactlyAsync<McpException>(() =>
            tools.CreateSubtask(40, "Child", cancellationToken: CancellationToken.None));
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    public async Task CreateSubtask_NonPositiveParent_ThrowsWithoutRequest()
    {
        using ScriptedHandler handler = new();
        IssueTools tools = CreateTools(handler);

        await Assert.ThrowsExactlyAsync<McpException>(() =>
            tools.CreateSubtask(0, "Child", cancellationToken: CancellationToken.None));
        Assert.IsEmpty(handler.Requests);
    }

    [TestMethod]
    public async Task CreateIssue_WithUploads_SendsUploadsArray()
    {
        using ScriptedHandler handler = new();
        handler.Enqueue("https://redmine.example/issues.json", """{"issue":{"id":42}}""");
        IssueTools tools = CreateTools(handler);

        await tools.CreateIssue(
            "ops",
            "With file",
            uploads: [new UploadReference("7.abc", "log.txt", "text/plain", "server log")],
            cancellationToken: CancellationToken.None);

        JsonObject upload = JsonNode.Parse(handler.Requests[0].Body!)!["issue"]!["uploads"]![0]!.AsObject();
        Assert.AreEqual("7.abc", upload["token"]!.GetValue<string>());
        Assert.AreEqual("log.txt", upload["filename"]!.GetValue<string>());
        Assert.AreEqual("text/plain", upload["content_type"]!.GetValue<string>());
        Assert.AreEqual("server log", upload["description"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task UpdateIssue_UploadsOnly_PutsIssue()
    {
        using ScriptedHandler handler = new();
        handler.Enqueue("https://redmine.example/issues/42.json", string.Empty);
        IssueTools tools = CreateTools(handler);

        await tools.UpdateIssue(42, uploads: [new UploadReference("7.abc", "log.txt")], cancellationToken: CancellationToken.None);

        Assert.AreEqual(HttpMethod.Put, handler.Requests[0].Method);
        JsonObject issue = JsonNode.Parse(handler.Requests[0].Body!)!["issue"]!.AsObject();
        Assert.HasCount(1, issue);
        Assert.AreEqual("7.abc", issue["uploads"]![0]!["token"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task UpdateIssue_NoFields_StillThrows()
    {
        using ScriptedHandler handler = new();
        IssueTools tools = CreateTools(handler);

        await Assert.ThrowsExactlyAsync<McpException>(() => tools.UpdateIssue(42, cancellationToken: CancellationToken.None));
        Assert.IsEmpty(handler.Requests);
    }

    [TestMethod]
    public async Task McpCreateIssue_SchemaAndJsonArguments_UseCamelCaseUploadFields()
    {
        using ScriptedHandler handler = new();
        handler.Enqueue("https://redmine.example/issues.json", """{"issue":{"id":43}}""");
        IssueTools tools = CreateTools(handler);
        McpServerTool tool = McpServerTool.Create(typeof(IssueTools).GetMethod(nameof(IssueTools.CreateIssue))!, tools);

        await using McpRoundTrip roundTrip = await McpRoundTrip.StartAsync(tool);

        IList<McpClientTool> listed = await roundTrip.Client.ListToolsAsync();
        JsonElement uploadsSchema = listed.Single(static t => t.Name == "create_issue")
            .ProtocolTool.InputSchema.GetProperty("properties").GetProperty("uploads");
        StringAssert.Contains(uploadsSchema.GetProperty("type").GetRawText(), "array", StringComparison.Ordinal);
        JsonElement itemProperties = uploadsSchema.GetProperty("items").GetProperty("properties");
        CollectionAssert.AreEquivalent(
            new[] { "token", "filename", "contentType", "description" },
            itemProperties.EnumerateObject().Select(static p => p.Name).ToArray());

        using JsonDocument uploadsArgument = JsonDocument.Parse("""[{"token":"t1","filename":"a.txt","contentType":"text/plain"}]""");
        CallToolResult result = await roundTrip.Client.CallToolAsync(
            "create_issue",
            new Dictionary<string, object?>
            {
                ["projectId"] = "ops",
                ["subject"] = "Via MCP",
                ["uploads"] = uploadsArgument.RootElement.Clone()
            });

        Assert.IsFalse(result.IsError ?? false, string.Join(Environment.NewLine, result.Content.OfType<TextContentBlock>().Select(static c => c.Text)));
        JsonObject upload = JsonNode.Parse(handler.Requests[0].Body!)!["issue"]!["uploads"]![0]!.AsObject();
        Assert.AreEqual("t1", upload["token"]!.GetValue<string>());
        Assert.AreEqual("a.txt", upload["filename"]!.GetValue<string>());
        Assert.AreEqual("text/plain", upload["content_type"]!.GetValue<string>());
    }

    private static IssueTools CreateTools(ScriptedHandler handler)
    {
        HttpClient httpClient = new(handler, disposeHandler: false);
        RedmineClient.Configure(httpClient, new RedmineOptions { ApiEndpoint = "https://redmine.example/", ApiKey = "secret-key" });
        return new IssueTools(new RedmineClient(httpClient));
    }

    private sealed class McpRoundTrip : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();
        private readonly List<IDisposable> _pipes = [];
        private McpServer? _server;
        private Task? _serverRun;

        public McpClient Client { get; private set; } = null!;

        public static async Task<McpRoundTrip> StartAsync(McpServerTool tool)
        {
            McpRoundTrip roundTrip = new();
            AnonymousPipeServerStream clientToServer = new(PipeDirection.Out);
            AnonymousPipeClientStream serverInput = new(PipeDirection.In, clientToServer.ClientSafePipeHandle);
            AnonymousPipeServerStream serverToClient = new(PipeDirection.Out);
            AnonymousPipeClientStream clientInput = new(PipeDirection.In, serverToClient.ClientSafePipeHandle);
            roundTrip._pipes.AddRange([clientToServer, serverInput, serverToClient, clientInput]);

            McpServerOptions options = new()
            {
                ToolCollection = [tool]
            };
            roundTrip._server = McpServer.Create(new StreamServerTransport(serverInput, serverToClient, "test"), options);
            roundTrip._serverRun = roundTrip._server.RunAsync(roundTrip._cancellation.Token);
            roundTrip.Client = await McpClient.CreateAsync(new StreamClientTransport(clientToServer, clientInput));
            return roundTrip;
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await _cancellation.CancelAsync();
            if (_serverRun is not null)
            {
                try
                {
                    await _serverRun.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (OperationCanceledException)
                {
                }
            }

            if (_server is not null)
            {
                await _server.DisposeAsync();
            }

            foreach (IDisposable pipe in _pipes)
            {
                pipe.Dispose();
            }

            _cancellation.Dispose();
        }
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<(string Uri, string Body)> _responses = new();

        public List<(HttpMethod Method, Uri Uri, string? Body)> Requests { get; } = [];

        public void Enqueue(string uri, string body)
        {
            _responses.Enqueue((uri, body));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Requests.Add((request.Method, request.RequestUri!, body));
            Assert.IsTrue(_responses.Count > 0, "Unexpected extra HTTP request: " + request.RequestUri);
            (string expectedUri, string responseBody) = _responses.Dequeue();
            Assert.AreEqual(expectedUri, request.RequestUri!.AbsoluteUri);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
