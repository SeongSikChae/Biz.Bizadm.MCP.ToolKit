using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.Redmine;
using Biz.Bizadm.MCP.ToolKit.Tools.Redmine.V7;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tests.Redmine.V7;

[TestClass]
public sealed class IssueToolsV7Tests
{
    [TestMethod]
    public async Task ListIssues_SendsV7Filters()
    {
        using V7RecordingHandler handler = new();
        IssueToolsV7 tools = new(handler.CreateClient());

        await tools.ListIssues(
            projectId: "demo",
            parentId: 9,
            issueIds: "1,2",
            createdOn: ">=2026-01-01",
            queryId: 4,
            sort: "updated_on:desc",
            include: "relations",
            cancellationToken: CancellationToken.None);

        string query = Uri.UnescapeDataString(handler.Last.Uri.Query);
        StringAssert.Contains(query, "project_id=demo", StringComparison.Ordinal);
        StringAssert.Contains(query, "parent_id=9", StringComparison.Ordinal);
        StringAssert.Contains(query, "issue_id=1,2", StringComparison.Ordinal);
        StringAssert.Contains(query, "created_on=>=2026-01-01", StringComparison.Ordinal);
        StringAssert.Contains(query, "query_id=4", StringComparison.Ordinal);
        StringAssert.Contains(query, "sort=updated_on:desc", StringComparison.Ordinal);
        StringAssert.Contains(query, "include=relations", StringComparison.Ordinal);
        StringAssert.Contains(query, "limit=25", StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task GetIssue_PassesWatchersInclude()
    {
        using V7RecordingHandler handler = new();
        IssueToolsV7 tools = new(handler.CreateClient());

        await tools.GetIssue(5, "watchers,allowed_statuses", CancellationToken.None);

        Assert.AreEqual("https://redmine.example/issues/5.json?include=watchers%2Callowed_statuses", handler.Last.Uri.ToString());
    }

    [TestMethod]
    public async Task CreateIssue_WritesV7Fields()
    {
        using V7RecordingHandler handler = new();
        IssueToolsV7 tools = new(handler.CreateClient());

        await tools.CreateIssue(
            "demo",
            "Title",
            isPrivate: false,
            categoryId: 2,
            fixedVersionId: 3,
            customFields: [new CustomFieldValue(7, Value: "x")],
            watcherUserIds: [10, 11],
            cancellationToken: CancellationToken.None);

        Assert.AreEqual(HttpMethod.Post, handler.Last.Method);
        JsonObject issue = JsonNode.Parse(handler.Last.Body!)!["issue"]!.AsObject();
        Assert.AreEqual("demo", issue["project_id"]!.GetValue<string>());
        Assert.IsFalse(issue["is_private"]!.GetValue<bool>());
        Assert.AreEqual(2, issue["category_id"]!.GetValue<int>());
        Assert.AreEqual(3, issue["fixed_version_id"]!.GetValue<int>());
        Assert.AreEqual(7, issue["custom_fields"]![0]!["id"]!.GetValue<int>());
        Assert.AreEqual("[10,11]", issue["watcher_user_ids"]!.ToJsonString());
        Assert.IsFalse(issue.ContainsKey("private_notes"));
    }

    [TestMethod]
    public async Task CreateSubtask_WithoutProjectId_InheritsParentProject()
    {
        using V7RecordingHandler handler = new();
        handler.Enqueue("""{"issue":{"id":40,"project":{"id":7}}}""");
        IssueToolsV7 tools = new(handler.CreateClient());

        await tools.CreateSubtask(40, "Child", watcherUserIds: [3], cancellationToken: CancellationToken.None);

        Assert.HasCount(2, handler.Requests);
        JsonObject issue = JsonNode.Parse(handler.Last.Body!)!["issue"]!.AsObject();
        Assert.AreEqual(7, issue["project_id"]!.GetValue<int>());
        Assert.AreEqual(40, issue["parent_issue_id"]!.GetValue<int>());
        Assert.AreEqual("[3]", issue["watcher_user_ids"]!.ToJsonString());
    }

    [TestMethod]
    public async Task UpdateIssue_WritesPrivateNotes()
    {
        using V7RecordingHandler handler = new();
        IssueToolsV7 tools = new(handler.CreateClient());

        await tools.UpdateIssue(5, notes: "memo", privateNotes: true, cancellationToken: CancellationToken.None);

        Assert.AreEqual(HttpMethod.Put, handler.Last.Method);
        Assert.AreEqual("""{"issue":{"notes":"memo","private_notes":true}}""", handler.Last.Body);
    }

    [TestMethod]
    public async Task UpdateIssue_NoFields_Throws()
    {
        using V7RecordingHandler handler = new();
        IssueToolsV7 tools = new(handler.CreateClient());

        await Assert.ThrowsExactlyAsync<McpException>(() => tools.UpdateIssue(5, cancellationToken: CancellationToken.None));
        Assert.IsEmpty(handler.Requests);
    }

    [TestMethod]
    public async Task DeleteAndWatcherTools_UseExpectedRoutes()
    {
        using V7RecordingHandler handler = new();
        IssueToolsV7 tools = new(handler.CreateClient());

        await tools.DeleteIssue(5, CancellationToken.None);
        Assert.AreEqual(HttpMethod.Delete, handler.Last.Method);
        Assert.AreEqual("https://redmine.example/issues/5.json", handler.Last.Uri.ToString());

        await tools.AddIssueWatcher(5, 8, CancellationToken.None);
        Assert.AreEqual(HttpMethod.Post, handler.Last.Method);
        Assert.AreEqual("https://redmine.example/issues/5/watchers.json", handler.Last.Uri.ToString());
        Assert.AreEqual("""{"user_id":8}""", handler.Last.Body);

        await tools.RemoveIssueWatcher(5, 8, CancellationToken.None);
        Assert.AreEqual(HttpMethod.Delete, handler.Last.Method);
        Assert.AreEqual("https://redmine.example/issues/5/watchers/8.json", handler.Last.Uri.ToString());
    }

    [TestMethod]
    public async Task NonPositiveIds_ThrowWithoutRequest()
    {
        using V7RecordingHandler handler = new();
        IssueToolsV7 tools = new(handler.CreateClient());

        await Assert.ThrowsExactlyAsync<McpException>(() => tools.DeleteIssue(0, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<McpException>(() => tools.AddIssueWatcher(1, 0, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<McpException>(() => tools.RemoveIssueWatcher(0, 1, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<McpException>(() => tools.CreateSubtask(0, "x", cancellationToken: CancellationToken.None));
        Assert.IsEmpty(handler.Requests);
    }
}
