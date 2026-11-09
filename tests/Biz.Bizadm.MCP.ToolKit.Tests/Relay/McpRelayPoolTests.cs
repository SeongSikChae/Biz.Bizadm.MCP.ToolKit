using System.Net;
using System.Text;

using Biz.Bizadm.MCP.ToolKit.Jenkins;
using Biz.Bizadm.MCP.ToolKit.Relay;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Biz.Bizadm.MCP.ToolKit.Tests.Relay;

[TestClass]
public sealed class McpRelayPoolTests
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

    private static IEnumerable<McpRelayTarget> Targets(params string[] names)
        => names.Select(Instance).Select(JenkinsMcpServerBuilderExtensions.ToTarget);

    private static McpRelayPool Pool(Func<McpRelayTarget, CancellationToken, Task<McpRelayConnection>> connect, params string[] names)
        => new("Jenkins", Targets(names), connect);

    [TestMethod]
    public async Task ListTools_SameInstance_ConnectsOnceAndCaches()
    {
        await using FakeMcpServer fake = new("getJob", "triggerBuild");
        await using McpRelayPool pool = Pool((_, ct) => fake.ConnectionAsync(ct), "prod");
        McpRelayTarget prod = pool.Resolve("prod");

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
        await using FakeMcpServer fake = new("getJob");
        await using McpRelayPool pool = Pool((_, ct) => fake.ConnectionAsync(ct), "prod");
        McpRelayTarget prod = pool.Resolve("PROD");

        await pool.ListToolsAsync(prod, refresh: false, TestContext.CancellationToken);
        await pool.ListToolsAsync(prod, refresh: true, TestContext.CancellationToken);

        Assert.AreEqual(2, fake.ListToolsCount);
        Assert.AreEqual(1, fake.ConnectCount);
    }

    [TestMethod]
    public async Task CallTool_NewConnection_ListsToolsOnceBeforeFirstCall()
    {
        await using FakeMcpServer fake = new("getJob");
        await using McpRelayPool pool = Pool((_, ct) => fake.ConnectionAsync(ct), "prod");
        McpRelayTarget prod = pool.Resolve("prod");

        await pool.CallToolAsync(prod, "getJob", null, TestContext.CancellationToken);
        Assert.AreEqual(1, fake.ListToolsCount);
        await pool.CallToolAsync(prod, "getJob", null, TestContext.CancellationToken);
        Assert.AreEqual(1, fake.ListToolsCount);
        Assert.AreEqual(2, fake.CallToolCount);

        // 호출 전에 받은 목록으로 풀 캐시도 채워 list_tools가 upstream을 다시 부르지 않는다
        IReadOnlyList<Tool> tools = await pool.ListToolsAsync(prod, refresh: false, TestContext.CancellationToken);
        Assert.HasCount(1, tools);
        Assert.AreEqual(1, fake.ListToolsCount);
    }

    [TestMethod]
    public async Task CallTool_AfterReconnect_ListsToolsAgainOnNewConnection()
    {
        await using FakeMcpServer fake = new("getJob");
        int attempts = 0;
        McpClient? first = null;
        await using McpRelayPool pool = Pool(async (_, ct) =>
        {
            if (++attempts > 1)
                return await fake.ConnectionAsync(ct);
            first = await fake.ConnectAsync(ct);
            return new McpRelayConnection(first, new TrackingOwner());
        }, "prod");
        McpRelayTarget prod = pool.Resolve("prod");
        await pool.ListToolsAsync(prod, refresh: false, TestContext.CancellationToken);
        Assert.AreEqual(1, fake.ListToolsCount);

        // 첫 연결을 끊어 다음 호출이 전송 오류로 끝나고 재연결되게 한다
        await first!.DisposeAsync();
        await Assert.ThrowsExactlyAsync<McpException>(() => pool.CallToolAsync(prod, "getJob", null, TestContext.CancellationToken));

        await pool.CallToolAsync(prod, "getJob", null, TestContext.CancellationToken);
        Assert.AreEqual(2, attempts);
        Assert.AreEqual(2, fake.ListToolsCount, "새 연결에서 호출 전에 목록을 다시 받아야 한다");
    }

    [TestMethod]
    public async Task CallTool_HeaderMismatchOnce_RelistsAndRetriesOnSameConnection()
    {
        await using FakeMcpServer fake = new("getJob") { HeaderMismatchFailures = 1 };
        await using McpRelayPool pool = Pool((_, ct) => fake.ConnectionAsync(ct), "prod");
        McpRelayTarget prod = pool.Resolve("prod");

        CallToolResult result = await pool.CallToolAsync(prod, "getJob", null, TestContext.CancellationToken);

        Assert.IsFalse(result.IsError ?? false);
        Assert.AreEqual(2, fake.CallToolCount);
        Assert.AreEqual(2, fake.ListToolsCount, "첫 호출 전 1회 + 재시도 전 1회");
        Assert.AreEqual(1, fake.ConnectCount);
    }

    [TestMethod]
    public async Task CallTool_HeaderMismatchTwice_RetriesOnlyOnceAndKeepsConnection()
    {
        await using FakeMcpServer fake = new("getJob") { HeaderMismatchFailures = 2 };
        await using McpRelayPool pool = Pool((_, ct) => fake.ConnectionAsync(ct), "prod");
        McpRelayTarget prod = pool.Resolve("prod");

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() => pool.CallToolAsync(prod, "getJob", null, TestContext.CancellationToken));

        Assert.StartsWith("Jenkins 'prod' returned error -32020", ex.Message);
        Assert.AreEqual(2, fake.CallToolCount);
        await pool.CallToolAsync(prod, "getJob", null, TestContext.CancellationToken);
        Assert.AreEqual(1, fake.ConnectCount);
    }

    [TestMethod]
    public async Task CallTool_OtherProtocolError_IsNotRetried()
    {
        await using FakeMcpServer fake = new("getJob") { FailCallWithProtocolError = true };
        await using McpRelayPool pool = Pool((_, ct) => fake.ConnectionAsync(ct), "prod");

        await Assert.ThrowsExactlyAsync<McpException>(() => pool.CallToolAsync(pool.Resolve("prod"), "getJob", null, TestContext.CancellationToken));

        Assert.AreEqual(1, fake.CallToolCount);
    }

    [TestMethod]
    public async Task ListTools_DifferentInstances_ReturnTheirOwnTools()
    {
        await using FakeMcpServer prodFake = new("getJob", "triggerBuild");
        await using FakeMcpServer devFake = new("getJob");
        await using McpRelayPool pool = Pool(
            (i, ct) => i.Name == "prod" ? prodFake.ConnectionAsync(ct) : devFake.ConnectionAsync(ct), "prod", "dev");

        IReadOnlyList<Tool> prod = await pool.ListToolsAsync(pool.Resolve("prod"), false, TestContext.CancellationToken);
        IReadOnlyList<Tool> dev = await pool.ListToolsAsync(pool.Resolve("dev"), false, TestContext.CancellationToken);

        Assert.HasCount(2, prod);
        Assert.HasCount(1, dev);
    }

    [TestMethod]
    public async Task ConnectFailure_WrapsWithStatusWithoutToken_AndReconnectsNextCall()
    {
        await using FakeMcpServer fake = new("getJob");
        int attempts = 0;
        await using McpRelayPool pool = Pool((_, ct) =>
        {
            if (++attempts == 1)
                throw new InvalidOperationException("connect failed", new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized));
            return fake.ConnectionAsync(ct);
        }, "prod");
        McpRelayTarget prod = pool.Resolve("prod");

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
        await using FakeMcpServer fake = new("getJob");
        await using McpRelayPool pool = Pool((_, ct) => fake.ConnectionAsync(ct), "prod");
        McpRelayTarget prod = pool.Resolve("prod");
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
        await using FakeMcpServer fake = new("getJob");
        int attempts = 0;
        await using McpRelayPool pool = Pool((_, ct) =>
        {
            if (++attempts == 1)
                throw new TaskCanceledException("HttpClient timeout");
            return fake.ConnectionAsync(ct);
        }, "prod");
        McpRelayTarget prod = pool.Resolve("prod");

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() => pool.CallToolAsync(prod, "getJob", null, TestContext.CancellationToken));
        Assert.Contains("HttpClient timeout", ex.Message);

        await pool.CallToolAsync(prod, "getJob", null, TestContext.CancellationToken);
        Assert.AreEqual(2, attempts);
    }

    [TestMethod]
    public async Task UpstreamProtocolError_IsWrappedWithInstanceAndKeepsClient()
    {
        await using FakeMcpServer fake = new("getJob") { FailCallWithProtocolError = true };
        await using McpRelayPool pool = Pool((_, ct) => fake.ConnectionAsync(ct), "prod");
        McpRelayTarget prod = pool.Resolve("prod");

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
        using McpRelayPool pool = Pool((_, _) => throw new AssertFailedException("must not connect"), "prod", "dev");

        foreach (string? name in new[] { null, " ", "qa" })
        {
            McpException ex = Assert.ThrowsExactly<McpException>(() => pool.Resolve(name));
            Assert.Contains("prod, dev", ex.Message);
            Assert.DoesNotContain(Token, ex.Message);
        }
        Assert.Contains("Unknown Jenkins instance 'qa'", Assert.ThrowsExactly<McpException>(() => pool.Resolve("qa")).Message);
    }

    [TestMethod]
    public void TransportOptions_UseStreamableHttpAndBasicAuthorization()
    {
        HttpClientTransportOptions options = McpRelayPool.CreateTransportOptions("Jenkins", Targets("prod").Single());

        Assert.AreEqual(new Uri("http://prod.example:8080/mcp-server/mcp"), options.Endpoint);
        Assert.AreEqual(HttpTransportMode.StreamableHttp, options.TransportMode);
        Assert.AreEqual("jenkins-prod", options.Name);
        string expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"builder:{Token}"));
        Assert.AreEqual(expected, options.AdditionalHeaders!["Authorization"]);
    }

    [TestMethod]
    public async Task Dispose_DisposesClientsAndOwnedResources()
    {
        await using FakeMcpServer fake = new("getJob");
        TrackingOwner owner = new();
        McpRelayConnection? created = null;
        McpRelayPool pool = Pool(async (_, ct) => created = await fake.ConnectionAsync(ct, owner), "prod");
        await pool.ListToolsAsync(pool.Resolve("prod"), false, TestContext.CancellationToken);

        await pool.DisposeAsync();

        Assert.IsTrue(owner.Disposed);
        await Assert.ThrowsAsync<Exception>(async () => await created!.Client.ListToolsAsync(cancellationToken: TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task SdkMcpExceptionFromConnect_IsWrappedAndReconnects()
    {
        await using FakeMcpServer fake = new("getJob");
        int attempts = 0;
        await using McpRelayPool pool = Pool((_, ct) =>
        {
            if (++attempts == 1)
                throw new McpException("Streamable HTTP POST response completed without a reply");
            return fake.ConnectionAsync(ct);
        }, "prod");
        McpRelayTarget prod = pool.Resolve("prod");

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() => pool.CallToolAsync(prod, "getJob", null, TestContext.CancellationToken));
        Assert.StartsWith("Jenkins 'prod' request failed", ex.Message);

        await pool.CallToolAsync(prod, "getJob", null, TestContext.CancellationToken);
        Assert.AreEqual(2, attempts);
    }

    [TestMethod]
    public async Task Evict_DoesNotWaitForConnectionDisposal_AndDisposesOwner()
    {
        await using FakeMcpServer fake = new("getJob");
        TrackingOwner hanging = new(hang: true);
        int attempts = 0;
        await using McpRelayPool pool = Pool(async (_, ct) =>
        {
            if (++attempts > 1)
                return await fake.ConnectionAsync(ct);
            // 첫 연결은 client를 미리 닫아 호출이 전송 오류로 끝나게 한다
            McpClient client = await fake.ConnectAsync(ct);
            await client.DisposeAsync();
            return new McpRelayConnection(client, hanging);
        }, "prod");
        McpRelayTarget prod = pool.Resolve("prod");

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
