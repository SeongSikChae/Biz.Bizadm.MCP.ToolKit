using System.Net;
using System.Text;

using Biz.Bizadm.MCP.ToolKit.Jenkins;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Biz.Bizadm.MCP.ToolKit.Tests.Jenkins;

[TestClass]
public sealed class JenkinsMcpClientPoolTests
{
    private const string Token = "secret-api-token";

    public TestContext TestContext { get; set; } = null!;

    private static JenkinsInstance Instance(string name) => new()
    {
        Name = name,
        Endpoint = $"http://{name}.example:8080/mcp-server/mcp",
        User = "builder",
        ApiToken = Token,
    };

    private static JenkinsOptions Options(params string[] names) => new() { Instances = [.. names.Select(Instance)] };

    [TestMethod]
    public async Task ListTools_SameInstance_ConnectsOnceAndCaches()
    {
        await using FakeJenkinsServer fake = new("getJob", "triggerBuild");
        await using JenkinsMcpClientPool pool = new(Options("prod"), (_, ct) => fake.ConnectionAsync(ct));
        JenkinsInstance prod = pool.Resolve("prod");

        IReadOnlyList<Tool> first = await pool.ListToolsAsync(prod, refresh: false, TestContext.CancellationToken);
        IReadOnlyList<Tool> second = await pool.ListToolsAsync(prod, refresh: false, TestContext.CancellationToken);

        CollectionAssert.AreEqual(new[] { "getJob", "triggerBuild" }, first.Select(t => t.Name).ToArray());
        Assert.AreSame(first, second);
        Assert.AreEqual(1, fake.ListToolsCount);
        Assert.AreEqual(1, fake.ConnectCount);
    }

    [TestMethod]
    public async Task ListTools_Refresh_QueriesUpstreamAgain()
    {
        await using FakeJenkinsServer fake = new("getJob");
        await using JenkinsMcpClientPool pool = new(Options("prod"), (_, ct) => fake.ConnectionAsync(ct));
        JenkinsInstance prod = pool.Resolve("PROD");

        await pool.ListToolsAsync(prod, refresh: false, TestContext.CancellationToken);
        await pool.ListToolsAsync(prod, refresh: true, TestContext.CancellationToken);

        Assert.AreEqual(2, fake.ListToolsCount);
        Assert.AreEqual(1, fake.ConnectCount);
    }

    [TestMethod]
    public async Task ListTools_DifferentInstances_ReturnTheirOwnTools()
    {
        await using FakeJenkinsServer prodFake = new("getJob", "triggerBuild");
        await using FakeJenkinsServer devFake = new("getJob");
        await using JenkinsMcpClientPool pool = new(Options("prod", "dev"),
            (i, ct) => i.Name == "prod" ? prodFake.ConnectionAsync(ct) : devFake.ConnectionAsync(ct));

        IReadOnlyList<Tool> prod = await pool.ListToolsAsync(pool.Resolve("prod"), false, TestContext.CancellationToken);
        IReadOnlyList<Tool> dev = await pool.ListToolsAsync(pool.Resolve("dev"), false, TestContext.CancellationToken);

        Assert.HasCount(2, prod);
        Assert.HasCount(1, dev);
    }

    [TestMethod]
    public async Task ConnectFailure_WrapsWithStatusWithoutToken_AndReconnectsNextCall()
    {
        await using FakeJenkinsServer fake = new("getJob");
        int attempts = 0;
        await using JenkinsMcpClientPool pool = new(Options("prod"), (_, ct) =>
        {
            if (++attempts == 1)
                throw new InvalidOperationException("connect failed", new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized));
            return fake.ConnectionAsync(ct);
        });
        JenkinsInstance prod = pool.Resolve("prod");

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() => pool.ListToolsAsync(prod, false, TestContext.CancellationToken));
        Assert.Contains("'prod'", ex.Message);
        Assert.Contains("HTTP 401", ex.Message);
        Assert.DoesNotContain(Token, ex.Message);
        Assert.DoesNotContain("builder", ex.Message);
        Assert.IsInstanceOfType<InvalidOperationException>(ex.InnerException);

        IReadOnlyList<Tool> tools = await pool.ListToolsAsync(prod, false, TestContext.CancellationToken);
        Assert.HasCount(1, tools);
        Assert.AreEqual(2, attempts);
    }

    [TestMethod]
    public async Task CallerCancellation_PropagatesAndKeepsClient()
    {
        await using FakeJenkinsServer fake = new("getJob");
        await using JenkinsMcpClientPool pool = new(Options("prod"), (_, ct) => fake.ConnectionAsync(ct));
        JenkinsInstance prod = pool.Resolve("prod");
        await pool.ListToolsAsync(prod, false, TestContext.CancellationToken);

        using CancellationTokenSource cts = new();
        await cts.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => pool.CallToolAsync(prod, "getJob", null, cts.Token));

        await pool.CallToolAsync(prod, "getJob", null, TestContext.CancellationToken);
        Assert.AreEqual(1, fake.ConnectCount);
    }

    [TestMethod]
    public async Task NonCallerCancellation_IsWrappedAndEvictsClient()
    {
        await using FakeJenkinsServer fake = new("getJob");
        int attempts = 0;
        await using JenkinsMcpClientPool pool = new(Options("prod"), (_, ct) =>
        {
            if (++attempts == 1)
                throw new TaskCanceledException("HttpClient timeout");
            return fake.ConnectionAsync(ct);
        });
        JenkinsInstance prod = pool.Resolve("prod");

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() => pool.CallToolAsync(prod, "getJob", null, TestContext.CancellationToken));
        Assert.Contains("HttpClient timeout", ex.Message);

        await pool.CallToolAsync(prod, "getJob", null, TestContext.CancellationToken);
        Assert.AreEqual(2, attempts);
    }

    [TestMethod]
    public async Task UpstreamProtocolError_IsWrappedWithInstanceAndKeepsClient()
    {
        await using FakeJenkinsServer fake = new("getJob") { FailCallWithProtocolError = true };
        await using JenkinsMcpClientPool pool = new(Options("prod"), (_, ct) => fake.ConnectionAsync(ct));
        JenkinsInstance prod = pool.Resolve("prod");

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() => pool.CallToolAsync(prod, "getJob", null, TestContext.CancellationToken));
        Assert.Contains("Jenkins 'prod' returned error -32602", ex.Message);
        Assert.Contains("upstream failure", ex.Message);
        Assert.IsInstanceOfType<McpProtocolException>(ex.InnerException);
        fake.FailCallWithProtocolError = false;
        await pool.CallToolAsync(prod, "getJob", null, TestContext.CancellationToken);

        Assert.AreEqual(1, fake.ConnectCount);
    }

    [TestMethod]
    public void Resolve_UnknownOrBlank_ThrowsWithRegisteredNamesWithoutToken()
    {
        using JenkinsMcpClientPool pool = new(Options("prod", "dev"), (_, _) => throw new AssertFailedException("must not connect"));

        foreach (string? name in new[] { null, " ", "qa" })
        {
            McpException ex = Assert.ThrowsExactly<McpException>(() => pool.Resolve(name));
            Assert.Contains("prod, dev", ex.Message);
            Assert.DoesNotContain(Token, ex.Message);
        }
    }

    [TestMethod]
    public void TransportOptions_UseStreamableHttpAndBasicAuthorization()
    {
        HttpClientTransportOptions options = JenkinsMcpClientPool.CreateTransportOptions(Instance("prod"));

        Assert.AreEqual(new Uri("http://prod.example:8080/mcp-server/mcp"), options.Endpoint);
        Assert.AreEqual(HttpTransportMode.StreamableHttp, options.TransportMode);
        string expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"builder:{Token}"));
        Assert.AreEqual(expected, options.AdditionalHeaders!["Authorization"]);
    }

    [TestMethod]
    public async Task Dispose_DisposesClientsAndOwnedResources()
    {
        await using FakeJenkinsServer fake = new("getJob");
        TrackingOwner owner = new();
        JenkinsConnection? created = null;
        JenkinsMcpClientPool pool = new(Options("prod"), async (_, ct) => created = await fake.ConnectionAsync(ct, owner));
        await pool.ListToolsAsync(pool.Resolve("prod"), false, TestContext.CancellationToken);

        await pool.DisposeAsync();

        Assert.IsTrue(owner.Disposed);
        await Assert.ThrowsAsync<Exception>(async () => await created!.Client.ListToolsAsync(cancellationToken: TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task SdkMcpExceptionFromConnect_IsWrappedAndReconnects()
    {
        await using FakeJenkinsServer fake = new("getJob");
        int attempts = 0;
        await using JenkinsMcpClientPool pool = new(Options("prod"), (_, ct) =>
        {
            if (++attempts == 1)
                throw new McpException("Streamable HTTP POST response completed without a reply");
            return fake.ConnectionAsync(ct);
        });
        JenkinsInstance prod = pool.Resolve("prod");

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() => pool.CallToolAsync(prod, "getJob", null, TestContext.CancellationToken));
        Assert.StartsWith("Jenkins 'prod' request failed", ex.Message);

        await pool.CallToolAsync(prod, "getJob", null, TestContext.CancellationToken);
        Assert.AreEqual(2, attempts);
    }

    [TestMethod]
    public async Task Evict_DoesNotWaitForConnectionDisposal_AndDisposesOwner()
    {
        await using FakeJenkinsServer fake = new("getJob");
        TrackingOwner hanging = new(hang: true);
        int attempts = 0;
        await using JenkinsMcpClientPool pool = new(Options("prod"), async (_, ct) =>
        {
            if (++attempts > 1)
                return await fake.ConnectionAsync(ct);
            // 첫 연결은 client를 미리 닫아 호출이 전송 오류로 끝나게 한다
            McpClient client = await fake.ConnectAsync(ct);
            await client.DisposeAsync();
            return new JenkinsConnection(client, hanging);
        });
        JenkinsInstance prod = pool.Resolve("prod");

        Task<CallToolResult> call = pool.CallToolAsync(prod, "getJob", null, TestContext.CancellationToken);
        Task finished = await Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds(10), TestContext.CancellationToken));

        Assert.AreSame(call, finished, "실패 응답이 연결 dispose 완료를 기다렸다");
        await Assert.ThrowsExactlyAsync<McpException>(() => call);
        Assert.IsTrue(hanging.DisposeStarted);
        Assert.IsFalse(hanging.Disposed);

        await pool.CallToolAsync(prod, "getJob", null, TestContext.CancellationToken);
        Assert.AreEqual(2, attempts);
    }
}
