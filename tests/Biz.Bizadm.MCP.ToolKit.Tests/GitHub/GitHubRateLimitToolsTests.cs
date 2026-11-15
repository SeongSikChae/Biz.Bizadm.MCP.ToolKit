using System.Net;

using Biz.Bizadm.MCP.ToolKit.GitHub;
using Biz.Bizadm.MCP.ToolKit.Tools.GitHub;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tests.GitHub;

[TestClass]
public sealed class GitHubRateLimitToolsTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task GetRateLimit_ResolvesInstanceAndReturnsResponse()
    {
        const string Body = """{"resources":{"core":{"limit":5000,"used":0,"remaining":5000,"reset":1700000000}}}""";
        GitHubRecordingHandler handler = new();
        handler.Enqueue(HttpStatusCode.OK, Body);
        using GitHubRestClient client = new(GitHubRestClientTests.Options(), handler);
        GitHubRateLimitTools tools = new(client);

        string result = await tools.GetRateLimit(" Enterprise ", TestContext.CancellationToken);

        Assert.AreEqual(Body, result);
        Assert.HasCount(1, handler.Requests);
        Assert.AreEqual(HttpMethod.Get, handler.Last.Method);
        Assert.AreEqual(new Uri("https://ghe.example/api/v3/rate_limit"), handler.Last.Uri);
    }

    [TestMethod]
    public async Task GetRateLimit_MissingOrUnknownInstance_ThrowsWithoutRequest()
    {
        GitHubRecordingHandler handler = new();
        using GitHubRestClient client = new(GitHubRestClientTests.Options(), handler);
        GitHubRateLimitTools tools = new(client);

        // 다른 REST Tool처럼 인스턴스 오류도 동기 예외가 아니라 실패한 Task로 전달된다
        Task<string> pending = tools.GetRateLimit(null, TestContext.CancellationToken);
        Assert.IsTrue(pending.IsFaulted);

        McpException missing = await Assert.ThrowsExactlyAsync<McpException>(() => pending);
        McpException unknown = await Assert.ThrowsExactlyAsync<McpException>(() => tools.GetRateLimit("nope", TestContext.CancellationToken));

        Assert.AreEqual("instance is required. Registered instances: public, enterprise", missing.Message);
        Assert.AreEqual("Unknown GitHub instance 'nope'. Registered instances: public, enterprise", unknown.Message);
        Assert.IsEmpty(handler.Requests);
    }
}
