using System.Net;
using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.GitHub;
using Biz.Bizadm.MCP.ToolKit.Relay;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tests.GitHub;

[TestClass]
public sealed class GitHubRestClientTests
{
    private const string Token = "ghp_secret-token";

    public TestContext TestContext { get; set; } = null!;

    internal static GitHubOptions Options() => new()
    {
        Instances =
        [
            new GitHubInstance { Name = "public", Endpoint = "https://api.githubcopilot.com/mcp/", ApiEndpoint = "https://api.github.com/", Token = Token },
            new GitHubInstance { Name = "enterprise", Endpoint = "https://ghe.example/mcp/", ApiEndpoint = "https://ghe.example/api/v3/", Token = Token },
        ],
    };

    [TestMethod]
    public async Task Send_CombinesApiEndpointAndSendsGitHubHeaders()
    {
        GitHubRecordingHandler handler = new();
        handler.Enqueue(HttpStatusCode.Created, """{"ref":"refs/tags/v1"}""");
        handler.Enqueue(HttpStatusCode.Created, "{}");
        using GitHubRestClient client = new(Options(), handler);

        string? result = await client.SendAsync(client.Resolve("public"), HttpMethod.Post, "repos/o/r/git/refs", new JsonObject { ["ref"] = "refs/tags/v1" }, TestContext.CancellationToken);
        await client.SendAsync(client.Resolve("ENTERPRISE"), HttpMethod.Get, "repos/o/r/git/refs", null, TestContext.CancellationToken);

        Assert.AreEqual("""{"ref":"refs/tags/v1"}""", result);
        Assert.AreEqual(new Uri("https://api.github.com/repos/o/r/git/refs"), handler.Requests[0].Uri);
        Assert.AreEqual(new Uri("https://ghe.example/api/v3/repos/o/r/git/refs"), handler.Requests[1].Uri);
        Assert.AreEqual("""{"ref":"refs/tags/v1"}""", handler.Requests[0].Body);
        Assert.IsNull(handler.Requests[1].Body);
        Dictionary<string, string> headers = handler.Requests[0].Headers;
        Assert.AreEqual($"Bearer {Token}", headers["Authorization"]);
        Assert.AreEqual("application/vnd.github+json", headers["Accept"]);
        Assert.AreEqual("2022-11-28", headers["X-GitHub-Api-Version"]);
        Assert.StartsWith("Biz.Bizadm.MCP.ToolKit/", headers["User-Agent"]);
    }

    [TestMethod]
    public async Task Send_NoContent_ReturnsNull()
    {
        GitHubRecordingHandler handler = new();
        handler.Enqueue(HttpStatusCode.NoContent);
        using GitHubRestClient client = new(Options(), handler);

        Assert.IsNull(await client.SendAsync(client.Resolve("public"), HttpMethod.Delete, "repos/o/r/git/refs/tags/v1", null, TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task Send_ErrorStatus_ThrowsWithMessageAndErrorsWithoutToken()
    {
        GitHubRecordingHandler handler = new();
        handler.Enqueue(HttpStatusCode.UnprocessableEntity, """{"message":"Validation Failed","errors":[{"code":"already_exists"},{"message":"Reference already exists"}]}""");
        handler.Enqueue(HttpStatusCode.Forbidden, """{"message":"Resource not accessible by personal access token","status":"403"}""");
        using GitHubRestClient client = new(Options(), handler);
        GitHubInstance target = client.Resolve("public");

        McpException unprocessable = await Assert.ThrowsExactlyAsync<McpException>(() => client.SendAsync(target, HttpMethod.Post, "repos/o/r/git/refs", new JsonObject(), TestContext.CancellationToken));
        McpException forbidden = await Assert.ThrowsExactlyAsync<McpException>(() => client.SendAsync(target, HttpMethod.Post, "repos/o/r/git/refs", new JsonObject(), TestContext.CancellationToken));

        Assert.AreEqual("GitHub 'public' POST repos/o/r/git/refs failed (HTTP 422): Validation Failed (already_exists; Reference already exists)", unprocessable.Message);
        Assert.AreEqual("GitHub 'public' POST repos/o/r/git/refs failed (HTTP 403): Resource not accessible by personal access token", forbidden.Message);
        Assert.DoesNotContain(Token, unprocessable.Message + forbidden.Message);
    }

    [TestMethod]
    public void DescribeError_NonJson_UsesTrimmedBodyPrefix()
    {
        Assert.AreEqual("Bad Gateway", GitHubRestClient.DescribeError("  Bad Gateway \n"));
        Assert.HasCount(200, GitHubRestClient.DescribeError(new string('x', 500)));
        Assert.AreEqual("{\"other\":1}", GitHubRestClient.DescribeError("{\"other\":1}"));
    }

    [TestMethod]
    public async Task Send_TransportFailure_IsWrapped_AndCallerCancellationPropagates()
    {
        GitHubRecordingHandler handler = new();
        handler.EnqueueException(new HttpRequestException("No such host is known."));
        using GitHubRestClient client = new(Options(), handler);
        GitHubInstance target = client.Resolve("public");

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() => client.SendAsync(target, HttpMethod.Get, "repos/o/r", null, TestContext.CancellationToken));
        Assert.AreEqual("GitHub 'public' GET repos/o/r failed: No such host is known.", ex.Message);
        Assert.IsInstanceOfType<HttpRequestException>(ex.InnerException);

        using CancellationTokenSource cts = new();
        await cts.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => client.SendAsync(target, HttpMethod.Get, "repos/o/r", null, cts.Token));
    }

    [TestMethod]
    public void Resolve_MatchesGatewayMessageFormat()
    {
        using GitHubRestClient client = new(Options(), new GitHubRecordingHandler());
        using McpRelayPool pool = new("GitHub", Options().Instances.Select(Microsoft.Extensions.DependencyInjection.GitHubMcpServerBuilderExtensions.ToTarget),
            (_, _) => throw new AssertFailedException("must not connect"));

        foreach (string? name in new[] { null, " ", "qa" })
        {
            string rest = Assert.ThrowsExactly<McpException>(() => client.Resolve(name)).Message;
            string gateway = Assert.ThrowsExactly<McpException>(() => pool.Resolve(name)).Message;
            Assert.AreEqual(gateway, rest);
        }
    }

    [TestMethod]
    public void RefHelpers_NormalizeAndEncode()
    {
        Assert.AreEqual("tags/v1.0", GitHubRestClient.NormalizeRef("refs/tags/v1.0"));
        Assert.AreEqual("heads/feature/x", GitHubRestClient.NormalizeRef("heads/feature/x"));
        Assert.AreEqual("tags/v%201", GitHubRestClient.RefPath("refs/tags/v 1"));
        Assert.AreEqual("heads/feature/x", GitHubRestClient.RefPath("heads/feature/x"));
        Assert.AreEqual("repos/my%20org/repo%23/", GitHubRestClient.RepoPath("my org", "repo#"));
        Assert.AreEqual("repos/o%2Fx/r/", GitHubRestClient.RepoPath("o/x", "r"));
    }

    [TestMethod]
    public void ResolveUploadUri_UsesTemplateAndChecksHost()
    {
        GitHubInstance github = Options().Instances[0];
        GitHubInstance enterprise = Options().Instances[1];

        Assert.AreEqual("https://uploads.github.com/repos/o/r/releases/1/assets?name=a%20b.zip&label=L%20x",
            GitHubRestClient.ResolveUploadUri(github, "https://uploads.github.com/repos/o/r/releases/1/assets{?name,label}", "a b.zip", "L x").AbsoluteUri);
        Assert.AreEqual("https://api.github.com/repos/o/r/releases/1/assets?name=a",
            GitHubRestClient.ResolveUploadUri(github, "https://api.github.com/repos/o/r/releases/1/assets{?name,label}", "a", null).AbsoluteUri);
        Assert.AreEqual("https://ghe.example/api/uploads/repos/o/r/releases/1/assets?name=a",
            GitHubRestClient.ResolveUploadUri(enterprise, "https://ghe.example/api/uploads/repos/o/r/releases/1/assets{?name,label}", "a", "").AbsoluteUri);

        foreach ((GitHubInstance instance, string? template) in new (GitHubInstance, string?)[]
        {
            (github, "https://evil.example/repos/o/r/releases/1/assets{?name,label}"),
            (github, "http://uploads.github.com/repos/o/r/releases/1/assets"),
            (github, "https://uploads.github.com:8443/repos/o/r/releases/1/assets"),
            (enterprise, "https://uploads.github.com/repos/o/r/releases/1/assets"),
        })
        {
            McpException ex = Assert.ThrowsExactly<McpException>(() => GitHubRestClient.ResolveUploadUri(instance, template, "a", null), template);
            Assert.Contains("does not match ApiEndpoint", ex.Message);
            Assert.DoesNotContain(Token, ex.Message);
        }
        foreach (string? template in new[] { null, "", "/repos/o/r/releases/1/assets", "ftp://uploads.github.com/x" })
            Assert.AreEqual($"GitHub 'public' release has no valid upload_url.",
                Assert.ThrowsExactly<McpException>(() => GitHubRestClient.ResolveUploadUri(github, template, "a", null)).Message);
    }

    [TestMethod]
    public async Task Send_AbsoluteUriWithBinaryContent_UsesGivenPathInErrors()
    {
        GitHubRecordingHandler handler = new();
        handler.Enqueue(HttpStatusCode.Created, """{"id":7}""");
        handler.Enqueue(HttpStatusCode.UnprocessableEntity, """{"message":"Validation Failed"}""");
        using GitHubRestClient client = new(Options(), handler);
        GitHubInstance target = client.Resolve("public");
        Uri uri = new("https://uploads.github.com/repos/o/r/releases/1/assets?name=a");

        string? ok = await client.SendAsync(target, HttpMethod.Post, uri, uri.AbsolutePath, new ByteArrayContent([1, 2, 3]), TestContext.CancellationToken);
        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() => client.SendAsync(target, HttpMethod.Post, uri, uri.AbsolutePath, new ByteArrayContent([1]), TestContext.CancellationToken));

        Assert.AreEqual("""{"id":7}""", ok);
        Assert.AreEqual(uri, handler.Requests[0].Uri);
        Assert.AreEqual($"Bearer {Token}", handler.Requests[0].Headers["Authorization"]);
        Assert.AreEqual("GitHub 'public' POST /repos/o/r/releases/1/assets failed (HTTP 422): Validation Failed", ex.Message);
    }

    [TestMethod]
    public void PathHelpers_RejectDotAndEmptySegments()
    {
        // 결합 시 dot-segment가 정리되어 ref 범위 밖(예: DELETE repos/other/repo)으로 나가는 것을 막는다
        foreach (string reference in new[] { "../../../../other/repo", "tags/..", "tags/./x", "tags//x", "tags/" })
            Assert.AreEqual("ref must not contain empty, '.' or '..' path segments.",
                Assert.ThrowsExactly<McpException>(() => GitHubRestClient.RefPath(reference)).Message, reference);
        Assert.ThrowsExactly<McpException>(() => GitHubRestClient.RepoPath("..", "r"));
        Assert.ThrowsExactly<McpException>(() => GitHubRestClient.RepoPath("o", "."));
        Assert.AreEqual("tags/v1..2", GitHubRestClient.RefPath("tags/v1..2"));
    }
}
