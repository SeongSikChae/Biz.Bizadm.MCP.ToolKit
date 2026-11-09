using Biz.Bizadm.MCP.ToolKit.Jenkins;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Biz.Bizadm.MCP.ToolKit.Tests.Jenkins;

[TestClass]
public sealed class JenkinsRegistrationTests
{
    private const string Token = "secret-api-token";

    private static JenkinsInstance Instance(string name = "prod") => new()
    {
        Name = name,
        Endpoint = "http://jenkins.example:8080/mcp-server/mcp",
        User = "builder",
        ApiToken = Token,
        Description = "운영 빌드",
    };

    private static ServiceCollection CreateServices(Action<JenkinsOptions> configure)
    {
        ServiceCollection services = new();
        services.AddMcpServer().WithJenkinsTools(configure);
        return services;
    }

    [TestMethod]
    public void WithJenkinsTools_RegistersThreeGatewayTools()
    {
        using ServiceProvider provider = CreateServices(o => o.Instances.Add(Instance())).BuildServiceProvider();
        string[] names = [.. provider.GetServices<McpServerTool>().Select(t => t.ProtocolTool.Name).Order()];

        CollectionAssert.AreEqual(new[] { "jenkins_call_tool", "jenkins_list_instances", "jenkins_list_tools" }, names);
    }

    [TestMethod]
    public void WithJenkinsTools_AnnotationsMatchDesign()
    {
        using ServiceProvider provider = CreateServices(o => o.Instances.Add(Instance())).BuildServiceProvider();
        Dictionary<string, McpServerTool> tools = provider.GetServices<McpServerTool>().ToDictionary(t => t.ProtocolTool.Name);

        Assert.IsTrue(tools["jenkins_list_instances"].ProtocolTool.Annotations!.ReadOnlyHint);
        Assert.IsTrue(tools["jenkins_list_tools"].ProtocolTool.Annotations!.ReadOnlyHint);
        Assert.IsFalse(tools["jenkins_call_tool"].ProtocolTool.Annotations!.ReadOnlyHint);
        Assert.IsTrue(tools["jenkins_call_tool"].ProtocolTool.Annotations!.DestructiveHint);
    }

    public static IEnumerable<object[]> InvalidOptions()
    {
        yield return [(Action<JenkinsOptions>)(o => { })];
        yield return [(Action<JenkinsOptions>)(o => o.Instances = null!)];
        yield return [(Action<JenkinsOptions>)(o => o.Instances.Add(null!))];
        yield return [(Action<JenkinsOptions>)(o => o.Instances.Add(new JenkinsInstance { Name = " ", Endpoint = "http://j/mcp-server/mcp", User = "u", ApiToken = Token }))];
        yield return [(Action<JenkinsOptions>)(o => { o.Instances.Add(Instance("prod")); o.Instances.Add(Instance("PROD")); })];
        yield return [(Action<JenkinsOptions>)(o => o.Instances.Add(new JenkinsInstance { Name = "a", Endpoint = "/mcp-server/mcp", User = "u", ApiToken = Token }))];
        yield return [(Action<JenkinsOptions>)(o => o.Instances.Add(new JenkinsInstance { Name = "a", Endpoint = "ftp://j/mcp", User = "u", ApiToken = Token }))];
        yield return [(Action<JenkinsOptions>)(o => o.Instances.Add(new JenkinsInstance { Name = "a", Endpoint = $"http://u:{Token}@j:8080/mcp-server/mcp", User = "u", ApiToken = Token }))];
        yield return [(Action<JenkinsOptions>)(o => o.Instances.Add(new JenkinsInstance { Name = "a", Endpoint = "http://j/mcp", User = " ", ApiToken = Token }))];
        yield return [(Action<JenkinsOptions>)(o => o.Instances.Add(new JenkinsInstance { Name = "a", Endpoint = "http://j/mcp", User = "u", ApiToken = " " }))];
    }

    [TestMethod]
    [DynamicData(nameof(InvalidOptions))]
    public void WithJenkinsTools_InvalidOptions_ThrowWithoutToken(Action<JenkinsOptions> configure)
    {
        ArgumentException ex = Assert.ThrowsExactly<ArgumentException>(() => CreateServices(configure));
        Assert.DoesNotContain(Token, ex.Message);
    }

    [TestMethod]
    public void WithJenkinsTools_NullArguments_Throw()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new ServiceCollection().AddMcpServer().WithJenkinsTools(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => JenkinsMcpServerBuilderExtensions.WithJenkinsTools(null!, _ => { }));
    }

    [TestMethod]
    public void WithJenkinsTools_StoresValidatedCopy()
    {
        JenkinsInstance original = Instance(" prod ");
        JenkinsOptions? source = null;
        using ServiceProvider provider = CreateServices(o => { o.Instances.Add(original); source = o; }).BuildServiceProvider();

        original.ApiToken = "changed";
        original.Endpoint = "http://changed/";
        source!.Instances.Add(Instance("dev"));

        JenkinsOptions stored = provider.GetRequiredService<JenkinsOptions>();
        Assert.HasCount(1, stored.Instances);
        Assert.AreEqual("prod", stored.Instances[0].Name);
        Assert.AreEqual(Token, stored.Instances[0].ApiToken);
        Assert.AreEqual("http://jenkins.example:8080/mcp-server/mcp", stored.Instances[0].Endpoint);
    }

    [TestMethod]
    public void WithJenkinsTools_WithRedmineTools_CoexistWithoutNameConflict()
    {
        ServiceCollection services = new();
        services.AddMcpServer()
            .WithRedmineTools(o => { o.ApiEndpoint = "https://redmine.example/"; o.ApiKey = "k"; })
            .WithJenkinsTools(o => o.Instances.Add(Instance()));
        using ServiceProvider provider = services.BuildServiceProvider();
        List<string> names = [.. provider.GetServices<McpServerTool>().Select(t => t.ProtocolTool.Name)];

        Assert.HasCount(names.Distinct().Count(), names);
        Assert.Contains("jenkins_call_tool", names);
    }
}
