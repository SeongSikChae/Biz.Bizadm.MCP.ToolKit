using Biz.Bizadm.MCP.ToolKit.AgentResource;
using Biz.Bizadm.MCP.ToolKit.Tools.AgentResource;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tests.AgentResource;

[TestClass]
public sealed class DocDocumentToolsTests
{
    [TestMethod]
    public void CreateDocDocument_EmptyCategory_AssignsSequentialNumbers()
    {
        using TempRoot root = new();
        DocDocumentTools tools = new(root.Options);

        Assert.AreEqual("auth/0001-login-방안.md", tools.CreateDocDocument("auth", "login", "방안", "a").Path);
        Assert.AreEqual("auth/0002-login-설계.md", tools.CreateDocDocument("auth", "login", "설계", "b", "done").Path);

        string text = File.ReadAllText(Path.Combine(root.RootPath, "docs", "auth", "0002-login-설계.md"));
        Assert.AreEqual("---\nstatus: done\ncategory: auth\nfeature: login\nphase: 설계\n---\n\nb\n", text);
    }

    [TestMethod]
    public void CreateDocDocument_ConcurrentDifferentFeatures_AssignsUniqueNumbers()
    {
        using TempRoot root = new();
        string[] paths = new string[20];

        // 요청마다 Tool 인스턴스가 새로 생기는 것과 같게 인스턴스를 따로 만든다.
        Parallel.For(0, paths.Length, new ParallelOptions { MaxDegreeOfParallelism = 8 },
            i => paths[i] = new DocDocumentTools(root.Options).CreateDocDocument("auth", $"f{i}", "방안", "x").Path);

        List<int> numbers = new DocDocumentTools(root.Options).ListDocDocuments("auth").Docs.Select(d => d.Number).ToList();
        CollectionAssert.AreEqual(Enumerable.Range(1, 20).ToList(), numbers);
        Assert.HasCount(20, paths.Distinct());
    }

    [TestMethod]
    public void CreateDocDocument_ExistingFiles_UsesNextAfterMax()
    {
        using TempRoot root = new();
        string directory = Path.Combine(root.RootPath, "docs", "storage");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "0005-x-방안.md"), "x");
        File.WriteAllText(Path.Combine(directory, "readme.md"), "x");

        Assert.AreEqual("storage/0006-storage-pool-개발.md",
            new DocDocumentTools(root.Options).CreateDocDocument("storage", "storage-pool", "개발", "c").Path);
    }

    [TestMethod]
    public void GetUpdateDelete_ByCategoryAndNumber()
    {
        using TempRoot root = new();
        DocDocumentTools tools = new(root.Options);
        tools.CreateDocDocument("auth", "login", "방안", "old");

        var doc = tools.GetDocDocument("auth", 1);
        Assert.AreEqual("auth/0001-login-방안.md", doc.Path);
        Assert.AreEqual(1, doc.Number);
        StringAssert.Contains(doc.Content, "old");

        tools.UpdateDocDocument("auth", 1, "new");
        var summary = tools.ListDocDocuments().Docs.Single();
        Assert.AreEqual("draft", summary.Status);
        Assert.AreEqual("login", summary.Feature);
        Assert.AreEqual("방안", summary.Phase);
        StringAssert.Contains(tools.GetDocDocument("auth", 1).Content, "new");

        tools.UpdateDocDocument("auth", 1, "new", "done");
        Assert.AreEqual("done", tools.ListDocDocuments().Docs.Single().Status);

        tools.DeleteDocDocument("auth", 1);
        Assert.ThrowsExactly<McpException>(() => tools.GetDocDocument("auth", 1));
    }

    [TestMethod]
    public void ListDocDocuments_FiltersAndSorts()
    {
        using TempRoot root = new();
        DocDocumentTools tools = new(root.Options);
        tools.CreateDocDocument("storage", "disk", "방안", "x");
        tools.CreateDocDocument("auth", "login", "방안", "x");
        tools.CreateDocDocument("auth", "logout", "방안", "x");

        CollectionAssert.AreEqual(
            new[] { "auth/0001-login-방안.md", "auth/0002-logout-방안.md", "storage/0001-disk-방안.md" },
            tools.ListDocDocuments().Docs.Select(d => d.Path).ToArray());
        Assert.HasCount(2, tools.ListDocDocuments(category: "AUTH").Docs);
        Assert.AreEqual("auth/0002-logout-방안.md", tools.ListDocDocuments(feature: "Logout").Docs.Single().Path);
        Assert.AreEqual(0, tools.ListDocDocuments("storage", "login").Docs.Count);
    }

    [TestMethod]
    public void NonAsciiDigitFileName_IsIgnored()
    {
        using TempRoot root = new();
        string directory = Path.Combine(root.RootPath, "docs", "auth");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "١٢٣٤-a-b.md"), "x");
        DocDocumentTools tools = new(root.Options);
        Assert.AreEqual(0, tools.ListDocDocuments().Docs.Count);
        Assert.AreEqual("auth/0001-login-방안.md", tools.CreateDocDocument("auth", "login", "방안", "x").Path);
    }

    [TestMethod]
    public void FindByNumber_DuplicateFiles_Throws()
    {
        using TempRoot root = new();
        string directory = Path.Combine(root.RootPath, "docs", "auth");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "0001-a-방안.md"), "x");
        File.WriteAllText(Path.Combine(directory, "0001-b-방안.md"), "x");
        Assert.ThrowsExactly<McpException>(() => new DocDocumentTools(root.Options).GetDocDocument("auth", 1));
    }

    [TestMethod]
    [DataRow("Auth", "login", "방안")]
    [DataRow("a/b", "login", "방안")]
    [DataRow("..", "login", "방안")]
    [DataRow("", "login", "방안")]
    [DataRow("auth", "", "방안")]
    [DataRow("auth", "log in", "방안")]
    [DataRow("auth", "a/b", "방안")]
    [DataRow("auth", "login", "a-b")]
    [DataRow("auth", "login", "a b")]
    [DataRow("auth", "login", "")]
    [DataRow("auth", "login", "..")]
    [DataRow("auth", "login", "a*b")]
    [DataRow("auth", "a?b", "방안")]
    public void CreateDocDocument_InvalidInput_Throws(string category, string feature, string phase)
    {
        using TempRoot root = new();
        Assert.ThrowsExactly<McpException>(() => new DocDocumentTools(root.Options).CreateDocDocument(category, feature, phase, "x"));
    }

    [TestMethod]
    public void CreateDocDocument_InvalidStatus_Throws()
    {
        using TempRoot root = new();
        DocDocumentTools tools = new(root.Options);
        Assert.ThrowsExactly<McpException>(() => tools.CreateDocDocument("auth", "login", "방안", "x", " "));
        Assert.ThrowsExactly<McpException>(() => tools.CreateDocDocument("auth", "login", "방안", "x", "a\nb"));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(10000)]
    [DataRow(2)]
    public void GetDocDocument_MissingOrOutOfRangeNumber_Throws(int number)
    {
        using TempRoot root = new();
        DocDocumentTools tools = new(root.Options);
        tools.CreateDocDocument("auth", "login", "방안", "x");
        Assert.ThrowsExactly<McpException>(() => tools.GetDocDocument("auth", number));
        Assert.ThrowsExactly<McpException>(() => tools.DeleteDocDocument("auth", number));
    }

    [TestMethod]
    public void ListDocDocuments_NoDocsDirectory_ReturnsEmpty()
    {
        using TempRoot root = new();
        Assert.AreEqual(0, new DocDocumentTools(new AgentResourceOptions { RootPath = root.RootPath }).ListDocDocuments().Docs.Count);
    }
}
