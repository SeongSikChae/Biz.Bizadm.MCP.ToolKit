using Biz.Bizadm.MCP.ToolKit.Redmine;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Biz.Bizadm.MCP.ToolKit.Tests.Redmine;

[TestClass]
public sealed class RedmineV7RegistrationTests
{
    private static ServiceCollection CreateServices(Action<RedmineOptions> configure)
    {
        ServiceCollection services = new();
        services.AddMcpServer().WithRedmineTools(configure);
        return services;
    }

    private static void Valid(RedmineOptions o)
    {
        o.ApiEndpoint = "https://redmine.example/";
        o.ApiKey = "secret-key";
    }

    [TestMethod]
    public void Options_DefaultToRedmine22Textile()
    {
        RedmineOptions options = new();
        Assert.AreEqual(RedmineVersion.V2_2, options.Version);
        Assert.AreEqual(RedmineTextFormatting.Textile, options.TextFormatting);
    }

    [TestMethod]
    public void WithRedmineTools_V22WithCommonMark_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CreateServices(o =>
        {
            Valid(o);
            o.TextFormatting = RedmineTextFormatting.CommonMark;
        }));
    }

    [TestMethod]
    public void WithRedmineTools_UndefinedEnums_Throw()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CreateServices(o =>
        {
            Valid(o);
            o.Version = (RedmineVersion)99;
        }));
        Assert.ThrowsExactly<ArgumentException>(() => CreateServices(o =>
        {
            Valid(o);
            o.TextFormatting = (RedmineTextFormatting)99;
        }));
    }

    private static readonly string[] V7ToolNames =
    [
        "list_issues", "get_issue", "create_issue", "create_subtask", "update_issue",
        "delete_issue", "add_issue_watcher", "remove_issue_watcher",
        "list_issue_relations", "create_issue_relation", "delete_issue_relation",
        "list_time_entries", "get_time_entry", "create_time_entry", "update_time_entry", "delete_time_entry",
        "update_journal",
        "list_wiki_pages", "get_wiki_page", "put_wiki_page", "get_wiki_page_version", "delete_wiki_page",
        "list_projects", "get_project",
        "get_current_user", "list_users", "get_user",
        "list_issue_statuses", "list_trackers", "list_issue_priorities", "list_time_entry_activities",
        "download_attachment", "upload_file", "update_attachment", "delete_attachment",
        "get_text_guide",
        "list_versions", "get_version", "create_version", "update_version", "delete_version",
        "list_issue_categories", "get_issue_category", "create_issue_category", "update_issue_category", "delete_issue_category",
        "list_memberships", "get_membership", "create_membership", "update_membership", "delete_membership",
        "search", "list_queries",
        "list_roles", "get_role", "list_groups", "get_group", "get_my_account",
        "list_custom_fields",
        "list_project_files", "list_news",
    ];

    // 설계 0002 D7 + 0005 Annotation 표: ReadOnly, Idempotent, Destructive, OpenWorld
    private static (bool ReadOnly, bool Idempotent, bool Destructive, bool OpenWorld) ExpectedV7(string name) => name switch
    {
        "get_text_guide" => (true, true, false, false),
        "create_issue" or "create_subtask" or "create_issue_relation" or "create_time_entry" or "upload_file"
            or "create_version" or "create_issue_category" or "create_membership" or "add_issue_watcher" => (false, false, false, true),
        "update_issue" or "update_time_entry" => (false, false, true, true),
        "put_wiki_page" or "update_version" or "update_issue_category" or "update_membership" or "update_journal" or "update_attachment" => (false, true, true, true),
        _ when name.StartsWith("delete_", StringComparison.Ordinal) || name == "remove_issue_watcher" => (false, true, true, true),
        _ => (true, true, false, true),
    };

    private static void V7(RedmineOptions o)
    {
        Valid(o);
        o.Version = RedmineVersion.V7_0;
    }

    [TestMethod]
    public void WithRedmineTools_V7_Registers61UniqueTools()
    {
        using ServiceProvider provider = CreateServices(V7).BuildServiceProvider();
        List<string> names = provider.GetServices<McpServerTool>().Select(t => t.ProtocolTool.Name).ToList();

        Assert.HasCount(61, names);
        Assert.HasCount(61, V7ToolNames);
        CollectionAssert.AllItemsAreUnique(names);
        CollectionAssert.AreEquivalent(V7ToolNames, names);
    }

    [TestMethod]
    public void WithRedmineTools_V7_AnnotationsMatchDesign()
    {
        using ServiceProvider provider = CreateServices(V7).BuildServiceProvider();
        foreach (McpServerTool tool in provider.GetServices<McpServerTool>())
        {
            string name = tool.ProtocolTool.Name;
            var annotations = tool.ProtocolTool.Annotations!;
            var expected = ExpectedV7(name);
            Assert.AreEqual(expected.ReadOnly, annotations.ReadOnlyHint, name);
            Assert.AreEqual(expected.Idempotent, annotations.IdempotentHint, name);
            Assert.AreEqual(expected.Destructive, annotations.DestructiveHint, name);
            Assert.AreEqual(expected.OpenWorld, annotations.OpenWorldHint, name);
        }
    }

    [TestMethod]
    public void WithRedmineTools_V7_DescriptionsHaveNo22Restrictions()
    {
        using ServiceProvider provider = CreateServices(V7).BuildServiceProvider();
        foreach (McpServerTool tool in provider.GetServices<McpServerTool>())
        {
            string schema = tool.ProtocolTool.InputSchema.GetRawText();
            string text = tool.ProtocolTool.Description + schema;
            Assert.IsFalse(text.Contains("2.2", StringComparison.Ordinal), tool.ProtocolTool.Name);
        }
    }

    [TestMethod]
    public void WithRedmineTools_V7_TextGuideUsesConfiguredFormat()
    {
        ServiceCollection services = CreateServices(o =>
        {
            V7(o);
            o.TextFormatting = RedmineTextFormatting.CommonMark;
        });
        services.AddTransient<Biz.Bizadm.MCP.ToolKit.Tools.Redmine.V7.TextGuideToolsV7>();
        using ServiceProvider provider = services.BuildServiceProvider();

        string guide = provider.GetRequiredService<Biz.Bizadm.MCP.ToolKit.Tools.Redmine.V7.TextGuideToolsV7>().GetTextGuide("issue");

        StringAssert.Contains(guide, "CommonMark", StringComparison.Ordinal);
    }

    [TestMethod]
    public void WithRedmineTools_V22Explicit_Registers27Tools()
    {
        using ServiceProvider provider = CreateServices(o =>
        {
            Valid(o);
            o.Version = RedmineVersion.V2_2;
        }).BuildServiceProvider();

        Assert.HasCount(27, provider.GetServices<McpServerTool>().ToList());
    }
}
