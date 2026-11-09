using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.Jenkins;
using Biz.Bizadm.MCP.ToolKit.Relay;
using Biz.Bizadm.MCP.ToolKit.Tests.Relay;
using Biz.Bizadm.MCP.ToolKit.Tools.Jenkins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Biz.Bizadm.MCP.ToolKit.Tests.Jenkins;

[TestClass]
public sealed class JenkinsToolsTests
{
    private const string Token = "secret-api-token";

    public TestContext TestContext { get; set; } = null!;

    private static JenkinsOptions Options() => new()
    {
        Instances =
        [
            new JenkinsInstance { Name = "prod", Endpoint = "http://prod.example:8080/mcp-server/mcp", User = "builder", ApiToken = Token, Description = "운영 빌드" },
            new JenkinsInstance { Name = "dev", Endpoint = "http://dev.example:8080/mcp-server/mcp", User = "builder", ApiToken = Token },
        ],
    };

    private static McpRelayPool Pool(Func<McpRelayTarget, CancellationToken, Task<McpRelayConnection>> connect)
        => new("Jenkins", Options().Instances.Select(JenkinsMcpServerBuilderExtensions.ToTarget), connect);

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [TestMethod]
    public void ListInstances_ReturnsNameDescriptionEndpointWithoutCredentials()
    {
        using McpRelayPool pool = Pool((_, _) => throw new AssertFailedException("must not connect"));
        string json = new JenkinsTools(pool).ListInstances();

        JsonArray items = JsonNode.Parse(json)!.AsArray();
        Assert.HasCount(2, items);
        Assert.AreEqual("prod", (string?)items[0]!["name"]);
        Assert.AreEqual("운영 빌드", (string?)items[0]!["description"]);
        Assert.AreEqual("http://prod.example:8080/mcp-server/mcp", (string?)items[0]!["endpoint"]);
        Assert.DoesNotContain(Token, json);
        Assert.DoesNotContain("builder", json);
    }

    [TestMethod]
    public async Task ListTools_ReturnsUpstreamNamesAndSchemas()
    {
        await using FakeMcpServer fake = new("getJob", "triggerBuild");
        await using McpRelayPool pool = Pool((_, ct) => fake.ConnectionAsync(ct));

        string json = await new JenkinsTools(pool).ListTools("prod", cancellationToken: TestContext.CancellationToken);

        JsonNode root = JsonNode.Parse(json)!;
        Assert.AreEqual("prod", (string?)root["instance"]);
        JsonArray tools = root["tools"]!.AsArray();
        Assert.AreEqual("getJob", (string?)tools[0]!["name"]);
        Assert.AreEqual("getJob description", (string?)tools[0]!["description"]);
        Assert.AreEqual("string", (string?)tools[0]!["inputSchema"]!["properties"]!["jobName"]!["type"]);
    }

    [TestMethod]
    public async Task CallTool_PassesArgumentsAndReturnsUpstreamResult()
    {
        await using FakeMcpServer fake = new("getJob");
        await using McpRelayPool pool = Pool((_, ct) => fake.ConnectionAsync(ct));
        JenkinsTools tools = new(pool);

        CallToolResult result = await tools.CallTool("prod", " getJob ", Json("""{"jobName":"build-main"}"""), TestContext.CancellationToken);

        Assert.AreEqual("getJob", fake.LastToolName);
        Assert.AreEqual("build-main", fake.LastArguments!["jobName"].GetString());
        Assert.AreEqual("called getJob", ((TextContentBlock)result.Content[0]).Text);
        Assert.IsFalse(result.IsError ?? false);

        CallToolResult failing = await tools.CallTool("prod", "failing_tool", null, TestContext.CancellationToken);
        Assert.IsTrue(failing.IsError);
    }

    [TestMethod]
    public async Task CallTool_ArgumentsAsJsonString_ArePassedAsObject()
    {
        await using FakeMcpServer fake = new("getJob");
        await using McpRelayPool pool = Pool((_, ct) => fake.ConnectionAsync(ct));

        await new JenkinsTools(pool).CallTool("prod", "getJob", Json("""{"text":"{\"jobName\":\"job\"}"}""").GetProperty("text"), TestContext.CancellationToken);

        Assert.AreEqual("job", fake.LastArguments!["jobName"].GetString());
    }

    [TestMethod]
    public void ToArguments_EmptyValues_ReturnNull()
    {
        Assert.IsNull(McpRelayTools.ToArguments(null));
        Assert.IsNull(McpRelayTools.ToArguments(Json("null")));
        Assert.IsNull(McpRelayTools.ToArguments(Json("\"  \"")));
    }

    [TestMethod]
    public void CallTool_InvalidInput_ThrowsWithoutCallingUpstream()
    {
        using McpRelayPool pool = Pool((_, _) => throw new AssertFailedException("must not connect"));
        JenkinsTools tools = new(pool);

        Assert.ThrowsExactly<McpException>(() => tools.CallTool("qa", "getJob"));
        Assert.ThrowsExactly<McpException>(() => tools.CallTool("prod", " "));
        Assert.ThrowsExactly<McpException>(() => tools.CallTool("prod", null));
        McpException missing = Assert.ThrowsExactly<McpException>(() => tools.CallTool(null, "getJob"));
        Assert.Contains("prod, dev", missing.Message);
        foreach (string arguments in new[] { "[1]", "3", "\"[1]\"", "\"{broken\"" })
            Assert.ThrowsExactly<McpException>(() => tools.CallTool("prod", "getJob", Json(arguments)), arguments);
    }

    [TestMethod]
    public async Task ThroughMcpServer_CallToolPassesResultAndTransportErrorKeepsMessage()
    {
        await using FakeMcpServer fake = new("getJob");
        ServiceCollection services = new();
        services.AddMcpServer().WithJenkinsTools(o =>
        {
            foreach (JenkinsInstance instance in Options().Instances)
                o.Instances.Add(instance);
        });
        services.AddKeyedSingleton(JenkinsTools.PoolKey, (sp, _) => new McpRelayPool("Jenkins",
            sp.GetRequiredService<JenkinsOptions>().Instances.Select(JenkinsMcpServerBuilderExtensions.ToTarget),
            (i, ct) => i.Name == "prod"
                ? fake.ConnectionAsync(ct)
                : throw new HttpRequestException("Forbidden", null, HttpStatusCode.Forbidden)));
        await using ServiceProvider provider = services.BuildServiceProvider();

        (ChannelTransport clientSide, ChannelTransport serverSide) = ChannelTransport.CreatePair();
        McpServerOptions serverOptions = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        await using McpServer server = McpServer.Create(serverSide, serverOptions, serviceProvider: provider);
        using CancellationTokenSource stop = new();
        Task run = server.RunAsync(stop.Token);
        await using McpClient client = await McpClient.CreateAsync(new ConnectedClientTransport(clientSide), cancellationToken: TestContext.CancellationToken);

        CallToolResult ok = await client.CallToolAsync("jenkins_call_tool",
            new Dictionary<string, object?> { ["instance"] = "prod", ["tool"] = "getJob", ["arguments"] = new Dictionary<string, object?> { ["jobName"] = "a" } },
            cancellationToken: TestContext.CancellationToken);
        Assert.IsFalse(ok.IsError ?? false);
        Assert.HasCount(1, ok.Content);
        Assert.AreEqual("called getJob", ((TextContentBlock)ok.Content[0]).Text);

        CallToolResult failed = await client.CallToolAsync("jenkins_call_tool",
            new Dictionary<string, object?> { ["instance"] = "dev", ["tool"] = "getJob" },
            cancellationToken: TestContext.CancellationToken);
        Assert.IsTrue(failed.IsError);
        string text = ((TextContentBlock)failed.Content[0]).Text;
        Assert.Contains("'dev'", text);
        Assert.Contains("HTTP 403", text);
        Assert.DoesNotContain(Token, text);

        // upstream 프로토콜 오류(없는 Tool 등)도 JSON-RPC 오류가 아니라 IsError 결과로 온다
        fake.FailCallWithProtocolError = true;
        CallToolResult upstreamError = await client.CallToolAsync("jenkins_call_tool",
            new Dictionary<string, object?> { ["instance"] = "prod", ["tool"] = "missing_tool" },
            cancellationToken: TestContext.CancellationToken);
        Assert.IsTrue(upstreamError.IsError);
        Assert.Contains("Jenkins 'prod' returned error", ((TextContentBlock)upstreamError.Content[0]).Text);

        // instance 인자 자체를 생략해도 등록 이름 안내가 결과로 온다
        CallToolResult noInstance = await client.CallToolAsync("jenkins_list_tools",
            new Dictionary<string, object?>(), cancellationToken: TestContext.CancellationToken);
        Assert.IsTrue(noInstance.IsError);
        Assert.Contains("prod, dev", ((TextContentBlock)noInstance.Content[0]).Text);

        await stop.CancelAsync();
    }
}
