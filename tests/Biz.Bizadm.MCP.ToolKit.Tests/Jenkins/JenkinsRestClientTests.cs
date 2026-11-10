using System.Net;

using Biz.Bizadm.MCP.ToolKit.Jenkins;
using Biz.Bizadm.MCP.ToolKit.Tests.GitHub;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tests.Jenkins;

[TestClass]
public sealed class JenkinsRestClientTests
{
    internal const string Token = "jenkins-secret-token";

    public TestContext TestContext { get; set; } = null!;

    internal static JenkinsOptions Options() => new()
    {
        Instances =
        [
            new JenkinsInstance { Name = "prod", Endpoint = "http://jenkins.example:8080/mcp-server/mcp", User = "builder", ApiToken = Token },
            new JenkinsInstance { Name = "ctx", Endpoint = "https://ci.example/jenkins/MCP-Server/mcp/", User = "builder", ApiToken = Token },
            new JenkinsInstance { Name = "other", Endpoint = "http://proxy.example/mcp", User = "builder", ApiToken = Token },
        ],
    };

    [TestMethod]
    [DataRow("http://j:8080/mcp-server/mcp", "http://j:8080/")]
    [DataRow("http://j:8080/mcp-server/mcp/", "http://j:8080/")]
    [DataRow("https://h/jenkins/MCP-SERVER/MCP", "https://h/jenkins/")]
    [DataRow("http://j/mcp", null)]
    [DataRow("http://j/foo-mcp-server/mcp", null)]
    [DataRow("http://j/mcp-server/mcp/x", null)]
    public void RootOf_StripsMcpPath(string endpoint, string? expected)
        => Assert.AreEqual(expected, JenkinsRestClient.RootOf(endpoint)?.AbsoluteUri);

    [TestMethod]
    [DataRow("job", "job", "job/job/")]
    [DataRow(" /folder/sub/job/ ", "folder/sub/job", "job/folder/job/sub/job/job/")]
    [DataRow("my folder/a#b", "my folder/a#b", "job/my%20folder/job/a%23b/")]
    [DataRow("multi/feature%2Fx", "multi/feature%2Fx", "job/multi/job/feature%252Fx/")]
    public void JobPath_EncodesSegments(string job, string name, string path)
        => Assert.AreEqual((name, path), JenkinsRestClient.JobPath(job));

    [TestMethod]
    [DataRow("a//b")]
    [DataRow("a/./b")]
    [DataRow("../b")]
    [DataRow("a\\b")]
    [DataRow("/")]
    public void JobPath_InvalidSegments_Throw(string job)
        => Assert.ThrowsExactly<McpException>(() => JenkinsRestClient.JobPath(job));

    [TestMethod]
    public void Resolve_UnknownBlankOrNonMcpEndpoint_Throw()
    {
        using JenkinsRestClient client = new(Options(), new GitHubRecordingHandler());

        Assert.AreEqual("prod", client.Resolve(" PROD ").Name);
        StringAssert.StartsWith(Assert.ThrowsExactly<McpException>(() => client.Resolve(null)).Message, "instance is required. Registered instances: prod, ctx, other");
        StringAssert.StartsWith(Assert.ThrowsExactly<McpException>(() => client.Resolve("x")).Message, "Unknown Jenkins instance 'x'.");
        Assert.AreEqual("Jenkins 'other' Endpoint does not end with /mcp-server/mcp; REST tools are unavailable.",
            Assert.ThrowsExactly<McpException>(() => client.Resolve("other")).Message);
    }

    [TestMethod]
    public async Task Send_CombinesRootAndSendsBasicAuth()
    {
        GitHubRecordingHandler handler = new();
        handler.Enqueue(HttpStatusCode.OK, "<project/>", "application/xml");
        handler.Enqueue(HttpStatusCode.OK, "<project/>", "application/xml");
        using JenkinsRestClient client = new(Options(), handler);

        string body = await client.SendAsync(client.Resolve("prod"), HttpMethod.Get, JenkinsRestClient.JobPath("multi/feature%2Fx").Path, "config.xml", null, TestContext.CancellationToken);
        await client.SendAsync(client.Resolve("ctx"), HttpMethod.Get, "job/a/", "config.xml", null, TestContext.CancellationToken);

        Assert.AreEqual("<project/>", body);
        Assert.AreEqual("http://jenkins.example:8080/job/multi/job/feature%252Fx/config.xml", handler.Requests[0].Uri.AbsoluteUri);
        Assert.AreEqual("https://ci.example/jenkins/job/a/config.xml", handler.Requests[1].Uri.AbsoluteUri);
        Assert.AreEqual("Basic " + Convert.ToBase64String("builder:jenkins-secret-token"u8.ToArray()), handler.Requests[0].Headers["Authorization"]);
        Assert.StartsWith("Biz.Bizadm.MCP.ToolKit/", handler.Requests[0].Headers["User-Agent"]);
    }

    [TestMethod]
    public async Task Send_Redirect_FailsWithLocation()
    {
        GitHubRecordingHandler handler = new();
        handler.Enqueue(HttpStatusCode.Found, configure: r => r.Headers.Location = new Uri("/login?from=x", UriKind.Relative));
        using JenkinsRestClient client = new(Options(), handler);

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() =>
            client.SendAsync(client.Resolve("prod"), HttpMethod.Post, "job/a/", "config.xml", null, TestContext.CancellationToken));

        Assert.AreEqual("Jenkins 'prod' POST job/a/config.xml failed (HTTP 302): redirected to http://jenkins.example:8080/login?from=x", ex.Message);
    }

    [TestMethod]
    public async Task SendRaw_ReturnsStatusAndAbsoluteLocation()
    {
        GitHubRecordingHandler handler = new();
        handler.Enqueue(HttpStatusCode.Found, configure: r => r.Headers.Location = new Uri("../../", UriKind.Relative));
        using JenkinsRestClient client = new(Options(), handler);

        (int status, Uri? location, string _) = await client.SendRawAsync(client.Resolve("ctx"), HttpMethod.Post, "job/f/job/a/", "doDelete", null, TestContext.CancellationToken);

        Assert.AreEqual(302, status);
        Assert.AreEqual("https://ci.example/jenkins/job/f/", location!.AbsoluteUri);
    }

    [TestMethod]
    public async Task Send_ErrorStatus_DescribesWithoutToken()
    {
        GitHubRecordingHandler handler = new();
        handler.Enqueue(HttpStatusCode.Forbidden, "<html><head><title>Error 403 No valid crumb was included in the request</title></head></html>", "text/html");
        handler.Enqueue(HttpStatusCode.NotFound, "Not Found", "text/plain");
        using JenkinsRestClient client = new(Options(), handler);
        JenkinsInstance prod = client.Resolve("prod");

        McpException forbidden = await Assert.ThrowsExactlyAsync<McpException>(() => client.SendAsync(prod, HttpMethod.Post, "job/a/", "config.xml", null, TestContext.CancellationToken));
        McpException notFound = await Assert.ThrowsExactlyAsync<McpException>(() => client.SendAsync(prod, HttpMethod.Get, "job/a/", "config.xml", null, TestContext.CancellationToken));

        Assert.AreEqual("Jenkins 'prod' POST job/a/config.xml failed (HTTP 403): Error 403 No valid crumb was included in the request (ApiToken must be a Jenkins API token, not a password)", forbidden.Message);
        Assert.AreEqual("Jenkins 'prod' GET job/a/config.xml failed (HTTP 404): Not Found", notFound.Message);
        Assert.DoesNotContain(Token, forbidden.Message + notFound.Message);
    }

    [TestMethod]
    public async Task Send_TransportError_WrapsWithoutToken()
    {
        GitHubRecordingHandler handler = new();
        handler.EnqueueException(new HttpRequestException("connection refused"));
        using JenkinsRestClient client = new(Options(), handler);

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() =>
            client.SendAsync(client.Resolve("prod"), HttpMethod.Get, "job/a/", "config.xml", null, TestContext.CancellationToken));

        Assert.AreEqual("Jenkins 'prod' GET job/a/config.xml failed: connection refused", ex.Message);
    }

    [TestMethod]
    [DataRow("<html><HEAD><TITLE>\n Bad &amp; worse \n</TITLE></HEAD><body>x</body></html>", 400, "Bad & worse")]
    [DataRow("<html><head><title>Jenkins [Jenkins]</title></head><body><h2>Oops!</h2><p>java.io.IOException: bad &lt;xml&gt;</p></body></html>", 500, "Jenkins [Jenkins] - Oops! java.io.IOException: bad <xml>")]
    [DataRow("<html><body><h1>Forbidden</h1> nope</body></html>", 403, "Forbidden nope")]
    [DataRow("plain <b>text</b>  body", 400, "plain <b>text</b> body")]
    public void DescribeError_Summarizes(string body, int status, string expected)
        => Assert.AreEqual(expected, JenkinsRestClient.DescribeError(body, status));

    [TestMethod]
    public void DescribeError_TruncatesTo200()
        => Assert.AreEqual(200, JenkinsRestClient.DescribeError(new string('x', 500), 500).Length);
}
