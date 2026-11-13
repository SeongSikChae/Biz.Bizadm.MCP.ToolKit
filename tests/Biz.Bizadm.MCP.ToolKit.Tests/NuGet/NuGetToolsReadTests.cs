using System.Net;
using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.NuGet;
using Biz.Bizadm.MCP.ToolKit.Tools.NuGet;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tests.NuGet;

[TestClass]
public sealed class NuGetToolsReadTests
{
    public TestContext TestContext { get; set; } = null!;

    internal static (NuGetTools Tools, NuGetRecordingHandler Handler, NuGetFeedClient Client) Create(string? index = null)
    {
        NuGetRecordingHandler handler = new();
        NuGetFeedClient client = new(NuGetFeedClientTests.Options(), handler);
        handler.Enqueue(HttpStatusCode.OK, index ?? NuGetFeedClientTests.Index());
        return (new NuGetTools(client), handler, client);
    }

    [TestMethod]
    public async Task Search_BuildsQueryAndMapsFields()
    {
        (NuGetTools tools, NuGetRecordingHandler handler, NuGetFeedClient client) = Create();
        using NuGetFeedClient _ = client;
        string longText = new('x', 400);
        handler.Enqueue(HttpStatusCode.OK, $$"""
            {"totalHits":2,"data":[
              {"id":"A.B","version":"1.2.0","description":"{{longText}}","totalDownloads":1234,"verified":true,"versions":[{},{}]},
              {"id":"Private.Pkg","version":"0.1.0-beta"}
            ]}
            """);

        string result = await tools.SearchPackages("internal", " json 한글 ", true, "DotnetTool", 5, 50, TestContext.CancellationToken);

        Assert.AreEqual("https://pkgs.example.com/query?q=json%20%ED%95%9C%EA%B8%80&skip=5&take=50&prerelease=true&semVerLevel=2.0.0&packageType=DotnetTool",
            handler.Last.Uri.AbsoluteUri);
        JsonNode json = JsonNode.Parse(result)!;
        Assert.AreEqual(2L, (long)json["totalHits"]!);
        JsonNode first = json["packages"]![0]!;
        Assert.AreEqual("A.B", (string?)first["id"]);
        Assert.HasCount(300, (string)first["description"]!);
        Assert.AreEqual(1234L, (long)first["totalDownloads"]!);
        Assert.IsTrue((bool)first["verified"]!);
        Assert.AreEqual(2, (int)first["versionCount"]!);
        JsonNode second = json["packages"]![1]!;   // 사설 피드: 선택 필드 없음
        Assert.IsNull(second["description"]);
        Assert.AreEqual(0L, (long)second["totalDownloads"]!);
        Assert.IsNull(second["verified"]);
        Assert.AreEqual(0, (int)second["versionCount"]!);
    }

    [TestMethod]
    public async Task Search_DefaultsAndExistingQuery()
    {
        (NuGetTools tools, NuGetRecordingHandler handler, NuGetFeedClient client) =
            Create(NuGetFeedClientTests.Index(("SearchQueryService/3.0.0-rc", "https://pkgs.example.com/q?feed=main")));
        using NuGetFeedClient _ = client;
        handler.Enqueue(HttpStatusCode.OK, """{"data":[]}""");

        await tools.SearchPackages("internal", cancellationToken: TestContext.CancellationToken);

        Assert.AreEqual("https://pkgs.example.com/q?feed=main&q=&skip=0&take=20&prerelease=false&semVerLevel=2.0.0", handler.Last.Uri.AbsoluteUri);
    }

    [TestMethod]
    [DataRow(-1, 20)]
    [DataRow(0, 0)]
    [DataRow(0, 101)]
    public async Task Search_RangeError_NoRequest(int skip, int take)
    {
        (NuGetTools tools, NuGetRecordingHandler handler, NuGetFeedClient client) = Create();
        using NuGetFeedClient _ = client;

        await Assert.ThrowsExactlyAsync<McpException>(() => tools.SearchPackages("internal", skip: skip, take: take, cancellationToken: TestContext.CancellationToken));

        Assert.HasCount(0, handler.Requests);
    }

    [TestMethod]
    [DataRow("""[1]""", "data array is missing")]
    [DataRow("""{"data":["str"]}""", "id is missing or not a string")]
    [DataRow("""{"data":[{"id":"a"}]}""", "version is missing or not a string")]
    public async Task Search_UnexpectedJson(string body, string detail)
    {
        (NuGetTools tools, NuGetRecordingHandler handler, NuGetFeedClient client) = Create();
        using NuGetFeedClient _ = client;
        handler.Enqueue(HttpStatusCode.OK, body);

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() => tools.SearchPackages("internal", cancellationToken: TestContext.CancellationToken));

        Assert.AreEqual($"NuGet 'internal' https://pkgs.example.com/query returned unexpected JSON: {detail}", ex.Message);
    }

    [TestMethod]
    public async Task Search_404_IsGeneralFailure()
    {
        (NuGetTools tools, NuGetRecordingHandler handler, NuGetFeedClient client) = Create();
        using NuGetFeedClient _ = client;
        handler.Enqueue(HttpStatusCode.NotFound, "nope");

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() => tools.SearchPackages("internal", cancellationToken: TestContext.CancellationToken));

        Assert.AreEqual("NuGet 'internal' GET https://pkgs.example.com/query failed (HTTP 404): nope", ex.Message);
    }

    [TestMethod]
    public async Task ListVersions_SortsFiltersAndKeepsUnparsableLast()
    {
        (NuGetTools tools, NuGetRecordingHandler handler, NuGetFeedClient client) =
            Create(NuGetFeedClientTests.Index(("PackageBaseAddress/3.0.0", "https://pkgs.example.com/flat")));
        using NuGetFeedClient _ = client;
        handler.Enqueue(HttpStatusCode.OK, """{"versions":["2.0.0","1.10.0","weird","1.2.0","2.0.0-beta.1"]}""");

        string all = await tools.ListVersions("internal", " My.Package ", cancellationToken: TestContext.CancellationToken);
        handler.Enqueue(HttpStatusCode.OK, """{"versions":["2.0.0","2.0.0-beta.1","1.0.0"]}""");
        string stable = await tools.ListVersions("internal", "My.Package", false, TestContext.CancellationToken);

        Assert.AreEqual("https://pkgs.example.com/flat/my.package/index.json", handler.Requests[1].Uri.AbsoluteUri);
        Assert.AreEqual("""{"id":"My.Package","versions":["1.2.0","1.10.0","2.0.0-beta.1","2.0.0","weird"]}""", all);
        Assert.AreEqual("""{"id":"My.Package","versions":["1.0.0","2.0.0"]}""", stable);
    }

    [TestMethod]
    public async Task ListVersions_NotFound()
    {
        (NuGetTools tools, NuGetRecordingHandler handler, NuGetFeedClient client) = Create();
        using NuGetFeedClient _ = client;
        handler.Enqueue(HttpStatusCode.NotFound, "");

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() => tools.ListVersions("internal", "A", cancellationToken: TestContext.CancellationToken));

        Assert.AreEqual("NuGet 'internal' package 'A' was not found.", ex.Message);
    }

    [TestMethod]
    [DataRow("""{"x":1}""", "versions array is missing")]
    [DataRow("""{"versions":[1]}""", "versions must contain strings")]
    public async Task ListVersions_UnexpectedJson(string body, string detail)
    {
        (NuGetTools tools, NuGetRecordingHandler handler, NuGetFeedClient client) = Create();
        using NuGetFeedClient _ = client;
        handler.Enqueue(HttpStatusCode.OK, body);

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() => tools.ListVersions("internal", "a", cancellationToken: TestContext.CancellationToken));

        Assert.AreEqual($"NuGet 'internal' https://pkgs.example.com/flat/a/index.json returned unexpected JSON: {detail}", ex.Message);
    }

    [TestMethod]
    [DataRow(null, "id is required.")]
    [DataRow("  ", "id is required.")]
    [DataRow("..", "id '..' is not a valid NuGet package id.")]
    [DataRow(".", "id '.' is not a valid NuGet package id.")]
    [DataRow("a/b", "id 'a/b' is not a valid NuGet package id.")]
    [DataRow("a..b", "id 'a..b' is not a valid NuGet package id.")]
    public async Task ListVersions_InvalidId_NoRequest(string? id, string message)
    {
        (NuGetTools tools, NuGetRecordingHandler handler, NuGetFeedClient client) = Create();
        using NuGetFeedClient _ = client;

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() => tools.ListVersions("internal", id, cancellationToken: TestContext.CancellationToken));

        Assert.AreEqual(message, ex.Message);
        Assert.HasCount(0, handler.Requests);
    }

    [TestMethod]
    public void RequireId_LengthLimit()
    {
        Assert.AreEqual(new string('a', 100), NuGetTools.RequireId(new string('a', 100)));
        Assert.ThrowsExactly<McpException>(() => NuGetTools.RequireId(new string('a', 101)));
    }

    [TestMethod]
    public void RequireVersion_Normalizes()
    {
        Assert.AreEqual("1.0.0", NuGetTools.RequireVersion("1.0"));
        Assert.AreEqual("1.0.0-Beta", NuGetTools.RequireVersion(" 1.0.0-Beta+sha.1 "));
        Assert.AreEqual("version 'x.y' is not a valid NuGet version.", Assert.ThrowsExactly<McpException>(() => NuGetTools.RequireVersion("x.y")).Message);
        Assert.AreEqual("version is required.", Assert.ThrowsExactly<McpException>(() => NuGetTools.RequireVersion(null)).Message);
    }
}
