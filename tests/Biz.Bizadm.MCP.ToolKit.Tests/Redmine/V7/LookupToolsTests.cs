using Biz.Bizadm.MCP.ToolKit.Tools.Redmine.V7;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tests.Redmine.V7;

[TestClass]
public sealed class LookupToolsTests
{
    [TestMethod]
    public async Task Search_Global_SendsQueryAndTypes()
    {
        using V7RecordingHandler handler = new();
        SearchTools tools = new(handler.CreateClient());

        await tools.Search("배포 오류", titlesOnly: true, types: "issues, wiki_pages", cancellationToken: CancellationToken.None);

        Uri uri = handler.Last.Uri;
        Assert.AreEqual("/search.json", uri.AbsolutePath);
        string query = Uri.UnescapeDataString(uri.Query);
        StringAssert.Contains(query, "q=배포 오류", StringComparison.Ordinal);
        StringAssert.Contains(query, "titles_only=1", StringComparison.Ordinal);
        StringAssert.Contains(query, "issues=1", StringComparison.Ordinal);
        StringAssert.Contains(query, "wiki_pages=1", StringComparison.Ordinal);
        StringAssert.Contains(query, "limit=25", StringComparison.Ordinal);
        Assert.IsFalse(query.Contains("all_words", StringComparison.Ordinal));
        Assert.IsFalse(query.Contains("scope", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Search_Project_AllWordsFalse_SendsEmptyValue()
    {
        using V7RecordingHandler handler = new();
        SearchTools tools = new(handler.CreateClient());

        await tools.Search("x", projectId: "demo", allWords: false, scope: "subprojects", cancellationToken: CancellationToken.None);

        Assert.AreEqual("/projects/demo/search.json", handler.Last.Uri.AbsolutePath);
        string query = handler.Last.Uri.Query;
        StringAssert.EndsWith(query, "&all_words=", StringComparison.Ordinal);
        StringAssert.Contains(query, "scope=subprojects", StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task Search_AllWordsTrue_OmitsParameter()
    {
        using V7RecordingHandler handler = new();
        SearchTools tools = new(handler.CreateClient());

        await tools.Search("x", allWords: true, titlesOnly: false, cancellationToken: CancellationToken.None);

        Assert.IsFalse(handler.Last.Uri.Query.Contains("all_words", StringComparison.Ordinal));
        Assert.IsFalse(handler.Last.Uri.Query.Contains("titles_only", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Search_InvalidInput_ThrowsWithoutRequest()
    {
        using V7RecordingHandler handler = new();
        SearchTools tools = new(handler.CreateClient());

        await Assert.ThrowsExactlyAsync<McpException>(() => tools.Search(" ", cancellationToken: CancellationToken.None));
        Assert.IsEmpty(handler.Requests);
    }

    [TestMethod]
    public async Task ReadOnlyLookups_UseExpectedRoutes()
    {
        using V7RecordingHandler handler = new();
        SearchTools search = new(handler.CreateClient());
        PrincipalTools principals = new(handler.CreateClient());
        CustomFieldTools customFields = new(handler.CreateClient());
        ProjectContentTools content = new(handler.CreateClient());

        await search.ListQueries(cancellationToken: CancellationToken.None);
        AssertGet(handler, "queries.json?limit=25&offset=0");
        await principals.ListRoles(CancellationToken.None);
        AssertGet(handler, "roles.json");
        await principals.GetRole(3, CancellationToken.None);
        AssertGet(handler, "roles/3.json");
        await principals.ListGroups(CancellationToken.None);
        AssertGet(handler, "groups.json");
        await principals.GetGroup(4, CancellationToken.None);
        AssertGet(handler, "groups/4.json?include=users%2Cmemberships");
        await principals.GetMyAccount(CancellationToken.None);
        AssertGet(handler, "my/account.json");
        await customFields.ListCustomFields(CancellationToken.None);
        AssertGet(handler, "custom_fields.json");
        await content.ListProjectFiles("demo", CancellationToken.None);
        AssertGet(handler, "projects/demo/files.json");
        await content.ListNews(cancellationToken: CancellationToken.None);
        AssertGet(handler, "news.json?limit=25&offset=0");
        await content.ListNews("demo", limit: 5, cancellationToken: CancellationToken.None);
        AssertGet(handler, "projects/demo/news.json?limit=5&offset=0");
    }

    [TestMethod]
    public async Task Lookups_InvalidInput_ThrowWithoutRequest()
    {
        using V7RecordingHandler handler = new();
        PrincipalTools principals = new(handler.CreateClient());
        ProjectContentTools content = new(handler.CreateClient());

        await Assert.ThrowsExactlyAsync<McpException>(() => principals.GetRole(0, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<McpException>(() => principals.GetGroup(0, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<McpException>(() => content.ListProjectFiles(" ", CancellationToken.None));
        Assert.IsEmpty(handler.Requests);
    }

    private static void AssertGet(V7RecordingHandler handler, string relative)
    {
        Assert.AreEqual(HttpMethod.Get, handler.Last.Method);
        Assert.AreEqual(V7RecordingHandler.Endpoint + relative, handler.Last.Uri.AbsoluteUri);
    }
}
