using System.Net;
using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.NuGet;
using Biz.Bizadm.MCP.ToolKit.Tools.NuGet;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tests.NuGet;

[TestClass]
public sealed class NuGetToolsMetadataTests
{
    private const string RegIndex = "https://pkgs.example.com/reg/a.b/index.json";

    public TestContext TestContext { get; set; } = null!;

    // nuget.org 형태: VulnerabilityInfo 리소스 있음
    private static string OrgIndex() => NuGetFeedClientTests.Index(
        ("RegistrationsBaseUrl/3.6.0", "https://pkgs.example.com/reg/"),
        ("VulnerabilityInfo/6.7.0", "https://pkgs.example.com/vuln/index.json"));

    private static string PrivateIndex() => NuGetFeedClientTests.Index(("RegistrationsBaseUrl/3.6.0", "https://pkgs.example.com/reg/"));

    private static (NuGetTools, NuGetRecordingHandler, NuGetFeedClient) Create(string index) => NuGetToolsReadTests.Create(index);

    private static string Registration(params string[] catalogEntries)
        => $$"""{"items":[{"@id":"https://pkgs.example.com/reg/a.b/page/1.json","items":[{{string.Join(",", catalogEntries.Select(e => $$"""{"catalogEntry":{{e}}}"""))}}]}]}""";

    private const string Plain = """{"version":"1.0.0","published":"2024-01-02T03:04:05+00:00"}""";
    private const string Unlisted = """{"version":"1.1.0","listed":false,"published":"1900-01-01T00:00:00+00:00"}""";
    private const string Rich = """
        {"version":"2.0.0+build.5","published":"2025-05-05T00:00:00+00:00","projectUrl":"https://proj.example",
         "dependencyGroups":[{"targetFramework":"net8.0","dependencies":[{"id":"Dep.A","range":"[1.0.0, )"},{"id":"Dep.B"}]},{}],
         "deprecation":{"reasons":["Legacy","CriticalBugs"],"message":"use C","alternatePackage":{"id":"C","range":"[3.0.0, )"}},
         "vulnerabilities":[{"advisoryUrl":"https://adv/1","severity":"2"},{"advisoryUrl":"https://adv/2","severity":"9"},{"advisoryUrl":"https://adv/3"}]}
        """;

    [TestMethod]
    public async Task Metadata_Summary_OrgFeed_MissingFieldsAreFalse()
    {
        (NuGetTools tools, NuGetRecordingHandler handler, NuGetFeedClient client) = Create(OrgIndex());
        using NuGetFeedClient _ = client;
        handler.Enqueue(HttpStatusCode.OK, Registration(Plain, Unlisted, Rich));

        string result = await tools.GetPackageMetadata("internal", "A.B", null, TestContext.CancellationToken);

        Assert.AreEqual(RegIndex, handler.Last.Uri.AbsoluteUri);
        JsonArray versions = JsonNode.Parse(result)!["versions"]!.AsArray();
        Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse("""{"version":"1.0.0","listed":true,"published":"2024-01-02T03:04:05+00:00","deprecated":false,"vulnerable":false}"""), versions[0]));
        Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse("""{"version":"1.1.0","listed":false,"published":null,"deprecated":false,"vulnerable":false}"""), versions[1]));
        Assert.IsTrue((bool)versions[2]!["deprecated"]!);
        Assert.IsTrue((bool)versions[2]!["vulnerable"]!);
    }

    [TestMethod]
    public async Task Metadata_Summary_PrivateFeed_MissingFieldsAreNull()
    {
        (NuGetTools tools, NuGetRecordingHandler handler, NuGetFeedClient client) = Create(PrivateIndex());
        using NuGetFeedClient _ = client;
        handler.Enqueue(HttpStatusCode.OK, Registration(Plain, Rich));

        JsonArray versions = JsonNode.Parse(await tools.GetPackageMetadata("internal", "A.B", null, TestContext.CancellationToken))!["versions"]!.AsArray();

        Assert.IsNull(versions[0]!["deprecated"]);
        Assert.IsNull(versions[0]!["vulnerable"]);
        Assert.IsTrue((bool)versions[1]!["deprecated"]!);   // 필드가 있으면 그 값
        Assert.IsTrue((bool)versions[1]!["vulnerable"]!);
    }

    [TestMethod]
    public async Task Metadata_Detail_MapsFieldsByNormalizedVersion()
    {
        (NuGetTools tools, NuGetRecordingHandler handler, NuGetFeedClient client) = Create(OrgIndex());
        using NuGetFeedClient _ = client;
        handler.Enqueue(HttpStatusCode.OK, Registration(Plain, Rich));

        JsonNode d = JsonNode.Parse(await tools.GetPackageMetadata("internal", "A.B", "2.0", TestContext.CancellationToken))!;

        Assert.AreEqual("A.B", (string?)d["id"]);
        Assert.AreEqual("2.0.0", (string?)d["version"]);
        Assert.IsTrue((bool)d["listed"]!);
        Assert.AreEqual("https://proj.example", (string?)d["projectUrl"]);
        Assert.AreEqual("""[{"targetFramework":"net8.0","dependencies":[{"id":"Dep.A","range":"[1.0.0, )"},{"id":"Dep.B","range":null}]},{"targetFramework":null,"dependencies":[]}]""",
            d["dependencyGroups"]!.ToJsonString());
        Assert.IsTrue((bool)d["deprecated"]!);
        Assert.AreEqual("""{"reasons":["Legacy","CriticalBugs"],"message":"use C","alternatePackage":{"id":"C","range":"[3.0.0, )"}}""", d["deprecation"]!.ToJsonString());
        Assert.IsTrue((bool)d["vulnerable"]!);
        Assert.AreEqual("""[{"severity":"high","advisoryUrl":"https://adv/1"},{"severity":"9","advisoryUrl":"https://adv/2"},{"severity":null,"advisoryUrl":"https://adv/3"}]""",
            d["vulnerabilities"]!.ToJsonString());
    }

    [TestMethod]
    public async Task Metadata_Detail_PrivateFeedWithoutFields_Null()
    {
        (NuGetTools tools, NuGetRecordingHandler handler, NuGetFeedClient client) = Create(PrivateIndex());
        using NuGetFeedClient _ = client;
        handler.Enqueue(HttpStatusCode.OK, Registration(Plain));

        JsonNode d = JsonNode.Parse(await tools.GetPackageMetadata("internal", "A.B", "1.0.0", TestContext.CancellationToken))!;

        Assert.IsNull(d["deprecated"]);
        Assert.IsNull(d["deprecation"]);
        Assert.IsNull(d["vulnerable"]);
        Assert.IsNull(d["vulnerabilities"]);
        Assert.AreEqual("[]", d["dependencyGroups"]!.ToJsonString());
    }

    [TestMethod]
    public async Task Metadata_ExternalPage_IsFetched()
    {
        (NuGetTools tools, NuGetRecordingHandler handler, NuGetFeedClient client) = Create(OrgIndex());
        using NuGetFeedClient _ = client;
        handler.Enqueue(HttpStatusCode.OK, """{"items":[{"@id":"https://cdn.example.com/reg/a.b/page/0.json","lower":"1.0.0","upper":"1.0.0"}]}""");
        handler.Enqueue(HttpStatusCode.OK, $$"""{"items":[{"catalogEntry":{{Plain}}}]}""");

        JsonNode d = JsonNode.Parse(await tools.GetPackageMetadata("internal", "A.B", null, TestContext.CancellationToken))!;

        Assert.AreEqual("https://cdn.example.com/reg/a.b/page/0.json", handler.Last.Uri.AbsoluteUri);
        Assert.IsFalse(handler.Last.Headers.ContainsKey("Authorization"));   // 다른 origin
        Assert.AreEqual(1, d["versions"]!.AsArray().Count);
    }

    [TestMethod]
    [DataRow("""{"x":1}""", RegIndex, "items array is missing")]
    [DataRow("""{"items":["s"]}""", RegIndex, "registration page must be an object")]
    [DataRow("""{"items":[{"lower":"1.0.0"}]}""", RegIndex, "registration page @id is missing or not an absolute http(s) URI")]
    [DataRow("""{"items":[{"@id":"file:///x"}]}""", RegIndex, "registration page @id is missing or not an absolute http(s) URI")]
    [DataRow("""{"items":[{"items":[{"x":1}]}]}""", RegIndex, "catalogEntry is missing")]
    [DataRow("""{"items":[{"items":[{"catalogEntry":{"id":"a"}}]}]}""", RegIndex, "version is missing or not a string")]
    public async Task Metadata_UnexpectedJson(string body, string url, string detail)
    {
        (NuGetTools tools, NuGetRecordingHandler handler, NuGetFeedClient client) = Create(OrgIndex());
        using NuGetFeedClient _ = client;
        handler.Enqueue(HttpStatusCode.OK, body);

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() => tools.GetPackageMetadata("internal", "A.B", null, TestContext.CancellationToken));

        Assert.AreEqual($"NuGet 'internal' {url} returned unexpected JSON: {detail}", ex.Message);
    }

    [TestMethod]
    public async Task Metadata_NotFound_PackageAndVersion()
    {
        (NuGetTools tools, NuGetRecordingHandler handler, NuGetFeedClient client) = Create(OrgIndex());
        using NuGetFeedClient _ = client;
        handler.Enqueue(HttpStatusCode.NotFound, "");
        handler.Enqueue(HttpStatusCode.OK, Registration(Plain));

        McpException package = await Assert.ThrowsExactlyAsync<McpException>(() => tools.GetPackageMetadata("internal", "A.B", null, TestContext.CancellationToken));
        McpException version = await Assert.ThrowsExactlyAsync<McpException>(() => tools.GetPackageMetadata("internal", "A.B", "9.9", TestContext.CancellationToken));

        Assert.AreEqual("NuGet 'internal' package 'A.B' was not found.", package.Message);
        Assert.AreEqual("NuGet 'internal' package 'A.B' version '9.9.0' was not found.", version.Message);
    }

    [TestMethod]
    public async Task Metadata_InvalidArguments_NoRequest()
    {
        (NuGetTools tools, NuGetRecordingHandler handler, NuGetFeedClient client) = Create(OrgIndex());
        using NuGetFeedClient _ = client;

        await Assert.ThrowsExactlyAsync<McpException>(() => tools.GetPackageMetadata("internal", null, null, TestContext.CancellationToken));
        await Assert.ThrowsExactlyAsync<McpException>(() => tools.GetPackageMetadata("internal", "A.B", "bad version", TestContext.CancellationToken));
        await Assert.ThrowsExactlyAsync<McpException>(() => tools.CheckVulnerabilities("internal", "A.B", null, TestContext.CancellationToken));
        await Assert.ThrowsExactlyAsync<McpException>(() => tools.CheckVulnerabilities("internal", "A.B", "  ", TestContext.CancellationToken));

        Assert.HasCount(0, handler.Requests);
    }

    [TestMethod]
    public async Task Check_Vulnerable()
    {
        (NuGetTools tools, NuGetRecordingHandler handler, NuGetFeedClient client) = Create(OrgIndex());
        using NuGetFeedClient _ = client;
        handler.Enqueue(HttpStatusCode.OK, Registration(Rich));

        string result = await tools.CheckVulnerabilities("internal", "A.B", "2.0.0", TestContext.CancellationToken);

        Assert.AreEqual("""{"id":"A.B","version":"2.0.0","vulnerable":true,"vulnerabilities":[{"severity":"high","advisoryUrl":"https://adv/1"},"""
            + """{"severity":"9","advisoryUrl":"https://adv/2"},{"severity":null,"advisoryUrl":"https://adv/3"}],"deprecated":true}""", result);
    }

    [TestMethod]
    public async Task Check_OrgFeedSafe_False_PrivateFeed_Null()
    {
        (NuGetTools org, NuGetRecordingHandler h1, NuGetFeedClient c1) = Create(OrgIndex());
        (NuGetTools priv, NuGetRecordingHandler h2, NuGetFeedClient c2) = Create(PrivateIndex());
        using NuGetFeedClient _1 = c1;
        using NuGetFeedClient _2 = c2;
        h1.Enqueue(HttpStatusCode.OK, Registration(Plain));
        h2.Enqueue(HttpStatusCode.OK, Registration(Plain));

        Assert.AreEqual("""{"id":"A.B","version":"1.0.0","vulnerable":false,"vulnerabilities":[],"deprecated":false}""",
            await org.CheckVulnerabilities("internal", "A.B", "1.0.0", TestContext.CancellationToken));
        Assert.AreEqual("""{"id":"A.B","version":"1.0.0","vulnerable":null,"vulnerabilities":null,"deprecated":null}""",
            await priv.CheckVulnerabilities("internal", "A.B", "1.0.0", TestContext.CancellationToken));
    }
}
