using Biz.Bizadm.MCP.ToolKit.Redmine;
using Biz.Bizadm.MCP.ToolKit.Tools.Redmine;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Biz.Bizadm.MCP.ToolKit.Tests.Redmine;

[TestClass]
public sealed class RedmineRegistrationTests
{
    private static readonly string[] ToolNames =
    [
        "list_issues", "get_issue", "create_issue", "create_subtask", "update_issue",
        "list_issue_relations", "create_issue_relation", "delete_issue_relation",
        "list_time_entries", "get_time_entry", "create_time_entry", "update_time_entry",
        "list_wiki_pages", "get_wiki_page", "put_wiki_page",
        "list_projects", "get_project",
        "get_current_user", "list_users", "get_user",
        "list_issue_statuses", "list_trackers", "list_issue_priorities", "list_time_entry_activities",
        "download_attachment", "upload_file",
        "get_text_guide",
    ];

    // 설계 0002 Annotation 표: ReadOnly, Idempotent, Destructive, OpenWorld
    private static (bool ReadOnly, bool Idempotent, bool Destructive, bool OpenWorld) Expected(string name) => name switch
    {
        "get_text_guide" => (true, true, false, false),
        "create_issue" or "create_subtask" or "create_issue_relation" or "create_time_entry" or "upload_file" => (false, false, false, true),
        "update_issue" or "update_time_entry" => (false, false, true, true),
        "put_wiki_page" or "delete_issue_relation" => (false, true, true, true),
        _ => (true, true, false, true),
    };

    private static ServiceCollection CreateServices(Action<RedmineOptions> configure)
    {
        ServiceCollection services = new();
        services.AddMcpServer().WithRedmineTools(configure);
        return services;
    }

    private static void Valid(RedmineOptions o)
    {
        o.ApiEndpoint = "https://redmine.example/redmine";
        o.ApiKey = "secret-key";
    }

    [TestMethod]
    public void WithRedmineTools_RegistersAll27Tools()
    {
        using ServiceProvider provider = CreateServices(Valid).BuildServiceProvider();
        List<string> names = provider.GetServices<McpServerTool>().Select(t => t.ProtocolTool.Name).ToList();
        Assert.HasCount(27, names);
        CollectionAssert.AreEquivalent(ToolNames, names);
    }

    [TestMethod]
    public void ToolAnnotations_MatchDesignTable()
    {
        using ServiceProvider provider = CreateServices(Valid).BuildServiceProvider();
        foreach (McpServerTool tool in provider.GetServices<McpServerTool>())
        {
            string name = tool.ProtocolTool.Name;
            var annotations = tool.ProtocolTool.Annotations!;
            var expected = Expected(name);
            Assert.AreEqual(expected.ReadOnly, annotations.ReadOnlyHint, name);
            Assert.AreEqual(expected.Idempotent, annotations.IdempotentHint, name);
            Assert.AreEqual(expected.Destructive, annotations.DestructiveHint, name);
            Assert.AreEqual(expected.OpenWorld, annotations.OpenWorldHint, name);
        }
    }

    [TestMethod]
    public void WithRedmineTools_ResolvesSingletonClientAndToolInstances()
    {
        ServiceCollection services = CreateServices(Valid);
        services.AddTransient<IssueTools>();
        using ServiceProvider provider = services.BuildServiceProvider();

        RedmineClient first = provider.GetRequiredService<RedmineClient>();
        Assert.AreSame(first, provider.GetRequiredService<RedmineClient>());
        Assert.AreEqual("https://redmine.example/redmine", provider.GetRequiredService<RedmineOptions>().ApiEndpoint);
        Assert.IsNotNull(provider.GetRequiredService<IssueTools>());
    }

    [TestMethod]
    public void WithRedmineTools_NullArguments_Throw()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => RedmineMcpServerBuilderExtensions.WithRedmineTools(null!, Valid));
        ServiceCollection services = new();
        Assert.ThrowsExactly<ArgumentNullException>(() => services.AddMcpServer().WithRedmineTools(null!));
    }

    [TestMethod]
    [DataRow("", "secret-key")]
    [DataRow("redmine/api", "secret-key")]
    [DataRow("ftp://redmine.example/", "secret-key")]
    [DataRow("https://redmine.example/", "")]
    [DataRow("https://redmine.example/", "  ")]
    public void WithRedmineTools_InvalidOptions_Throw(string endpoint, string apiKey)
    {
        Assert.ThrowsExactly<ArgumentException>(() => CreateServices(o =>
        {
            o.ApiEndpoint = endpoint;
            o.ApiKey = apiKey;
        }));
    }
}
