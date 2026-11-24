using Biz.Bizadm.MCP.ToolKit.GitHub;
using Biz.Bizadm.MCP.ToolKit.Jenkins;
using Biz.Bizadm.MCP.ToolKit.Relay;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Biz.Bizadm.MCP.ToolKit.Tests.GitHub;

[TestClass]
public sealed class GitHubRegistrationTests
{
    private const string Token = "ghp_secret-token";

    private static GitHubInstance Instance(string name = "public") => new()
    {
        Name = name,
        Endpoint = "https://api.githubcopilot.com/mcp/",
        ApiEndpoint = "https://api.github.com/",
        Token = Token,
        Description = "github.com 조직",
    };

    private static ServiceCollection CreateServices(Action<GitHubOptions> configure)
    {
        ServiceCollection services = new();
        services.AddMcpServer().WithGitHubTools(configure);
        return services;
    }

    [TestMethod]
    public void WithGitHubTools_RegistersGatewayAndRefTools()
    {
        using ServiceProvider provider = CreateServices(o => o.Instances.Add(Instance())).BuildServiceProvider();
        string[] names = [.. provider.GetServices<McpServerTool>().Select(t => t.ProtocolTool.Name).Order(StringComparer.Ordinal)];

        CollectionAssert.AreEqual(new[]
        {
            "github_call_tool", "github_create_annotated_tag", "github_create_lightweight_tag", "github_create_milestone", "github_create_release",
            "github_delete_milestone", "github_delete_ref", "github_delete_release", "github_delete_release_asset", "github_get_milestone", "github_get_rate_limit",
            "github_list_instances", "github_list_milestones", "github_list_tools", "github_update_milestone", "github_update_ref", "github_update_release", "github_upload_release_asset",
        }, names);
    }

    [TestMethod]
    public void WithGitHubTools_MilestoneToolAnnotationsAndSchemaMatchDesign()
    {
        using ServiceProvider provider = CreateServices(o => o.Instances.Add(Instance())).BuildServiceProvider();
        Dictionary<string, McpServerTool> tools = provider.GetServices<McpServerTool>().ToDictionary(t => t.ProtocolTool.Name);

        foreach (string name in new[] { "github_list_milestones", "github_get_milestone" })
        {
            ToolAnnotations a = tools[name].ProtocolTool.Annotations!;
            Assert.IsTrue(a.ReadOnlyHint, name);
            Assert.IsTrue(a.IdempotentHint, name);
            Assert.IsFalse(a.DestructiveHint, name);
            Assert.IsTrue(a.OpenWorldHint, name);
        }
        ToolAnnotations create = tools["github_create_milestone"].ProtocolTool.Annotations!;
        Assert.IsFalse(create.ReadOnlyHint);
        Assert.IsFalse(create.IdempotentHint);
        Assert.IsFalse(create.DestructiveHint);
        Assert.IsTrue(create.OpenWorldHint);
        foreach (string name in new[] { "github_update_milestone", "github_delete_milestone" })
        {
            ToolAnnotations a = tools[name].ProtocolTool.Annotations!;
            Assert.IsFalse(a.ReadOnlyHint, name);
            Assert.IsTrue(a.IdempotentHint, name);
            Assert.IsTrue(a.DestructiveHint, name);
            Assert.IsTrue(a.OpenWorldHint, name);
        }

        string[] common = ["instance", "owner", "repo"];
        Dictionary<string, string[]> inputs = new()
        {
            ["github_list_milestones"] = ["state", "sort", "direction", "perPage", "page"],
            ["github_get_milestone"] = ["milestoneNumber"],
            ["github_create_milestone"] = ["title", "state", "description", "dueOn"],
            ["github_update_milestone"] = ["milestoneNumber", "title", "state", "description", "dueOn", "clearDescription", "clearDueOn"],
            ["github_delete_milestone"] = ["milestoneNumber"],
        };
        foreach ((string name, string[] extra) in inputs)
        {
            System.Text.Json.JsonElement props = tools[name].ProtocolTool.InputSchema.GetProperty("properties");
            foreach (string p in common.Concat(extra))
                Assert.IsTrue(props.TryGetProperty(p, out _), $"{name}.{p}");
        }
    }

    [TestMethod]
    public void WithGitHubTools_ReleaseToolAnnotationsAndSchemaMatchDesign()
    {
        using ServiceProvider provider = CreateServices(o => o.Instances.Add(Instance())).BuildServiceProvider();
        Dictionary<string, McpServerTool> tools = provider.GetServices<McpServerTool>().ToDictionary(t => t.ProtocolTool.Name);

        Assert.IsFalse(tools["github_create_release"].ProtocolTool.Annotations!.DestructiveHint);
        Assert.IsFalse(tools["github_upload_release_asset"].ProtocolTool.Annotations!.DestructiveHint);
        Assert.IsFalse(tools["github_upload_release_asset"].ProtocolTool.Annotations!.IdempotentHint);
        System.Text.Json.JsonElement upload = tools["github_upload_release_asset"].ProtocolTool.InputSchema.GetProperty("properties");
        foreach (string p in new[] { "releaseId", "tag", "name", "contentBase64", "contentType", "label" })
            Assert.IsTrue(upload.TryGetProperty(p, out _), p);
        foreach (string name in new[] { "github_update_release", "github_delete_release", "github_delete_release_asset" })
        {
            Assert.IsTrue(tools[name].ProtocolTool.Annotations!.DestructiveHint, name);
            Assert.IsTrue(tools[name].ProtocolTool.Annotations!.IdempotentHint, name);
        }
        System.Text.Json.JsonElement props = tools["github_update_release"].ProtocolTool.InputSchema.GetProperty("properties");
        foreach (string p in new[] { "instance", "owner", "repo", "releaseId", "tag", "tagName", "targetCommitish", "name", "body", "draft", "prerelease" })
            Assert.IsTrue(props.TryGetProperty(p, out _), p);
        Assert.IsTrue(tools["github_delete_release_asset"].ProtocolTool.InputSchema.GetProperty("properties").TryGetProperty("assetId", out _));
    }

    [TestMethod]
    public void WithGitHubTools_RateLimitToolAnnotationsAndSchemaMatchDesign()
    {
        using ServiceProvider provider = CreateServices(o => o.Instances.Add(Instance())).BuildServiceProvider();
        McpServerTool tool = provider.GetServices<McpServerTool>().Single(t => t.ProtocolTool.Name == "github_get_rate_limit");
        ToolAnnotations a = tool.ProtocolTool.Annotations!;

        Assert.IsTrue(a.ReadOnlyHint);
        Assert.IsTrue(a.IdempotentHint);
        Assert.IsFalse(a.DestructiveHint);
        Assert.IsTrue(a.OpenWorldHint);
        Assert.IsTrue(tool.ProtocolTool.InputSchema.GetProperty("properties").TryGetProperty("instance", out _));
    }

    [TestMethod]
    public void WithGitHubTools_RefToolAnnotationsMatchDesign()
    {
        using ServiceProvider provider = CreateServices(o => o.Instances.Add(Instance())).BuildServiceProvider();
        Dictionary<string, ToolAnnotations> a = provider.GetServices<McpServerTool>().ToDictionary(t => t.ProtocolTool.Name, t => t.ProtocolTool.Annotations!);

        foreach (string name in new[] { "github_create_lightweight_tag", "github_create_annotated_tag", "github_update_ref", "github_delete_ref" })
        {
            Assert.IsFalse(a[name].ReadOnlyHint, name);
            Assert.IsTrue(a[name].OpenWorldHint, name);
        }
        // 메서드 인자 @ref가 입력 스키마에서 ref로 노출되는지
        McpServerTool update = provider.GetServices<McpServerTool>().Single(t => t.ProtocolTool.Name == "github_update_ref");
        System.Text.Json.JsonElement props = update.ProtocolTool.InputSchema.GetProperty("properties");
        foreach (string p in new[] { "instance", "owner", "repo", "ref", "sha", "force" })
            Assert.IsTrue(props.TryGetProperty(p, out _), p);
        Assert.IsFalse(a["github_create_lightweight_tag"].DestructiveHint);
        Assert.IsFalse(a["github_create_annotated_tag"].DestructiveHint);
        Assert.IsTrue(a["github_update_ref"].DestructiveHint);
        Assert.IsTrue(a["github_delete_ref"].DestructiveHint);
    }

    [TestMethod]
    public void WithGitHubTools_AnnotationsMatchDesign()
    {
        using ServiceProvider provider = CreateServices(o => o.Instances.Add(Instance())).BuildServiceProvider();
        Dictionary<string, McpServerTool> tools = provider.GetServices<McpServerTool>().ToDictionary(t => t.ProtocolTool.Name);

        Assert.IsTrue(tools["github_list_instances"].ProtocolTool.Annotations!.ReadOnlyHint);
        Assert.IsTrue(tools["github_list_tools"].ProtocolTool.Annotations!.ReadOnlyHint);
        Assert.IsFalse(tools["github_call_tool"].ProtocolTool.Annotations!.ReadOnlyHint);
        Assert.IsTrue(tools["github_call_tool"].ProtocolTool.Annotations!.DestructiveHint);
    }

    public static IEnumerable<object[]> InvalidOptions()
    {
        yield return [(Action<GitHubOptions>)(o => { })];
        yield return [(Action<GitHubOptions>)(o => o.Instances = null!)];
        yield return [(Action<GitHubOptions>)(o => o.Instances.Add(null!))];
        yield return [(Action<GitHubOptions>)(o => o.Instances.Add(new GitHubInstance { Name = " ", Endpoint = "https://g/mcp/", ApiEndpoint = "https://api.github.com/", Token = Token }))];
        yield return [(Action<GitHubOptions>)(o => { o.Instances.Add(Instance("public")); o.Instances.Add(Instance("PUBLIC")); })];
        yield return [(Action<GitHubOptions>)(o => o.Instances.Add(new GitHubInstance { Name = "a", Endpoint = "/mcp/", ApiEndpoint = "https://api.github.com/", Token = Token }))];
        yield return [(Action<GitHubOptions>)(o => o.Instances.Add(new GitHubInstance { Name = "a", Endpoint = "ftp://g/mcp", ApiEndpoint = "https://api.github.com/", Token = Token }))];
        yield return [(Action<GitHubOptions>)(o => o.Instances.Add(new GitHubInstance { Name = "a", Endpoint = $"https://u:{Token}@g/mcp/", ApiEndpoint = "https://api.github.com/", Token = Token }))];
        yield return [(Action<GitHubOptions>)(o => o.Instances.Add(new GitHubInstance { Name = "a", Endpoint = "https://g/mcp/", ApiEndpoint = "https://api.github.com/", Token = " " }))];
        yield return [(Action<GitHubOptions>)(o => o.Instances.Add(new GitHubInstance { Name = "a", Endpoint = "https://g/mcp/", Token = Token }))];
        yield return [(Action<GitHubOptions>)(o => o.Instances.Add(new GitHubInstance { Name = "a", Endpoint = "https://g/mcp/", ApiEndpoint = "/api/v3/", Token = Token }))];
        yield return [(Action<GitHubOptions>)(o => o.Instances.Add(new GitHubInstance { Name = "a", Endpoint = "https://g/mcp/", ApiEndpoint = "ftp://g/api", Token = Token }))];
        yield return [(Action<GitHubOptions>)(o => o.Instances.Add(new GitHubInstance { Name = "a", Endpoint = "https://g/mcp/", ApiEndpoint = $"https://u:{Token}@g/api/v3/", Token = Token }))];
        yield return [(Action<GitHubOptions>)(o => o.Instances.Add(new GitHubInstance { Name = "a", Endpoint = "https://g/mcp/", ApiEndpoint = "https://g/api/v3?x=1", Token = Token }))];
        yield return [(Action<GitHubOptions>)(o => o.Instances.Add(new GitHubInstance { Name = "a", Endpoint = "https://g/mcp/", ApiEndpoint = "https://g/api/v3/#f", Token = Token }))];
    }

    [TestMethod]
    public void WithGitHubTools_MissingApiEndpoint_NamesProperty()
    {
        ArgumentException ex = Assert.ThrowsExactly<ArgumentException>(() => CreateServices(o =>
            o.Instances.Add(new GitHubInstance { Name = "a", Endpoint = "https://g/mcp/", Token = Token })));
        Assert.StartsWith("GitHub instance 'a' ApiEndpoint must be an absolute http or https URI", ex.Message);
    }

    [TestMethod]
    public void WithGitHubTools_ApiEndpoint_IsNormalizedWithTrailingSlash()
    {
        GitHubInstance instance = Instance("enterprise");
        instance.ApiEndpoint = " https://ghe.example/api/v3 ";
        using ServiceProvider provider = CreateServices(o => o.Instances.Add(instance)).BuildServiceProvider();

        Assert.AreEqual("https://ghe.example/api/v3/", provider.GetRequiredService<GitHubOptions>().Instances[0].ApiEndpoint);
    }

    [TestMethod]
    [DynamicData(nameof(InvalidOptions))]
    public void WithGitHubTools_InvalidOptions_ThrowWithoutToken(Action<GitHubOptions> configure)
    {
        ArgumentException ex = Assert.ThrowsExactly<ArgumentException>(() => CreateServices(configure));
        Assert.DoesNotContain(Token, ex.Message);
        Assert.Contains("GitHub", ex.Message);
    }

    [TestMethod]
    public void WithGitHubTools_NullArguments_Throw()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new ServiceCollection().AddMcpServer().WithGitHubTools(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => GitHubMcpServerBuilderExtensions.WithGitHubTools(null!, _ => { }));
    }

    [TestMethod]
    public void WithGitHubTools_StoresValidatedCopy()
    {
        GitHubInstance original = Instance(" public ");
        original.Token = $" {Token} ";
        GitHubOptions? source = null;
        using ServiceProvider provider = CreateServices(o => { o.Instances.Add(original); source = o; }).BuildServiceProvider();

        original.Token = "changed";
        original.Endpoint = "https://changed/";
        source!.Instances.Add(Instance("enterprise"));

        GitHubOptions stored = provider.GetRequiredService<GitHubOptions>();
        Assert.HasCount(1, stored.Instances);
        Assert.AreEqual("public", stored.Instances[0].Name);
        Assert.AreEqual(Token, stored.Instances[0].Token);
        Assert.AreEqual("https://api.githubcopilot.com/mcp/", stored.Instances[0].Endpoint);
        Assert.AreEqual("https://api.github.com/", stored.Instances[0].ApiEndpoint);
    }

    [TestMethod]
    public void TransportOptions_UseStreamableHttpAndBearerAuthorization()
    {
        HttpClientTransportOptions options = McpRelayPool.CreateTransportOptions("GitHub", GitHubMcpServerBuilderExtensions.ToTarget(Instance()));

        Assert.AreEqual(new Uri("https://api.githubcopilot.com/mcp/"), options.Endpoint);
        Assert.AreEqual(HttpTransportMode.StreamableHttp, options.TransportMode);
        Assert.AreEqual("github-public", options.Name);
        Assert.AreEqual($"Bearer {Token}", options.AdditionalHeaders!["Authorization"]);
    }

    [TestMethod]
    public void WithGitHubTools_WithJenkinsAndRedmineTools_CoexistWithoutNameConflict()
    {
        ServiceCollection services = new();
        services.AddMcpServer()
            .WithRedmineTools(o => { o.ApiEndpoint = "https://redmine.example/"; o.ApiKey = "k"; })
            .WithJenkinsTools(o => o.Instances.Add(new JenkinsInstance { Name = "prod", Endpoint = "http://j/mcp-server/mcp", User = "u", ApiToken = "t" }))
            .WithGitHubTools(o => o.Instances.Add(Instance()));
        using ServiceProvider provider = services.BuildServiceProvider();
        List<string> names = [.. provider.GetServices<McpServerTool>().Select(t => t.ProtocolTool.Name)];

        Assert.HasCount(names.Distinct().Count(), names);
        Assert.Contains("jenkins_call_tool", names);
        Assert.Contains("github_call_tool", names);
        Assert.AreEqual("GitHub", provider.GetRequiredKeyedService<McpRelayPool>("github").Label);
        Assert.AreEqual("Jenkins", provider.GetRequiredKeyedService<McpRelayPool>("jenkins").Label);
    }
}
