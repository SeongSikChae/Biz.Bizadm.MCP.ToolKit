using Biz.Bizadm.MCP.ToolKit.Tools.AgentResource;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tests.AgentResource;

[TestClass]
public sealed class ToolDocumentToolsTests
{
    [TestMethod]
    public void RegisterToolDocument_ThenListAndUsage_ReturnsFields()
    {
        using TempRoot root = new();
        ToolDocumentTools tools = new(root.Options);

        Assert.AreEqual("xml-parser/TOOL.md",
            tools.RegisterToolDocument("xml-parser", "Parses\r\nXML", "C#", "tools/xml-parser/XmlParser.dll", "dotnet XmlParser.dll\r\n--input a.xml").Path);

        var tool = tools.ListToolDocuments().Tools.Single();
        Assert.AreEqual("xml-parser", tool.Path);
        Assert.AreEqual("xml-parser", tool.Name);
        Assert.AreEqual("Parses XML", tool.Description);
        Assert.AreEqual("C#", tool.Language);
        Assert.AreEqual("tools/xml-parser/XmlParser.dll", tool.Location);

        var usage = tools.GetToolDocumentUsage("xml-parser/TOOL.md");
        Assert.AreEqual("xml-parser", usage.Name);
        Assert.AreEqual("tools/xml-parser/XmlParser.dll", usage.Location);
        Assert.AreEqual("dotnet XmlParser.dll\n--input a.xml", usage.Usage);
        Assert.IsFalse(File.ReadAllText(Path.Combine(root.RootPath, "tools", "xml-parser", "TOOL.md")).Contains('\r'));
    }

    [TestMethod]
    public void RegisterToolDocument_Existing_Throws()
    {
        using TempRoot root = new();
        ToolDocumentTools tools = new(root.Options);
        tools.RegisterToolDocument("a", "d", "C#", "l", "u");
        Assert.ThrowsExactly<McpException>(() => tools.RegisterToolDocument("a", "d", "C#", "l", "u"));
    }

    [TestMethod]
    [DataRow("", "C#", "l", "u")]
    [DataRow("d", " ", "l", "u")]
    [DataRow("d", "C#", "", "u")]
    [DataRow("d", "C#", "l", "")]
    public void RegisterToolDocument_MissingField_Throws(string description, string language, string location, string usage)
    {
        using TempRoot root = new();
        Assert.ThrowsExactly<McpException>(() => new ToolDocumentTools(root.Options).RegisterToolDocument("a", description, language, location, usage));
    }

    [TestMethod]
    public void UpdateThenDeleteToolDocument_KeepsLocationFile()
    {
        using TempRoot root = new();
        ToolDocumentTools tools = new(root.Options);
        string artifact = Path.Combine(root.RootPath, "artifact.exe");
        File.WriteAllText(artifact, "bin");
        tools.RegisterToolDocument("a", "d", "C#", artifact, "u");

        tools.UpdateToolDocument("a", "d2", "PowerShell", artifact, "u2");
        Assert.AreEqual("d2", tools.ListToolDocuments().Tools.Single().Description);
        Assert.AreEqual("u2", tools.GetToolDocumentUsage("a").Usage);

        tools.DeleteToolDocument("a");
        Assert.IsFalse(Directory.Exists(Path.Combine(root.RootPath, "tools", "a")));
        Assert.IsTrue(File.Exists(artifact));
        Assert.ThrowsExactly<McpException>(() => tools.GetToolDocumentUsage("a"));
        Assert.ThrowsExactly<McpException>(() => tools.UpdateToolDocument("a", "d", "C#", "l", "u"));
    }

    [TestMethod]
    public void GetToolDocumentRegistrationGuide_UsesSnakeCaseToolNames()
    {
        using TempRoot root = new();
        var guide = new ToolDocumentTools(root.Options).GetToolDocumentRegistrationGuide();
        Assert.AreEqual("tools/<name>/TOOL.md", guide.StorageLayout);
        Assert.AreEqual("register_tool_document", guide.RelatedTools.Register);
        Assert.HasCount(5, guide.RequiredFields);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("../x")]
    [DataRow("C:/x")]
    [DataRow("a/../../x")]
    [DataRow("./")]
    [DataRow("....")]
    public void GetToolDocumentUsage_InvalidName_Throws(string name)
    {
        using TempRoot root = new();
        Assert.ThrowsExactly<McpException>(() => new ToolDocumentTools(root.Options).GetToolDocumentUsage(name));
    }
}
