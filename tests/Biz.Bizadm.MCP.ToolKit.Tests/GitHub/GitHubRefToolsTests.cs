using System.Net;
using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.GitHub;
using Biz.Bizadm.MCP.ToolKit.Tools.GitHub;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tests.GitHub;

[TestClass]
public sealed class GitHubRefToolsTests
{
    private const string Sha = "391ec90e17d1ca5990cbce30ba983b565df631c6";
    private const string TagObjectSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    public TestContext TestContext { get; set; } = null!;

    private static (GitHubRefTools Tools, GitHubRecordingHandler Handler, GitHubRestClient Client) Create()
    {
        GitHubRecordingHandler handler = new();
        GitHubRestClient client = new(GitHubRestClientTests.Options(), handler);
        return (new GitHubRefTools(client), handler, client);
    }

    private static string RefResponse(string reference, string sha)
        => "{\"ref\":\"" + reference + "\",\"object\":{\"sha\":\"" + sha + "\",\"type\":\"commit\"}}";

    [TestMethod]
    public async Task CreateLightweightTag_PostsRefAndReturnsResponse()
    {
        (GitHubRefTools tools, GitHubRecordingHandler handler, GitHubRestClient client) = Create();
        using GitHubRestClient _ = client;
        handler.Enqueue(HttpStatusCode.Created, RefResponse("refs/tags/v1.0.0", Sha));

        string result = await tools.CreateLightweightTag("public", "SeongSikChae", "Biz.Bizadm.MCP.ToolKit", " v1.0.0 ", Sha, TestContext.CancellationToken);

        Assert.AreEqual(RefResponse("refs/tags/v1.0.0", Sha), result);
        Assert.AreEqual(HttpMethod.Post, handler.Last.Method);
        Assert.AreEqual(new Uri("https://api.github.com/repos/SeongSikChae/Biz.Bizadm.MCP.ToolKit/git/refs"), handler.Last.Uri);
        Assert.AreEqual($$"""{"ref":"refs/tags/v1.0.0","sha":"{{Sha}}"}""", handler.Last.Body);
    }

    [TestMethod]
    public async Task CreateAnnotatedTag_PostsTagObjectThenRef_AndCombinesResponses()
    {
        (GitHubRefTools tools, GitHubRecordingHandler handler, GitHubRestClient client) = Create();
        using GitHubRestClient _ = client;
        handler.Enqueue(HttpStatusCode.Created, $$"""{"sha":"{{TagObjectSha}}","tag":"v1.0.0"}""");
        handler.Enqueue(HttpStatusCode.Created, RefResponse("refs/tags/v1.0.0", TagObjectSha));

        string result = await tools.CreateAnnotatedTag("enterprise", "o", "r", "v1.0.0", Sha, "release 1.0.0", "Bizadm", "bot@example.com", TestContext.CancellationToken);

        Assert.HasCount(2, handler.Requests);
        Assert.AreEqual(new Uri("https://ghe.example/api/v3/repos/o/r/git/tags"), handler.Requests[0].Uri);
        JsonNode tagBody = JsonNode.Parse(handler.Requests[0].Body!)!;
        Assert.AreEqual("v1.0.0", (string?)tagBody["tag"]);
        Assert.AreEqual("release 1.0.0", (string?)tagBody["message"]);
        Assert.AreEqual(Sha, (string?)tagBody["object"]);
        Assert.AreEqual("commit", (string?)tagBody["type"]);
        Assert.AreEqual("Bizadm", (string?)tagBody["tagger"]!["name"]);
        Assert.AreEqual("bot@example.com", (string?)tagBody["tagger"]!["email"]);
        Assert.AreEqual(new Uri("https://ghe.example/api/v3/repos/o/r/git/refs"), handler.Requests[1].Uri);
        Assert.AreEqual($$"""{"ref":"refs/tags/v1.0.0","sha":"{{TagObjectSha}}"}""", handler.Requests[1].Body);

        JsonNode combined = JsonNode.Parse(result)!;
        Assert.AreEqual(TagObjectSha, (string?)combined["tag"]!["sha"]);
        Assert.AreEqual("refs/tags/v1.0.0", (string?)combined["ref"]!["ref"]);
    }

    [TestMethod]
    public async Task CreateAnnotatedTag_WithoutTagger_OmitsTagger()
    {
        (GitHubRefTools tools, GitHubRecordingHandler handler, GitHubRestClient client) = Create();
        using GitHubRestClient _ = client;
        handler.Enqueue(HttpStatusCode.Created, $$"""{"sha":"{{TagObjectSha}}"}""");
        handler.Enqueue(HttpStatusCode.Created, RefResponse("refs/tags/v1", TagObjectSha));

        await tools.CreateAnnotatedTag("public", "o", "r", "v1", Sha, "m", cancellationToken: TestContext.CancellationToken);

        Assert.IsNull(JsonNode.Parse(handler.Requests[0].Body!)!["tagger"]);
    }

    [TestMethod]
    public async Task CreateAnnotatedTag_RefFails_ReportsCreatedTagObjectSha()
    {
        (GitHubRefTools tools, GitHubRecordingHandler handler, GitHubRestClient client) = Create();
        using GitHubRestClient _ = client;
        handler.Enqueue(HttpStatusCode.Created, $$"""{"sha":"{{TagObjectSha}}"}""");
        handler.Enqueue(HttpStatusCode.UnprocessableEntity, """{"message":"Reference already exists"}""");

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() => tools.CreateAnnotatedTag("public", "o", "r", "v1", Sha, "m", cancellationToken: TestContext.CancellationToken));

        Assert.AreEqual($"GitHub 'public' POST repos/o/r/git/refs failed (HTTP 422): Reference already exists (tag object {TagObjectSha} was created without a ref)", ex.Message);
    }

    [TestMethod]
    public async Task CreateAnnotatedTag_TagResponseWithoutSha_FailsBeforeRef()
    {
        (GitHubRefTools tools, GitHubRecordingHandler handler, GitHubRestClient client) = Create();
        using GitHubRestClient _ = client;
        handler.Enqueue(HttpStatusCode.Created, """{"tag":"v1"}""");

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() => tools.CreateAnnotatedTag("public", "o", "r", "v1", Sha, "m", cancellationToken: TestContext.CancellationToken));

        Assert.AreEqual("GitHub 'public' git/tags response has no sha.", ex.Message);
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    public async Task UpdateRef_PatchesNormalizedEncodedRef()
    {
        (GitHubRefTools tools, GitHubRecordingHandler handler, GitHubRestClient client) = Create();
        using GitHubRestClient _ = client;
        handler.Enqueue(HttpStatusCode.OK, RefResponse("refs/tags/v 1", Sha));

        string result = await tools.UpdateRef("public", "o", "r", "refs/tags/v 1", Sha, force: true, TestContext.CancellationToken);

        Assert.AreEqual(RefResponse("refs/tags/v 1", Sha), result);
        Assert.AreEqual(HttpMethod.Patch, handler.Last.Method);
        Assert.AreEqual("https://api.github.com/repos/o/r/git/refs/tags/v%201", handler.Last.Uri.AbsoluteUri);
        Assert.AreEqual($$"""{"sha":"{{Sha}}","force":true}""", handler.Last.Body);
    }

    [TestMethod]
    public async Task DeleteRef_DeletesAndReturnsDeletedRef()
    {
        (GitHubRefTools tools, GitHubRecordingHandler handler, GitHubRestClient client) = Create();
        using GitHubRestClient _ = client;
        handler.Enqueue(HttpStatusCode.NoContent);
        handler.Enqueue(HttpStatusCode.NoContent);

        string tag = await tools.DeleteRef("public", "o", "r", "tags/v1", TestContext.CancellationToken);
        string branch = await tools.DeleteRef("public", "o", "r", "refs/heads/feature/x", TestContext.CancellationToken);

        Assert.AreEqual("""{"deleted":"refs/tags/v1"}""", tag);
        Assert.AreEqual("""{"deleted":"refs/heads/feature/x"}""", branch);
        Assert.AreEqual(HttpMethod.Delete, handler.Requests[0].Method);
        Assert.AreEqual(new Uri("https://api.github.com/repos/o/r/git/refs/tags/v1"), handler.Requests[0].Uri);
        Assert.AreEqual(new Uri("https://api.github.com/repos/o/r/git/refs/heads/feature/x"), handler.Requests[1].Uri);
        Assert.IsNull(handler.Requests[0].Body);
    }

    [TestMethod]
    public async Task InvalidInput_ThrowsWithoutRequest()
    {
        (GitHubRefTools tools, GitHubRecordingHandler handler, GitHubRestClient client) = Create();
        using GitHubRestClient _ = client;

        string[] messages =
        [
            (await Assert.ThrowsExactlyAsync<McpException>(() => tools.CreateLightweightTag(null, "o", "r", "v1", Sha))).Message,
            (await Assert.ThrowsExactlyAsync<McpException>(() => tools.CreateLightweightTag("qa", "o", "r", "v1", Sha))).Message,
            (await Assert.ThrowsExactlyAsync<McpException>(() => tools.CreateLightweightTag("public", " ", "r", "v1", Sha))).Message,
            (await Assert.ThrowsExactlyAsync<McpException>(() => tools.CreateLightweightTag("public", "o", null, "v1", Sha))).Message,
            (await Assert.ThrowsExactlyAsync<McpException>(() => tools.CreateLightweightTag("public", "o", "r", null, Sha))).Message,
            (await Assert.ThrowsExactlyAsync<McpException>(() => tools.CreateLightweightTag("public", "o", "r", "v1", null))).Message,
            (await Assert.ThrowsExactlyAsync<McpException>(() => tools.CreateAnnotatedTag("public", "o", "r", "v1", Sha, null))).Message,
            (await Assert.ThrowsExactlyAsync<McpException>(() => tools.CreateAnnotatedTag("public", "o", "r", "v1", Sha, "m", taggerName: "n"))).Message,
            (await Assert.ThrowsExactlyAsync<McpException>(() => tools.UpdateRef("public", "o", "r", null, Sha))).Message,
            (await Assert.ThrowsExactlyAsync<McpException>(() => tools.DeleteRef("public", "o", "r", " "))).Message,
            (await Assert.ThrowsExactlyAsync<McpException>(() => tools.DeleteRef("public", "o", "r", "../../../../other/repo"))).Message,
            (await Assert.ThrowsExactlyAsync<McpException>(() => tools.DeleteRef("public", "..", "r", "tags/v1"))).Message,
        ];

        Assert.IsEmpty(handler.Requests);
        CollectionAssert.AreEqual(new[]
        {
            "instance is required. Registered instances: public, enterprise",
            "Unknown GitHub instance 'qa'. Registered instances: public, enterprise",
            "owner is required.", "repo is required.", "tag is required.", "sha is required.", "message is required.",
            "taggerName and taggerEmail must be given together.", "ref is required.", "ref is required.",
            "ref must not contain empty, '.' or '..' path segments.", "owner must not contain empty, '.' or '..' path segments.",
        }, messages);
    }
}
