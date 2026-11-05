using Biz.Bizadm.MCP.ToolKit.Tools.Redmine.V7;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tests.Redmine.V7;

[TestClass]
public sealed class ProjectStructureToolsTests
{
    [TestMethod]
    public async Task VersionTools_UseExpectedRoutesAndBodies()
    {
        using V7RecordingHandler handler = new();
        VersionTools tools = new(handler.CreateClient());

        await tools.ListVersions("my proj", CancellationToken.None);
        AssertRequest(handler, HttpMethod.Get, "projects/my%20proj/versions.json", null);

        await tools.GetVersion(3, CancellationToken.None);
        AssertRequest(handler, HttpMethod.Get, "versions/3.json", null);

        await tools.CreateVersion("demo", "1.0", status: "open", sharing: "none", dueDate: "2026-12-31", cancellationToken: CancellationToken.None);
        AssertRequest(handler, HttpMethod.Post, "projects/demo/versions.json",
            """{"version":{"name":"1.0","status":"open","sharing":"none","due_date":"2026-12-31"}}""");

        await tools.UpdateVersion(3, status: "closed", cancellationToken: CancellationToken.None);
        AssertRequest(handler, HttpMethod.Put, "versions/3.json", """{"version":{"status":"closed"}}""");

        await tools.DeleteVersion(3, CancellationToken.None);
        AssertRequest(handler, HttpMethod.Delete, "versions/3.json", null);
    }

    [TestMethod]
    public async Task IssueCategoryTools_UseExpectedRoutesAndBodies()
    {
        using V7RecordingHandler handler = new();
        IssueCategoryTools tools = new(handler.CreateClient());

        await tools.ListIssueCategories("demo", CancellationToken.None);
        AssertRequest(handler, HttpMethod.Get, "projects/demo/issue_categories.json", null);

        await tools.GetIssueCategory(4, CancellationToken.None);
        AssertRequest(handler, HttpMethod.Get, "issue_categories/4.json", null);

        await tools.CreateIssueCategory("demo", "UI", assignedToId: 2, cancellationToken: CancellationToken.None);
        AssertRequest(handler, HttpMethod.Post, "projects/demo/issue_categories.json",
            """{"issue_category":{"name":"UI","assigned_to_id":2}}""");

        await tools.UpdateIssueCategory(4, name: "UX", cancellationToken: CancellationToken.None);
        AssertRequest(handler, HttpMethod.Put, "issue_categories/4.json", """{"issue_category":{"name":"UX"}}""");

        await tools.DeleteIssueCategory(4, cancellationToken: CancellationToken.None);
        AssertRequest(handler, HttpMethod.Delete, "issue_categories/4.json", null);

        await tools.DeleteIssueCategory(4, reassignToId: 5, cancellationToken: CancellationToken.None);
        AssertRequest(handler, HttpMethod.Delete, "issue_categories/4.json?reassign_to_id=5", null);
    }

    [TestMethod]
    public async Task MembershipTools_UseExpectedRoutesAndBodies()
    {
        using V7RecordingHandler handler = new();
        MembershipTools tools = new(handler.CreateClient());

        await tools.ListMemberships("demo", cancellationToken: CancellationToken.None);
        AssertRequest(handler, HttpMethod.Get, "projects/demo/memberships.json?limit=25&offset=0", null);

        await tools.GetMembership(6, CancellationToken.None);
        AssertRequest(handler, HttpMethod.Get, "memberships/6.json", null);

        await tools.CreateMembership("demo", 9, [3, 4], CancellationToken.None);
        AssertRequest(handler, HttpMethod.Post, "projects/demo/memberships.json",
            """{"membership":{"user_id":9,"role_ids":[3,4]}}""");

        await tools.UpdateMembership(6, [5], CancellationToken.None);
        AssertRequest(handler, HttpMethod.Put, "memberships/6.json", """{"membership":{"role_ids":[5]}}""");

        await tools.DeleteMembership(6, CancellationToken.None);
        AssertRequest(handler, HttpMethod.Delete, "memberships/6.json", null);
    }

    [TestMethod]
    public async Task InvalidInput_ThrowsWithoutRequest()
    {
        using V7RecordingHandler handler = new();
        VersionTools versions = new(handler.CreateClient());
        IssueCategoryTools categories = new(handler.CreateClient());
        MembershipTools memberships = new(handler.CreateClient());

        await Assert.ThrowsExactlyAsync<McpException>(() => versions.ListVersions(" ", CancellationToken.None));
        await Assert.ThrowsExactlyAsync<McpException>(() => versions.CreateVersion("demo", " ", cancellationToken: CancellationToken.None));
        await Assert.ThrowsExactlyAsync<McpException>(() => versions.UpdateVersion(3, cancellationToken: CancellationToken.None));
        await Assert.ThrowsExactlyAsync<McpException>(() => versions.GetVersion(0, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<McpException>(() => categories.UpdateIssueCategory(4, cancellationToken: CancellationToken.None));
        await Assert.ThrowsExactlyAsync<McpException>(() => categories.DeleteIssueCategory(0, cancellationToken: CancellationToken.None));
        await Assert.ThrowsExactlyAsync<McpException>(() => memberships.CreateMembership("demo", 9, [], CancellationToken.None));
        await Assert.ThrowsExactlyAsync<McpException>(() => memberships.CreateMembership("demo", 0, [3], CancellationToken.None));
        await Assert.ThrowsExactlyAsync<McpException>(() => memberships.UpdateMembership(6, [0], CancellationToken.None));
        Assert.IsEmpty(handler.Requests);
    }

    private static void AssertRequest(V7RecordingHandler handler, HttpMethod method, string relative, string? body)
    {
        Assert.AreEqual(method, handler.Last.Method);
        Assert.AreEqual(V7RecordingHandler.Endpoint + relative, handler.Last.Uri.AbsoluteUri);
        Assert.AreEqual(body, handler.Last.Body);
    }
}
