using Biz.Bizadm.MCP.ToolKit.Tools.AgentResource;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tests.AgentResource;

[TestClass]
public sealed class SkillDocumentToolsTests
{
    [TestMethod]
    public void CreateSkillDocument_ThenListAndGet_ReturnsContent()
    {
        using TempRoot root = new();
        SkillDocumentTools tools = new(root.Options);

        Assert.AreEqual("create-pr/SKILL.md", tools.CreateSkillDocument("create-pr", "---\nname: create-pr\ndescription: d\n---\nbody").Path);

        var skill = tools.ListSkillDocuments().Skills.Single();
        Assert.AreEqual("create-pr", skill.Path);
        Assert.AreEqual("create-pr", skill.Name);
        Assert.AreEqual("d", skill.Description);
        Assert.AreEqual(tools.GetSkillDocument("create-pr").Content, tools.GetSkillDocument("create-pr/SKILL.md").Content);
    }

    [TestMethod]
    public void CreateSkillDocument_Existing_Throws()
    {
        using TempRoot root = new();
        SkillDocumentTools tools = new(root.Options);
        tools.CreateSkillDocument("a", "x");
        Assert.ThrowsExactly<McpException>(() => tools.CreateSkillDocument("a/SKILL.md", "y"));
    }

    [TestMethod]
    public void UpdateThenDeleteSkillDocument_RemovesEmptyDirectory()
    {
        using TempRoot root = new();
        SkillDocumentTools tools = new(root.Options);
        tools.CreateSkillDocument("a", "old");
        tools.UpdateSkillDocument("a", "new");
        Assert.AreEqual("new", tools.GetSkillDocument("a").Content);

        tools.DeleteSkillDocument("a");
        Assert.IsFalse(Directory.Exists(Path.Combine(root.RootPath, "skills", "a")));
        Assert.IsTrue(Directory.Exists(Path.Combine(root.RootPath, "skills")));
        Assert.ThrowsExactly<McpException>(() => tools.GetSkillDocument("a"));
    }

    [TestMethod]
    public void DeleteSkillDocument_KeepsNonEmptyDirectory()
    {
        using TempRoot root = new();
        SkillDocumentTools tools = new(root.Options);
        tools.CreateSkillDocument("a", "x");
        File.WriteAllText(Path.Combine(root.RootPath, "skills", "a", "other.txt"), "keep");
        tools.DeleteSkillDocument("a");
        Assert.IsTrue(File.Exists(Path.Combine(root.RootPath, "skills", "a", "other.txt")));
    }

    [TestMethod]
    public void ListSkillDocuments_IgnoresSkillFileAtRoot()
    {
        using TempRoot root = new();
        Directory.CreateDirectory(Path.Combine(root.RootPath, "skills"));
        File.WriteAllText(Path.Combine(root.RootPath, "skills", "SKILL.md"), "x");
        Assert.AreEqual(0, new SkillDocumentTools(root.Options).ListSkillDocuments().Skills.Count);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("SKILL.md")]
    [DataRow("../x")]
    [DataRow("C:/x")]
    [DataRow("a/../../x")]
    [DataRow("./")]
    [DataRow(".")]
    [DataRow("...")]
    [DataRow(".. ")]
    [DataRow("create-pr.")]
    [DataRow("a /b")]
    [DataRow("a./b")]
    public void GetSkillDocument_InvalidName_Throws(string name)
    {
        using TempRoot root = new();
        Assert.ThrowsExactly<McpException>(() => new SkillDocumentTools(root.Options).GetSkillDocument(name));
    }

    [TestMethod]
    [DataRow("./")]
    [DataRow("...")]
    public void CreateSkillDocument_RootAlias_ThrowsAndWritesNothing(string name)
    {
        using TempRoot root = new();
        Assert.ThrowsExactly<McpException>(() => new SkillDocumentTools(root.Options).CreateSkillDocument(name, "x"));
        Assert.IsFalse(File.Exists(Path.Combine(root.RootPath, "skills", "SKILL.md")));
    }
}
