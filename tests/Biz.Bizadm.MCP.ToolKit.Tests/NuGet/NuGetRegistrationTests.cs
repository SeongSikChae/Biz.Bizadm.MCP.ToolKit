using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.NuGet;
using Biz.Bizadm.MCP.ToolKit.Tools.NuGet;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Biz.Bizadm.MCP.ToolKit.Tests.NuGet;

[TestClass]
public sealed class NuGetRegistrationTests
{
    private const string ApiKey = "secret-api-key";
    private const string Password = "secret-password";

    private static NuGetInstance Instance(string name = "internal") => new()
    {
        Name = name,
        ServiceIndex = "https://pkgs.example.com/v3/index.json",
        ApiKey = ApiKey,
        User = "builder",
        Password = Password,
        Description = "사내 피드",
    };

    private static ServiceCollection CreateServices(Action<NuGetOptions> configure)
    {
        ServiceCollection services = new();
        services.AddMcpServer().WithNuGetTools(configure);
        return services;
    }

    private static ArgumentException Fails(Action<NuGetOptions> configure)
        => Assert.ThrowsExactly<ArgumentException>(() => CreateServices(configure));

    [TestMethod]
    public void WithNuGetTools_RegistersNineToolsWithAnnotations()
    {
        using ServiceProvider provider = CreateServices(o => o.Instances.Add(Instance())).BuildServiceProvider();
        Dictionary<string, McpServerTool> tools = provider.GetServices<McpServerTool>().ToDictionary(t => t.ProtocolTool.Name);

        CollectionAssert.AreEqual(new[]
        {
            "nuget_check_vulnerabilities", "nuget_get_package_metadata", "nuget_list_instances", "nuget_list_versions",
            "nuget_push_package", "nuget_push_symbol_package", "nuget_relist_package", "nuget_search_packages", "nuget_unlist_package",
        }, tools.Keys.Order(StringComparer.Ordinal).ToArray());

        Assert.IsFalse(tools["nuget_list_instances"].ProtocolTool.Annotations!.OpenWorldHint);
        foreach (string name in new[] { "nuget_list_instances", "nuget_search_packages", "nuget_list_versions", "nuget_get_package_metadata", "nuget_check_vulnerabilities" })
        {
            Assert.IsTrue(tools[name].ProtocolTool.Annotations!.ReadOnlyHint, name);
            Assert.IsFalse(tools[name].ProtocolTool.Annotations!.DestructiveHint, name);
        }
        foreach (string name in new[] { "nuget_push_package", "nuget_push_symbol_package", "nuget_unlist_package", "nuget_relist_package" })
        {
            Assert.IsFalse(tools[name].ProtocolTool.Annotations!.ReadOnlyHint, name);
            Assert.IsTrue(tools[name].ProtocolTool.Annotations!.OpenWorldHint, name);
        }
        Assert.IsTrue(tools["nuget_unlist_package"].ProtocolTool.Annotations!.DestructiveHint);
        Assert.IsFalse(tools["nuget_push_package"].ProtocolTool.Annotations!.DestructiveHint);
        Assert.IsFalse(tools["nuget_push_package"].ProtocolTool.Annotations!.IdempotentHint);
        Assert.IsFalse(tools["nuget_push_symbol_package"].ProtocolTool.Annotations!.DestructiveHint);
        Assert.IsFalse(tools["nuget_push_symbol_package"].ProtocolTool.Annotations!.IdempotentHint);
        Assert.IsFalse(tools["nuget_relist_package"].ProtocolTool.Annotations!.DestructiveHint);
        StringAssert.Contains(tools["nuget_unlist_package"].ProtocolTool.Description, "일부 사설 피드(BaGet, Nexus 설정 등)는 버전을 실제로 삭제할 수 있다");
        StringAssert.Contains(tools["nuget_list_versions"].ProtocolTool.Description, "unlisted 버전도 포함");
        StringAssert.Contains(tools["nuget_search_packages"].ProtocolTool.Description, "SearchQueryService/3.5.0");
    }

    [TestMethod]
    public void ListInstances_HidesCredentials()
    {
        using ServiceProvider provider = CreateServices(o =>
        {
            o.Instances.Add(Instance());
            o.Instances.Add(new NuGetInstance { Name = "nuget.org", ServiceIndex = "https://api.nuget.org/v3/index.json" });
        }).BuildServiceProvider();
        NuGetTools tools = new(provider);

        string json = tools.ListInstances();

        Assert.IsFalse(json.Contains(ApiKey, StringComparison.Ordinal));
        Assert.IsFalse(json.Contains(Password, StringComparison.Ordinal));
        JsonArray items = JsonNode.Parse(json)!.AsArray();
        Assert.AreEqual("internal", (string?)items[0]!["name"]);
        Assert.AreEqual("https://pkgs.example.com/v3/index.json", (string?)items[0]!["serviceIndex"]);
        Assert.IsTrue((bool)items[0]!["canWrite"]!);
        Assert.IsFalse((bool)items[1]!["canWrite"]!);
        Assert.IsNull(items[1]!["description"]);
    }

    [TestMethod]
    public void Validate_NoInstances_Fails()
        => StringAssert.Contains(Fails(_ => { }).Message, "At least one NuGet instance is required");

    [TestMethod]
    public void Validate_DuplicateName_Fails()
        => StringAssert.Contains(Fails(o => { o.Instances.Add(Instance("a")); o.Instances.Add(Instance("A")); }).Message, "Duplicate NuGet instance name");

    [TestMethod]
    [DataRow("relative/index.json")]
    [DataRow("ftp://pkgs.example.com/index.json")]
    public void Validate_NotAbsoluteHttp_FailsWithServiceIndexProperty(string serviceIndex)
    {
        ArgumentException ex = Fails(o => o.Instances.Add(new NuGetInstance { Name = "x", ServiceIndex = serviceIndex }));
        StringAssert.Contains(ex.Message, "NuGet instance 'x' ServiceIndex must be an absolute http or https URI");
    }

    [TestMethod]
    public void Validate_UserInfo_Fails()
    {
        ArgumentException ex = Fails(o => o.Instances.Add(new NuGetInstance { Name = "x", ServiceIndex = "https://u:p@pkgs.example.com/index.json" }));
        StringAssert.Contains(ex.Message, "ServiceIndex must not contain user info. Use User and Password");
    }

    [TestMethod]
    public void Validate_UserWithoutPassword_Fails()
    {
        ArgumentException ex = Fails(o => o.Instances.Add(new NuGetInstance { Name = "x", ServiceIndex = "https://p/index.json", User = "u" }));
        StringAssert.Contains(ex.Message, "User and Password must be set together");
    }

    [TestMethod]
    public void Validate_UserWithColon_Fails()
    {
        ArgumentException ex = Fails(o => o.Instances.Add(new NuGetInstance { Name = "x", ServiceIndex = "https://p/index.json", User = "a:b", Password = "p" }));
        StringAssert.Contains(ex.Message, "User must not contain ':'");
    }

    [TestMethod]
    [DataRow("ApiKey")]
    [DataRow("Password")]
    public void Validate_ControlCharacters_FailsWithoutValue(string property)
    {
        const string value = "abc\r\nInjected: x";
        NuGetInstance instance = new() { Name = "x", ServiceIndex = "https://p/index.json", User = "u", Password = "p" };
        if (property == "ApiKey") instance.ApiKey = value; else instance.Password = value;

        ArgumentException ex = Fails(o => o.Instances.Add(instance));

        StringAssert.Contains(ex.Message, $"{property} must not contain control characters");
        Assert.IsFalse(ex.Message.Contains("Injected", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ExistingEndpointMessage_Unchanged()
    {
        ServiceCollection services = new();
        ArgumentException ex = Assert.ThrowsExactly<ArgumentException>(() => services.AddMcpServer().WithJenkinsTools(o =>
            o.Instances.Add(new Biz.Bizadm.MCP.ToolKit.Jenkins.JenkinsInstance { Name = "j", Endpoint = "relative", User = "u", ApiToken = "t" })));
        StringAssert.Contains(ex.Message, "Jenkins instance 'j' Endpoint must be an absolute http or https URI");
    }
}
