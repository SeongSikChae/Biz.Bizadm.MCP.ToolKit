using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.GitHub;
using Biz.Bizadm.MCP.ToolKit.Jenkins;
using Biz.Bizadm.MCP.ToolKit.Relay;
using Biz.Bizadm.MCP.ToolKit.Tests.Relay;
using Biz.Bizadm.MCP.ToolKit.Tools.GitHub;
using Biz.Bizadm.MCP.ToolKit.Tools.Jenkins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Biz.Bizadm.MCP.ToolKit.Tests.GitHub;

[TestClass]
public sealed class GitHubToolsTests
{
    private const string Token = "ghp_secret-token";

    public TestContext TestContext { get; set; } = null!;

    private static GitHubInstance[] Instances() =>
    [
        new GitHubInstance { Name = "public", Endpoint = "https://api.githubcopilot.com/mcp/", Token = Token, Description = "github.com" },
        new GitHubInstance { Name = "enterprise", Endpoint = "https://ghe.example/mcp/", Token = Token },
    ];

    private static McpRelayPool Pool(Func<McpRelayTarget, CancellationToken, Task<McpRelayConnection>> connect)
        => new("GitHub", Instances().Select(GitHubMcpServerBuilderExtensions.ToTarget), connect);

    [TestMethod]
    public void ListInstances_ReturnsNameDescriptionEndpointWithoutToken()
    {
        using McpRelayPool pool = Pool((_, _) => throw new AssertFailedException("must not connect"));
        string json = new GitHubTools(pool).ListInstances();

        JsonArray items = JsonNode.Parse(json)!.AsArray();
        Assert.HasCount(2, items);
        Assert.AreEqual("public", (string?)items[0]!["name"]);
        Assert.AreEqual("github.com", (string?)items[0]!["description"]);
        Assert.AreEqual("https://api.githubcopilot.com/mcp/", (string?)items[0]!["endpoint"]);
        Assert.DoesNotContain(Token, json);
        Assert.DoesNotContain("Bearer", json);
    }

    [TestMethod]
    public async Task ListTools_ReturnsUpstreamNames()
    {
        await using FakeMcpServer fake = new("search_repositories", "create_issue");
        await using McpRelayPool pool = Pool((_, ct) => fake.ConnectionAsync(ct));

        string json = await new GitHubTools(pool).ListTools("public", cancellationToken: TestContext.CancellationToken);

        JsonNode result = JsonNode.Parse(json)!;
        Assert.AreEqual("public", (string?)result["instance"]);
        CollectionAssert.AreEqual(new[] { "search_repositories", "create_issue" },
            result["tools"]!.AsArray().Select(t => (string?)t!["name"]).ToArray());
    }

    [TestMethod]
    public void CallTool_InvalidInput_ThrowsGitHubMessageWithoutCallingUpstream()
    {
        using McpRelayPool pool = Pool((_, _) => throw new AssertFailedException("must not connect"));
        GitHubTools tools = new(pool);

        McpException unknown = Assert.ThrowsExactly<McpException>(() => tools.CallTool("qa", "create_issue"));
        Assert.Contains("Unknown GitHub instance 'qa'", unknown.Message);
        Assert.Contains("public, enterprise", unknown.Message);
        Assert.ThrowsExactly<McpException>(() => tools.CallTool("public", " "));
    }

    [TestMethod]
    public async Task ThroughMcpServer_GitHubAndJenkinsUseTheirOwnPools()
    {
        await using FakeMcpServer githubFake = new("create_issue");
        await using FakeMcpServer jenkinsFake = new("getJob");
        ServiceCollection services = new();
        services.AddMcpServer()
            .WithJenkinsTools(o => o.Instances.Add(new JenkinsInstance { Name = "prod", Endpoint = "http://j/mcp-server/mcp", User = "u", ApiToken = "t" }))
            .WithGitHubTools(o => { foreach (GitHubInstance i in Instances()) o.Instances.Add(i); });
        services.AddKeyedSingleton(GitHubTools.PoolKey, (sp, _) => new McpRelayPool("GitHub",
            sp.GetRequiredService<GitHubOptions>().Instances.Select(GitHubMcpServerBuilderExtensions.ToTarget),
            (i, ct) => i.Name == "public"
                ? githubFake.ConnectionAsync(ct)
                : throw new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized)));
        services.AddKeyedSingleton(JenkinsTools.PoolKey, (sp, _) => new McpRelayPool("Jenkins",
            sp.GetRequiredService<JenkinsOptions>().Instances.Select(JenkinsMcpServerBuilderExtensions.ToTarget),
            (_, ct) => jenkinsFake.ConnectionAsync(ct)));
        await using ServiceProvider provider = services.BuildServiceProvider();

        (ChannelTransport clientSide, ChannelTransport serverSide) = ChannelTransport.CreatePair();
        McpServerOptions serverOptions = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        await using McpServer server = McpServer.Create(serverSide, serverOptions, serviceProvider: provider);
        using CancellationTokenSource stop = new();
        Task run = server.RunAsync(stop.Token);
        await using McpClient client = await McpClient.CreateAsync(new ConnectedClientTransport(clientSide), cancellationToken: TestContext.CancellationToken);

        CallToolResult ok = await client.CallToolAsync("github_call_tool",
            new Dictionary<string, object?> { ["instance"] = "public", ["tool"] = "create_issue", ["arguments"] = """{"title":"t"}""" },
            cancellationToken: TestContext.CancellationToken);
        Assert.IsFalse(ok.IsError ?? false);
        Assert.AreEqual("called create_issue", ((TextContentBlock)ok.Content[0]).Text);
        Assert.AreEqual("t", githubFake.LastArguments!["title"].GetString());
        Assert.AreEqual(1, githubFake.CallToolCount);
        Assert.AreEqual(0, jenkinsFake.CallToolCount);
        Assert.AreEqual(0, jenkinsFake.ConnectCount);

        CallToolResult failed = await client.CallToolAsync("github_call_tool",
            new Dictionary<string, object?> { ["instance"] = "enterprise", ["tool"] = "create_issue" },
            cancellationToken: TestContext.CancellationToken);
        Assert.IsTrue(failed.IsError);
        string text = ((TextContentBlock)failed.Content[0]).Text;
        Assert.Contains("GitHub 'enterprise' request failed (HTTP 401)", text);
        Assert.DoesNotContain(Token, text);

        await stop.CancelAsync();
        await run;
    }
}
