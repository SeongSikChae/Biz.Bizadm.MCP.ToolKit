using System.Net;
using System.Text;
using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.NuGet;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tests.NuGet;

[TestClass]
public sealed class NuGetFeedClientTests
{
    internal const string ApiKey = "secret-api-key";
    internal const string Password = "secret-password";
    internal const string InternalIndex = "https://pkgs.example.com/v3/index.json";
    internal const string OrgIndex = "https://api.nuget.org/v3/index.json";

    public TestContext TestContext { get; set; } = null!;

    internal static NuGetOptions Options() => new()
    {
        Instances =
        [
            new NuGetInstance { Name = "internal", ServiceIndex = InternalIndex, ApiKey = ApiKey, User = "builder", Password = Password },
            new NuGetInstance { Name = "org", ServiceIndex = OrgIndex },
            new NuGetInstance { Name = "plain", ServiceIndex = "http://feed.local/v3/index.json", ApiKey = ApiKey },
        ],
    };

    /// <summary>service index JSON. 기본 리소스는 같은 호스트의 search·base·registrations·publish</summary>
    internal static string Index(params (string Type, string Id)[] resources)
    {
        (string Type, string Id)[] list = resources.Length > 0 ? resources :
        [
            ("SearchQueryService/3.5.0", "https://pkgs.example.com/query"),
            ("PackageBaseAddress/3.0.0", "https://pkgs.example.com/flat/"),
            ("RegistrationsBaseUrl/3.6.0", "https://pkgs.example.com/reg"),
            ("PackagePublish/2.0.0", "https://pkgs.example.com/api/v2/package"),
        ];
        JsonArray array = [.. list.Select(r => (JsonNode)new JsonObject { ["@id"] = r.Id, ["@type"] = r.Type })];
        return new JsonObject { ["version"] = "3.0.0", ["resources"] = array }.ToJsonString();
    }

    private static NuGetFeedClient Create(NuGetRecordingHandler handler, TimeSpan? readTimeout = null, long? maxBytes = null)
        => new(Options(), handler, readTimeout, null, maxBytes);

    [TestMethod]
    public async Task GetResource_PicksHighestPriorityAndFirstOfSameType()
    {
        NuGetRecordingHandler handler = new();
        using NuGetFeedClient client = Create(handler);
        handler.Enqueue(HttpStatusCode.OK, Index(
            ("SearchQueryService", "https://pkgs.example.com/s0"),
            ("SearchQueryService/3.0.0-beta", "https://pkgs.example.com/beta"),
            ("SearchQueryService/3.0.0-rc", "https://pkgs.example.com/rc1"),
            ("SearchQueryService/3.0.0-rc", "https://pkgs.example.com/rc2"),
            ("RegistrationsBaseUrl/3.0.0-beta", "https://pkgs.example.com/regbeta")));

        NuGetInstance instance = client.Resolve("internal");
        Uri search = await client.GetResourceAsync(instance, NuGetResource.Search, TestContext.CancellationToken);
        Uri reg = await client.GetResourceAsync(instance, NuGetResource.Registrations, TestContext.CancellationToken);

        Assert.AreEqual("https://pkgs.example.com/rc1", search.AbsoluteUri);
        Assert.AreEqual("https://pkgs.example.com/regbeta", reg.AbsoluteUri);
        Assert.HasCount(1, handler.Requests); // 캐시
        Assert.AreEqual(InternalIndex, handler.Last.Uri.AbsoluteUri);
    }

    [TestMethod]
    public async Task GetResource_TypeArrayIsExpanded_AndNonHttpIdsSkipped()
    {
        NuGetRecordingHandler handler = new();
        using NuGetFeedClient client = Create(handler);
        handler.Enqueue(HttpStatusCode.OK, """
            {"resources":[
              {"@id":"file:///c:/x","@type":"PackagePublish/2.0.0"},
              {"@id":"relative/path","@type":"PackagePublish/2.0.0"},
              {"@id":"https://pkgs.example.com/push","@type":["Other","PackagePublish/2.0.0"]},
              "not-an-object"
            ]}
            """);

        Uri publish = await client.GetResourceAsync(client.Resolve("internal"), NuGetResource.Publish, TestContext.CancellationToken);

        Assert.AreEqual("https://pkgs.example.com/push", publish.AbsoluteUri);
    }

    [TestMethod]
    public async Task GetResource_Missing_NamesPurposeAndTypes()
    {
        NuGetRecordingHandler handler = new();
        using NuGetFeedClient client = Create(handler);
        handler.Enqueue(HttpStatusCode.OK, Index(("SearchQueryService/3.5.0", "https://pkgs.example.com/q")));

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() =>
            client.GetResourceAsync(client.Resolve("internal"), NuGetResource.PackageBase, TestContext.CancellationToken));

        Assert.AreEqual("NuGet 'internal' service index has no package base address resource (PackageBaseAddress/3.0.0).", ex.Message);
    }

    [TestMethod]
    [DataRow("""{"version":"3.0.0"}""")]
    [DataRow("""[1,2]""")]
    public async Task GetResource_NoResourcesArray_UnexpectedJson(string body)
    {
        NuGetRecordingHandler handler = new();
        using NuGetFeedClient client = Create(handler);
        handler.Enqueue(HttpStatusCode.OK, body);

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() =>
            client.GetResourceAsync(client.Resolve("internal"), NuGetResource.Search, TestContext.CancellationToken));

        Assert.AreEqual($"NuGet 'internal' {InternalIndex} returned unexpected JSON: resources array is missing", ex.Message);
    }

    [TestMethod]
    public async Task GetResource_FailureIsNotCached()
    {
        NuGetRecordingHandler handler = new();
        using NuGetFeedClient client = Create(handler);
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, "down");
        handler.Enqueue(HttpStatusCode.OK, Index());
        NuGetInstance instance = client.Resolve("internal");

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() =>
            client.GetResourceAsync(instance, NuGetResource.Search, TestContext.CancellationToken));
        Uri search = await client.GetResourceAsync(instance, NuGetResource.Search, TestContext.CancellationToken);

        Assert.AreEqual($"NuGet 'internal' GET {InternalIndex} failed (HTTP 503): down", ex.Message);
        Assert.AreEqual("https://pkgs.example.com/query", search.AbsoluteUri);
        Assert.HasCount(2, handler.Requests);
    }

    [TestMethod]
    public async Task GetResource_ServiceIndex404_IsGeneralFailure()
    {
        NuGetRecordingHandler handler = new();
        using NuGetFeedClient client = Create(handler);
        handler.Enqueue(HttpStatusCode.NotFound, "", "text/plain", r => r.ReasonPhrase = "Not Found");

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() =>
            client.GetResourceAsync(client.Resolve("internal"), NuGetResource.Search, TestContext.CancellationToken));

        Assert.AreEqual($"NuGet 'internal' GET {InternalIndex} failed (HTTP 404): Not Found", ex.Message);
    }

    [TestMethod]
    public async Task GetResource_ConcurrentCallsShareOneIndexRequest()
    {
        NuGetRecordingHandler handler = new();
        using NuGetFeedClient client = Create(handler);
        TaskCompletionSource<HttpResponseMessage> gate = handler.EnqueueGate();
        NuGetInstance instance = client.Resolve("internal");

        Task<Uri> first = client.GetResourceAsync(instance, NuGetResource.Search, TestContext.CancellationToken);
        Task<Uri> second = client.GetResourceAsync(instance, NuGetResource.Publish, TestContext.CancellationToken);
        gate.SetResult(NuGetRecordingHandler.Response(HttpStatusCode.OK, Index()));
        await Task.WhenAll(first, second);

        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    public async Task GetResource_OneCallerCancels_OthersAndCacheSurvive()
    {
        NuGetRecordingHandler handler = new();
        using NuGetFeedClient client = Create(handler);
        TaskCompletionSource<HttpResponseMessage> gate = handler.EnqueueGate();
        NuGetInstance instance = client.Resolve("internal");
        using CancellationTokenSource cancel = new();

        Task<Uri> cancelled = client.GetResourceAsync(instance, NuGetResource.Search, cancel.Token);
        Task<Uri> other = client.GetResourceAsync(instance, NuGetResource.Search, TestContext.CancellationToken);
        await cancel.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => cancelled);
        gate.SetResult(NuGetRecordingHandler.Response(HttpStatusCode.OK, Index()));
        await other;
        await client.GetResourceAsync(instance, NuGetResource.Search, TestContext.CancellationToken);

        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    public async Task GetResource_AllCallersCancelledThenLoadFails_NextCallReloads()
    {
        NuGetRecordingHandler handler = new();
        using NuGetFeedClient client = Create(handler);
        TaskCompletionSource<HttpResponseMessage> gate = handler.EnqueueGate();
        handler.Enqueue(HttpStatusCode.OK, Index());
        NuGetInstance instance = client.Resolve("internal");
        using CancellationTokenSource cancel = new();

        Task<Uri> cancelled = client.GetResourceAsync(instance, NuGetResource.Search, cancel.Token);
        await cancel.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => cancelled);
        gate.SetResult(NuGetRecordingHandler.Response(HttpStatusCode.InternalServerError, "boom"));
        // 공유 로드가 실패로 끝나기를 기다린다(시간이 아니라 상태로 판정, 최대 10초)
        using (CancellationTokenSource wait = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken))
        {
            wait.CancelAfter(TimeSpan.FromSeconds(10));
            while (!client.IsIndexLoadFaulted("internal"))
                await Task.Delay(10, wait.Token);
        }

        Uri search = await client.GetResourceAsync(instance, NuGetResource.Search, TestContext.CancellationToken);

        Assert.AreEqual("https://pkgs.example.com/query", search.AbsoluteUri);
        Assert.HasCount(2, handler.Requests);
    }

    [TestMethod]
    public async Task GetResource_HttpsInstanceWithHttpPublish_Rejected()
    {
        NuGetRecordingHandler handler = new();
        using NuGetFeedClient client = Create(handler);
        handler.Enqueue(HttpStatusCode.OK, Index(("PackagePublish/2.0.0", "http://pkgs.example.com/push")));

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() =>
            client.GetResourceAsync(client.Resolve("internal"), NuGetResource.Publish, TestContext.CancellationToken));

        Assert.AreEqual("NuGet 'internal' publish resource is not https; ApiKey would be sent in clear text.", ex.Message);
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    public async Task GetResource_HttpsInstanceWithHttpSymbolPublish_Rejected()
    {
        NuGetRecordingHandler handler = new();
        using NuGetFeedClient client = Create(handler);
        handler.Enqueue(HttpStatusCode.OK, Index(("SymbolPackagePublish/4.9.0", "http://pkgs.example.com/symbols")));

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() =>
            client.GetResourceAsync(client.Resolve("internal"), NuGetResource.SymbolPublish, TestContext.CancellationToken));

        Assert.AreEqual("NuGet 'internal' symbol publish resource is not https; ApiKey would be sent in clear text.", ex.Message);
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    public async Task GetResource_SymbolPublish_FoundOrMissing()
    {
        NuGetRecordingHandler handler = new();
        using NuGetFeedClient client = Create(handler);
        handler.Enqueue(HttpStatusCode.OK, Index(("SymbolPackagePublish/4.9.0", "https://pkgs.example.com/api/v2/symbolpackage")));

        Uri symbols = await client.GetResourceAsync(client.Resolve("internal"), NuGetResource.SymbolPublish, TestContext.CancellationToken);
        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() =>
            client.GetResourceAsync(client.Resolve("internal"), NuGetResource.Publish, TestContext.CancellationToken));

        Assert.AreEqual("https://pkgs.example.com/api/v2/symbolpackage", symbols.AbsoluteUri);
        Assert.AreEqual("NuGet 'internal' service index has no publish resource (PackagePublish/2.0.0).", ex.Message);
    }

    [TestMethod]
    public async Task GetResource_NoSymbolPublish_Fails()
    {
        NuGetRecordingHandler handler = new();
        using NuGetFeedClient client = Create(handler);
        handler.Enqueue(HttpStatusCode.OK, Index());

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() =>
            client.GetResourceAsync(client.Resolve("internal"), NuGetResource.SymbolPublish, TestContext.CancellationToken));

        Assert.AreEqual("NuGet 'internal' service index has no symbol publish resource (SymbolPackagePublish/4.9.0).", ex.Message);
    }

    [TestMethod]
    public async Task GetResource_HttpInstanceWithHttpPublish_Allowed()
    {
        NuGetRecordingHandler handler = new();
        using NuGetFeedClient client = Create(handler);
        handler.Enqueue(HttpStatusCode.OK, Index(("PackagePublish/2.0.0", "http://feed.local/push")));

        Uri publish = await client.GetResourceAsync(client.Resolve("plain"), NuGetResource.Publish, TestContext.CancellationToken);

        Assert.AreEqual("http://feed.local/push", publish.AbsoluteUri);
    }

    [TestMethod]
    public async Task GetResource_PathResourceWithQuery_Unexpected_SearchAllowed()
    {
        NuGetRecordingHandler handler = new();
        using NuGetFeedClient client = Create(handler);
        handler.Enqueue(HttpStatusCode.OK, Index(
            ("SearchQueryService/3.5.0", "https://pkgs.example.com/q?feed=a"),
            ("RegistrationsBaseUrl/3.6.0", "https://pkgs.example.com/reg?feed=a")));
        NuGetInstance instance = client.Resolve("internal");

        Uri search = await client.GetResourceAsync(instance, NuGetResource.Search, TestContext.CancellationToken);
        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() =>
            client.GetResourceAsync(instance, NuGetResource.Registrations, TestContext.CancellationToken));

        Assert.AreEqual("https://pkgs.example.com/q?feed=a", search.AbsoluteUri);
        Assert.AreEqual($"NuGet 'internal' {InternalIndex} returned unexpected JSON: registrations @id must not contain query or fragment", ex.Message);
    }

    [TestMethod]
    public async Task ProvidesVulnerabilityInfo_ByResourcePresence()
    {
        NuGetRecordingHandler handler = new();
        using NuGetFeedClient client = Create(handler);
        handler.Enqueue(HttpStatusCode.OK, Index(("VulnerabilityInfo/6.7.0", "https://api.nuget.org/v3/vulnerabilities/index.json")));
        handler.Enqueue(HttpStatusCode.OK, Index());

        Assert.IsTrue(await client.ProvidesVulnerabilityInfoAsync(client.Resolve("org"), TestContext.CancellationToken));
        Assert.IsFalse(await client.ProvidesVulnerabilityInfoAsync(client.Resolve("internal"), TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task Headers_BasicOnlySameOrigin_ApiKeyOnlyPublish()
    {
        NuGetRecordingHandler handler = new();
        using NuGetFeedClient client = Create(handler);
        NuGetInstance instance = client.Resolve("internal");
        handler.Enqueue(HttpStatusCode.OK, "{}");
        handler.Enqueue(HttpStatusCode.OK, "{}");
        handler.Enqueue(HttpStatusCode.OK, "{}");
        handler.Enqueue(HttpStatusCode.OK, "{}");
        handler.Enqueue(HttpStatusCode.Created, "");

        await client.GetJsonAsync(instance, new Uri("https://pkgs.example.com/reg/a/index.json"), TestContext.CancellationToken);
        await client.GetJsonAsync(instance, new Uri("https://other.example.com/reg/a/index.json"), TestContext.CancellationToken);
        await client.GetJsonAsync(instance, new Uri("http://pkgs.example.com/reg/a/index.json"), TestContext.CancellationToken);
        await client.GetJsonAsync(instance, new Uri("https://pkgs.example.com:8443/reg/a/index.json"), TestContext.CancellationToken);
        await client.SendPublishAsync(instance, HttpMethod.Put, new Uri("https://pkgs.example.com/api/v2/package"), null, TestContext.CancellationToken);

        IReadOnlyList<(HttpMethod Method, Uri Uri, string? Body, Dictionary<string, string> Headers)> requests = handler.Requests;
        string basic = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("builder:" + Password));
        Assert.AreEqual(basic, requests[0].Headers["Authorization"]);
        Assert.IsFalse(requests[1].Headers.ContainsKey("Authorization"));   // 다른 host
        Assert.IsFalse(requests[2].Headers.ContainsKey("Authorization"));   // 다른 scheme
        Assert.IsFalse(requests[3].Headers.ContainsKey("Authorization"));   // 다른 port
        for (int i = 0; i < 4; i++)
            Assert.IsFalse(requests[i].Headers.ContainsKey("X-NuGet-ApiKey"), i.ToString());
        Assert.AreEqual(basic, requests[4].Headers["Authorization"]);
        Assert.AreEqual(ApiKey, requests[4].Headers["X-NuGet-ApiKey"]);
        foreach (var r in requests)
        {
            StringAssert.StartsWith(r.Headers["User-Agent"], "Biz.Bizadm.MCP.ToolKit/");
            Assert.AreEqual("4.1.0", r.Headers["X-NuGet-Protocol-Version"]);
        }
    }

    [TestMethod]
    public async Task GetJson_404_ReturnsNull_UnlessDisabled()
    {
        NuGetRecordingHandler handler = new();
        using NuGetFeedClient client = Create(handler);
        NuGetInstance instance = client.Resolve("org");
        handler.Enqueue(HttpStatusCode.NotFound, "");
        handler.Enqueue(HttpStatusCode.NotFound, "missing");
        Uri uri = new("https://api.nuget.org/v3/x");

        Assert.IsNull(await client.GetJsonAsync(instance, uri, TestContext.CancellationToken));
        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() =>
            client.GetJsonAsync(instance, uri, TestContext.CancellationToken, notFoundAsNull: false));
        Assert.AreEqual("NuGet 'org' GET https://api.nuget.org/v3/x failed (HTTP 404): missing", ex.Message);
    }

    [TestMethod]
    public async Task GetJson_Redirect_FailsWithLocationWithoutQueryOrUserInfo()
    {
        NuGetRecordingHandler handler = new();
        using NuGetFeedClient client = Create(handler);
        handler.Enqueue(HttpStatusCode.Found, "", "text/plain",
            r => r.Headers.Location = new Uri("https://u:p@cdn.example.com/x?sig=secret"));

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() =>
            client.GetJsonAsync(client.Resolve("org"), new Uri("https://api.nuget.org/v3/x?q=1"), TestContext.CancellationToken));

        Assert.AreEqual("NuGet 'org' GET https://api.nuget.org/v3/x failed (HTTP 302): redirected to https://cdn.example.com/x", ex.Message);
    }

    [TestMethod]
    public void Display_RemovesUserInfoAndQuery()
        => Assert.AreEqual("https://h.example:8443/a/b", NuGetFeedClient.Display(new Uri("https://u:p@h.example:8443/a/b?k=v#f")));

    [TestMethod]
    public async Task Failure_AuthHints()
    {
        NuGetRecordingHandler handler = new();
        using NuGetFeedClient client = Create(handler);
        NuGetInstance instance = client.Resolve("internal");
        handler.Enqueue(HttpStatusCode.Unauthorized, "");
        handler.Enqueue(HttpStatusCode.Forbidden, "", "text/plain", r => r.ReasonPhrase = "Forbidden");

        McpException read = await Assert.ThrowsExactlyAsync<McpException>(() =>
            client.GetJsonAsync(instance, new Uri("https://pkgs.example.com/x"), TestContext.CancellationToken));
        McpException write = await Assert.ThrowsExactlyAsync<McpException>(() =>
            client.SendPublishAsync(instance, HttpMethod.Delete, new Uri("https://pkgs.example.com/p/a/1.0.0"), null, TestContext.CancellationToken));

        StringAssert.EndsWith(read.Message, "(check User/Password; Basic is sent only to the ServiceIndex origin)");
        Assert.AreEqual("NuGet 'internal' DELETE https://pkgs.example.com/p/a/1.0.0 failed (HTTP 403): Forbidden "
            + "(check ApiKey and its package scope; Basic is sent only to the ServiceIndex origin)", write.Message);
        Assert.IsFalse(read.Message.Contains(Password) || write.Message.Contains(ApiKey));
    }

    [TestMethod]
    public async Task SendPublish_ReturnsConflictWithBodyAndReason()
    {
        NuGetRecordingHandler handler = new();
        using NuGetFeedClient client = Create(handler);
        handler.Enqueue(HttpStatusCode.Conflict, "already exists", "text/plain", r => r.ReasonPhrase = "Conflict");

        (int status, string body, string? reason) = await client.SendPublishAsync(client.Resolve("internal"), HttpMethod.Put,
            new Uri("https://pkgs.example.com/api/v2/package"), null, TestContext.CancellationToken);

        Assert.AreEqual(409, status);
        Assert.AreEqual("already exists", body);
        Assert.AreEqual("Conflict", reason);
    }

    [TestMethod]
    public async Task Send_TransportError()
    {
        NuGetRecordingHandler handler = new();
        using NuGetFeedClient client = Create(handler);
        handler.EnqueueException(new HttpRequestException("connection refused"));

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() =>
            client.GetJsonAsync(client.Resolve("org"), new Uri("https://api.nuget.org/v3/x"), TestContext.CancellationToken));

        Assert.AreEqual("NuGet 'org' GET https://api.nuget.org/v3/x failed: connection refused", ex.Message);
    }

    [TestMethod]
    public async Task Send_Timeout()
    {
        NuGetRecordingHandler handler = new();
        using NuGetFeedClient client = Create(handler, readTimeout: TimeSpan.FromMilliseconds(50));
        handler.EnqueueDelay(TimeSpan.FromSeconds(10));

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() =>
            client.GetJsonAsync(client.Resolve("org"), new Uri("https://api.nuget.org/v3/x"), TestContext.CancellationToken));

        Assert.AreEqual("NuGet 'org' GET https://api.nuget.org/v3/x timed out after 0s.", ex.Message);
    }

    [TestMethod]
    public async Task Send_CallerCancel_PropagatesOperationCanceled()
    {
        NuGetRecordingHandler handler = new();
        using NuGetFeedClient client = Create(handler);
        handler.EnqueueDelay(TimeSpan.FromSeconds(10));
        using CancellationTokenSource cancel = new(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            client.GetJsonAsync(client.Resolve("org"), new Uri("https://api.nuget.org/v3/x"), cancel.Token));
    }

    [TestMethod]
    public async Task GetJson_InvalidJson_DuplicateKeys_AndBom()
    {
        NuGetRecordingHandler handler = new();
        using NuGetFeedClient client = Create(handler);
        NuGetInstance instance = client.Resolve("org");
        Uri uri = new("https://api.nuget.org/v3/x");
        handler.Enqueue(HttpStatusCode.OK, "{not json");
        handler.Enqueue(HttpStatusCode.OK, """{"a":1,"a":2}""");
        handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("""{"ok":true}""")]),
        });

        McpException invalid = await Assert.ThrowsExactlyAsync<McpException>(() => client.GetJsonAsync(instance, uri, TestContext.CancellationToken));
        McpException duplicate = await Assert.ThrowsExactlyAsync<McpException>(() => client.GetJsonAsync(instance, uri, TestContext.CancellationToken));
        JsonNode? bom = await client.GetJsonAsync(instance, uri, TestContext.CancellationToken);

        StringAssert.StartsWith(invalid.Message, "NuGet 'org' https://api.nuget.org/v3/x returned invalid JSON: ");
        StringAssert.StartsWith(duplicate.Message, "NuGet 'org' https://api.nuget.org/v3/x returned invalid JSON: ");
        Assert.IsTrue((bool)bom!["ok"]!);
    }

    [TestMethod]
    public async Task GetJson_ResponseTooLarge()
    {
        NuGetRecordingHandler handler = new();
        using NuGetFeedClient client = Create(handler, maxBytes: 10);
        NuGetInstance instance = client.Resolve("org");
        Uri uri = new("https://api.nuget.org/v3/x");
        handler.Enqueue(HttpStatusCode.OK, """{"data":"0123456789"}""");   // Content-Length 초과
        handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes("""{"data":"0123456789"}"""))), // 길이 미상
        });

        McpException byHeader = await Assert.ThrowsExactlyAsync<McpException>(() => client.GetJsonAsync(instance, uri, TestContext.CancellationToken));
        McpException byBody = await Assert.ThrowsExactlyAsync<McpException>(() => client.GetJsonAsync(instance, uri, TestContext.CancellationToken));

        Assert.AreEqual("NuGet 'org' GET https://api.nuget.org/v3/x response exceeded 10 bytes.", byHeader.Message);
        Assert.AreEqual(byHeader.Message, byBody.Message);
    }

    [TestMethod]
    public void DefaultHandler_DisablesRedirectAndDecompresses()
    {
        using SocketsHttpHandler handler = NuGetFeedClient.CreateDefaultHandler();
        Assert.IsFalse(handler.AllowAutoRedirect);
        Assert.AreEqual(DecompressionMethods.All, handler.AutomaticDecompression);
    }

    [TestMethod]
    public void Child_AppendsSlash()
    {
        Assert.AreEqual("https://h/flat/a/index.json", NuGetFeedClient.Child(new Uri("https://h/flat"), "a/index.json").AbsoluteUri);
        Assert.AreEqual("https://h/flat/a/index.json", NuGetFeedClient.Child(new Uri("https://h/flat/"), "a/index.json").AbsoluteUri);
    }
}
