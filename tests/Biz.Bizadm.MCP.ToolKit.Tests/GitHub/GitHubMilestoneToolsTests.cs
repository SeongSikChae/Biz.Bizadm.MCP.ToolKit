using System.Net;

using Biz.Bizadm.MCP.ToolKit.GitHub;
using Biz.Bizadm.MCP.ToolKit.Tools.GitHub;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tests.GitHub;

[TestClass]
public sealed class GitHubMilestoneToolsTests
{
    public TestContext TestContext { get; set; } = null!;

    private static (GitHubMilestoneTools Tools, GitHubRecordingHandler Handler, GitHubRestClient Client) Create()
    {
        GitHubRecordingHandler handler = new();
        GitHubRestClient client = new(GitHubRestClientTests.Options(), handler);
        return (new GitHubMilestoneTools(client), handler, client);
    }

    private const string Milestone = """{"number":3,"title":"v1.8","state":"open","due_on":"2026-12-31T12:00:00Z"}""";

    [TestMethod]
    public async Task ListMilestones_SendsGivenQueryAndReturnsResponse()
    {
        (GitHubMilestoneTools tools, GitHubRecordingHandler handler, GitHubRestClient client) = Create();
        using GitHubRestClient _ = client;
        string body = "[" + Milestone + "]";
        handler.Enqueue(HttpStatusCode.OK, body);

        string result = await tools.ListMilestones("public", "o", "r", "all", "completeness", "desc", 50, 2, TestContext.CancellationToken);

        Assert.AreEqual(body, result);
        Assert.HasCount(1, handler.Requests);
        Assert.AreEqual(HttpMethod.Get, handler.Last.Method);
        Assert.AreEqual(new Uri("https://api.github.com/repos/o/r/milestones?state=all&sort=completeness&direction=desc&per_page=50&page=2"), handler.Last.Uri);
        Assert.IsNull(handler.Last.Body);
    }

    [TestMethod]
    public async Task ListMilestones_NoOptions_SendsWithoutQuery()
    {
        (GitHubMilestoneTools tools, GitHubRecordingHandler handler, GitHubRestClient client) = Create();
        using GitHubRestClient _ = client;
        handler.Enqueue(HttpStatusCode.OK, "[]");

        await tools.ListMilestones("public", "o", "r", cancellationToken: TestContext.CancellationToken);

        Assert.HasCount(1, handler.Requests);
        Assert.AreEqual(new Uri("https://api.github.com/repos/o/r/milestones"), handler.Last.Uri);
        Assert.AreEqual(string.Empty, handler.Last.Uri.Query);
    }

    [TestMethod]
    public async Task GetMilestone_GetsByNumberAndReturnsResponse()
    {
        (GitHubMilestoneTools tools, GitHubRecordingHandler handler, GitHubRestClient client) = Create();
        using GitHubRestClient _ = client;
        handler.Enqueue(HttpStatusCode.OK, Milestone);

        string result = await tools.GetMilestone("public", "o", "r", 3, TestContext.CancellationToken);

        Assert.AreEqual(Milestone, result);
        Assert.HasCount(1, handler.Requests);
        Assert.AreEqual(HttpMethod.Get, handler.Last.Method);
        Assert.AreEqual(new Uri("https://api.github.com/repos/o/r/milestones/3"), handler.Last.Uri);
    }

    [TestMethod]
    public async Task CreateMilestone_PostsOnlyGivenFields()
    {
        (GitHubMilestoneTools tools, GitHubRecordingHandler handler, GitHubRestClient client) = Create();
        using GitHubRestClient _ = client;
        handler.Enqueue(HttpStatusCode.Created, Milestone);
        handler.Enqueue(HttpStatusCode.Created, Milestone);

        string result = await tools.CreateMilestone("public", "o", "r", "v1.8", "open", "notes", "2026-12-31", TestContext.CancellationToken);
        await tools.CreateMilestone("public", "o", "r", "v1.8", cancellationToken: TestContext.CancellationToken);

        Assert.AreEqual(Milestone, result);
        Assert.HasCount(2, handler.Requests);
        Assert.AreEqual(HttpMethod.Post, handler.Requests[0].Method);
        Assert.AreEqual(new Uri("https://api.github.com/repos/o/r/milestones"), handler.Requests[0].Uri);
        Assert.AreEqual("""{"title":"v1.8","state":"open","description":"notes","due_on":"2026-12-31T12:00:00Z"}""", handler.Requests[0].Body);
        Assert.AreEqual("""{"title":"v1.8"}""", handler.Requests[1].Body);
    }

    [TestMethod]
    public async Task UpdateMilestone_PatchesOnlyGivenFields()
    {
        (GitHubMilestoneTools tools, GitHubRecordingHandler handler, GitHubRestClient client) = Create();
        using GitHubRestClient _ = client;
        handler.Enqueue(HttpStatusCode.OK, Milestone);

        string result = await tools.UpdateMilestone("public", "o", "r", 3, state: "closed", cancellationToken: TestContext.CancellationToken);

        Assert.AreEqual(Milestone, result);
        Assert.HasCount(1, handler.Requests);
        Assert.AreEqual(HttpMethod.Patch, handler.Last.Method);
        Assert.AreEqual(new Uri("https://api.github.com/repos/o/r/milestones/3"), handler.Last.Uri);
        Assert.AreEqual("""{"state":"closed"}""", handler.Last.Body);
    }

    [TestMethod]
    public async Task UpdateMilestone_ClearFlags_SendNull()
    {
        (GitHubMilestoneTools tools, GitHubRecordingHandler handler, GitHubRestClient client) = Create();
        using GitHubRestClient _ = client;
        handler.Enqueue(HttpStatusCode.OK, Milestone);

        await tools.UpdateMilestone("public", "o", "r", 3, clearDescription: true, clearDueOn: true, cancellationToken: TestContext.CancellationToken);

        Assert.HasCount(1, handler.Requests);
        Assert.AreEqual("""{"description":null,"due_on":null}""", handler.Last.Body);
    }

    [TestMethod]
    public async Task DeleteMilestone_ReturnsDeletedNumber()
    {
        (GitHubMilestoneTools tools, GitHubRecordingHandler handler, GitHubRestClient client) = Create();
        using GitHubRestClient _ = client;
        handler.Enqueue(HttpStatusCode.NoContent);

        string result = await tools.DeleteMilestone("public", "o", "r", 3, TestContext.CancellationToken);

        Assert.AreEqual("""{"deleted":{"milestoneNumber":3}}""", result);
        Assert.HasCount(1, handler.Requests);
        Assert.AreEqual(HttpMethod.Delete, handler.Last.Method);
        Assert.AreEqual(new Uri("https://api.github.com/repos/o/r/milestones/3"), handler.Last.Uri);
    }

    private const string InstanceMissing = "instance is required. Registered instances: public, enterprise";

    public static IEnumerable<object[]> InvalidInputs()
    {
        // 공통 오류: 다섯 Tool 각각
        string[] tools = ["list", "get", "create", "update", "delete"];
        foreach (string t in tools)
        {
            yield return [t, "instance:null", InstanceMissing];
            yield return [t, "instance:nope", "Unknown GitHub instance 'nope'. Registered instances: public, enterprise"];
            yield return [t, "owner:null", "owner is required."];
            yield return [t, "repo:null", "repo is required."];
            yield return [t, "owner:..", "owner must not contain empty, '.' or '..' path segments."];
            yield return [t, "repo:..", "repo must not contain empty, '.' or '..' path segments."];
        }
        foreach (string t in new[] { "get", "update", "delete" })
        {
            yield return [t, "number:null", "milestoneNumber must be a positive integer."];
            yield return [t, "number:0", "milestoneNumber must be a positive integer."];
        }
        // C01-I1
        yield return ["list", "state:foo", "state must be one of: open, closed, all."];
        yield return ["list", "sort:foo", "sort must be one of: due_on, completeness."];
        yield return ["list", "direction:foo", "direction must be one of: asc, desc."];
        yield return ["list", "perPage:0", "perPage must be between 1 and 100."];
        yield return ["list", "perPage:101", "perPage must be between 1 and 100."];
        yield return ["list", "page:0", "page must be a positive integer."];
        // C01-I3
        yield return ["create", "title:null", "title is required."];
        yield return ["create", "state:all", "state must be one of: open, closed."];
        yield return ["create", "dueOn:2026-02-30", "dueOn must be a date in YYYY-MM-DD format."];
        yield return ["create", "dueOn:2026/12/31", "dueOn must be a date in YYYY-MM-DD format."];
        // C01-I4
        yield return ["update", "none", "at least one field to update is required."];
        yield return ["update", "state:all", "state must be one of: open, closed."];
        yield return ["update", "dueOn:20261231", "dueOn must be a date in YYYY-MM-DD format."];
        yield return ["update", "description+clear", "description and clearDescription cannot be used together."];
        yield return ["update", "dueOn+clear", "dueOn and clearDueOn cannot be used together."];
    }

    [TestMethod]
    [DynamicData(nameof(InvalidInputs))]
    public async Task InvalidInput_ThrowsWithoutRequest(string tool, string scenario, string message)
    {
        (GitHubMilestoneTools tools, GitHubRecordingHandler handler, GitHubRestClient client) = Create();
        using GitHubRestClient _ = client;
        string? instance = "public", owner = "o", repo = "r", title = "t", state = null, sort = null, direction = null, dueOn = null, description = null;
        long? number = 3;
        int? perPage = null, page = null;
        bool clearDescription = false, clearDueOn = false;
        if (scenario != "none")
        {
            switch (scenario.Split(':', 2)[0], scenario.Contains(':') ? scenario.Split(':', 2)[1] : "")
            {
                case ("instance", var v): instance = v == "null" ? null : v; break;
                case ("owner", var v): owner = v == "null" ? null : v; break;
                case ("repo", var v): repo = v == "null" ? null : v; break;
                case ("number", var v): number = v == "null" ? null : long.Parse(v); break;
                case ("state", var v): state = v; break;
                case ("sort", var v): sort = v; break;
                case ("direction", var v): direction = v; break;
                case ("perPage", var v): perPage = int.Parse(v); break;
                case ("page", var v): page = int.Parse(v); break;
                case ("title", _): title = null; break;
                case ("dueOn", var v) when scenario.StartsWith("dueOn:", StringComparison.Ordinal): dueOn = v; break;
                case ("description+clear", _): description = "d"; clearDescription = true; break;
                case ("dueOn+clear", _): dueOn = "2026-12-31"; clearDueOn = true; break;
                default: Assert.Fail(scenario); break;
            }
        }

        Func<Task<string>> call = tool switch
        {
            "list" => () => tools.ListMilestones(instance, owner, repo, state, sort, direction, perPage, page, TestContext.CancellationToken),
            "get" => () => tools.GetMilestone(instance, owner, repo, number, TestContext.CancellationToken),
            "create" => () => tools.CreateMilestone(instance, owner, repo, title, state, description, dueOn, TestContext.CancellationToken),
            "update" => () => tools.UpdateMilestone(instance, owner, repo, number, null, state, description, dueOn, clearDescription, clearDueOn, TestContext.CancellationToken),
            "delete" => () => tools.DeleteMilestone(instance, owner, repo, number, TestContext.CancellationToken),
            _ => throw new InvalidOperationException(tool),
        };

        // 입력 오류는 동기 예외가 아니라 실패한 Task로 전달된다
        Task<string> pending = call();
        Assert.IsTrue(pending.IsFaulted);
        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() => pending);

        Assert.AreEqual(message, ex.Message);
        Assert.IsEmpty(handler.Requests);
    }

    [TestMethod]
    public async Task GitHubError_UsesExistingFormatWithoutToken()
    {
        (GitHubMilestoneTools tools, GitHubRecordingHandler handler, GitHubRestClient client) = Create();
        using GitHubRestClient _ = client;
        handler.Enqueue(HttpStatusCode.NotFound, """{"message":"Not Found"}""");

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() => tools.GetMilestone("public", "o", "r", 3, TestContext.CancellationToken));

        Assert.AreEqual("GitHub 'public' GET repos/o/r/milestones/3 failed (HTTP 404): Not Found", ex.Message);
        Assert.DoesNotContain("ghp_secret-token", ex.Message);
    }

    [TestMethod]
    public async Task ListMilestones_GitHubError_MessageHasNoQuery()
    {
        (GitHubMilestoneTools tools, GitHubRecordingHandler handler, GitHubRestClient client) = Create();
        using GitHubRestClient _ = client;
        handler.Enqueue(HttpStatusCode.UnprocessableEntity, """{"message":"Validation Failed"}""");

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() => tools.ListMilestones("public", "o", "r", "all", cancellationToken: TestContext.CancellationToken));

        Assert.AreEqual("GitHub 'public' GET repos/o/r/milestones failed (HTTP 422): Validation Failed", ex.Message);
        Assert.DoesNotContain("?", ex.Message);
        Assert.AreEqual("?state=all", handler.Last.Uri.Query);
    }

    [TestMethod]
    public async Task GetMilestone_EmptyBody_ThrowsNoContent()
    {
        (GitHubMilestoneTools tools, GitHubRecordingHandler handler, GitHubRestClient client) = Create();
        using GitHubRestClient _ = client;
        handler.Enqueue(HttpStatusCode.OK, "");

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() => tools.GetMilestone("public", "o", "r", 3, TestContext.CancellationToken));

        Assert.AreEqual("GitHub 'public' GET repos/o/r/milestones/3 returned no content.", ex.Message);
    }

    [TestMethod]
    public async Task UppercaseChoice_IsRejectedWithoutRequest()
    {
        (GitHubMilestoneTools tools, GitHubRecordingHandler handler, GitHubRestClient client) = Create();
        using GitHubRestClient _ = client;

        McpException list = await Assert.ThrowsExactlyAsync<McpException>(() => tools.ListMilestones("public", "o", "r", direction: "DESC", cancellationToken: TestContext.CancellationToken));
        McpException update = await Assert.ThrowsExactlyAsync<McpException>(() => tools.UpdateMilestone("public", "o", "r", 3, state: "Closed", cancellationToken: TestContext.CancellationToken));

        Assert.AreEqual("direction must be one of: asc, desc.", list.Message);
        Assert.AreEqual("state must be one of: open, closed.", update.Message);
        Assert.IsEmpty(handler.Requests);
    }
}
