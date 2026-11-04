using Biz.Bizadm.MCP.ToolKit.Tools.AgentResource;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Biz.Bizadm.MCP.ToolKit.Tests.AgentResource;

[TestClass]
public sealed class RuleDocumentToolsTests
{
    [TestMethod]
    public void CreateRuleDocument_ThenListAndGet_ReturnsContent()
    {
        using TempRoot root = new();
        RuleDocumentTools tools = new(root.Options);

        Assert.AreEqual("dotnet/naming.md", tools.CreateRuleDocument("dotnet/naming", "---\ndescription: d\n---\nbody").Path);

        var rule = tools.ListRuleDocuments().Rules.Single();
        Assert.AreEqual("dotnet/naming.md", rule.Path);
        Assert.AreEqual("d", rule.Description);
        StringAssert.Contains(tools.GetRuleDocument("dotnet\\naming.md").Content, "body");
        Assert.IsTrue(File.Exists(Path.Combine(root.RootPath, "rules", "dotnet", "naming.md")));
    }

    [TestMethod]
    public void CreateRuleDocument_Existing_Throws()
    {
        using TempRoot root = new();
        RuleDocumentTools tools = new(root.Options);
        tools.CreateRuleDocument("AGENTS.md", "a");
        Assert.ThrowsExactly<McpException>(() => tools.CreateRuleDocument("AGENTS", "b"));
        Assert.AreEqual("a", tools.GetRuleDocument("AGENTS").Content);
    }

    [TestMethod]
    public void CreateRuleDocument_DirectoryWithSameName_ThrowsMcpException()
    {
        using TempRoot root = new();
        Directory.CreateDirectory(Path.Combine(root.RootPath, "rules", "foo.md"));
        Assert.ThrowsExactly<McpException>(() => new RuleDocumentTools(root.Options).CreateRuleDocument("foo", "x"));
    }

    [TestMethod]
    public void UpdateRuleDocument_ThenDelete_ChangesAndRemoves()
    {
        using TempRoot root = new();
        RuleDocumentTools tools = new(root.Options);
        tools.CreateRuleDocument("a.md", "old");

        tools.UpdateRuleDocument("a.md", "new");
        Assert.AreEqual("new", tools.GetRuleDocument("a.md").Content);

        tools.DeleteRuleDocument("a.md");
        Assert.ThrowsExactly<McpException>(() => tools.GetRuleDocument("a.md"));
        Assert.AreEqual(0, tools.ListRuleDocuments().Rules.Count);
    }

    [TestMethod]
    public void MissingDocument_Throws()
    {
        using TempRoot root = new();
        RuleDocumentTools tools = new(root.Options);
        Assert.ThrowsExactly<McpException>(() => tools.GetRuleDocument("none.md"));
        Assert.ThrowsExactly<McpException>(() => tools.UpdateRuleDocument("none.md", "x"));
        Assert.ThrowsExactly<McpException>(() => tools.DeleteRuleDocument("none.md"));
    }

    [TestMethod]
    public void ListRuleDocuments_NoRulesDirectory_ReturnsEmpty()
    {
        using TempRoot root = new();
        Assert.AreEqual(0, new RuleDocumentTools(root.Options).ListRuleDocuments().Rules.Count);
    }

    [TestMethod]
    [DataRow("../x.md")]
    [DataRow("C:/x.md")]
    [DataRow("a/../../x.md")]
    [DataRow("/../x.md")]
    [DataRow("")]
    [DataRow("x.txt")]
    public void GetRuleDocument_InvalidName_Throws(string name)
    {
        using TempRoot root = new();
        Assert.ThrowsExactly<McpException>(() => new RuleDocumentTools(root.Options).GetRuleDocument(name));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("relative/path")]
    public void WithAgentResourceTools_InvalidRootPath_Throws(string rootPath)
    {
        ServiceCollection services = new();
        Assert.ThrowsExactly<ArgumentException>(() => services.AddMcpServer().WithAgentResourceTools(o => o.RootPath = rootPath));
    }

    [TestMethod]
    public void WithAgentResourceTools_RegistersRuleTools()
    {
        using TempRoot root = new();
        ServiceCollection services = new();
        services.AddMcpServer().WithAgentResourceTools(o => o.RootPath = root.RootPath);
        using ServiceProvider provider = services.BuildServiceProvider();
        List<string> names = provider.GetServices<McpServerTool>().Select(t => t.ProtocolTool.Name).ToList();
        CollectionAssert.Contains(names, "list_rule_documents");
        CollectionAssert.Contains(names, "delete_rule_document");
    }
}
