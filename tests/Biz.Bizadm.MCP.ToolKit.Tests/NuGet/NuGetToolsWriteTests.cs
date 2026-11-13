using System.Net;

using Biz.Bizadm.MCP.ToolKit.NuGet;
using Biz.Bizadm.MCP.ToolKit.Tools.NuGet;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tests.NuGet;

[TestClass]
public sealed class NuGetToolsWriteTests
{
    private const string Publish = "https://pkgs.example.com/api/v2/package";

    public TestContext TestContext { get; set; } = null!;

    private static (NuGetTools, NuGetRecordingHandler, NuGetFeedClient) Create() => NuGetToolsReadTests.Create();

    [TestMethod]
    public async Task Push_Base64_SendsMultipartWithApiKey()
    {
        (NuGetTools tools, NuGetRecordingHandler handler, NuGetFeedClient client) = Create();
        using NuGetFeedClient _ = client;
        handler.Enqueue(HttpStatusCode.Created, "");

        string result = await tools.PushPackage("internal", contentBase64: Convert.ToBase64String(NuGetPackageFileTests.Package()), cancellationToken: TestContext.CancellationToken);

        Assert.AreEqual("""{"pushed":{"id":"My.Pkg","version":"1.0.0"},"skipped":false}""", result);
        Assert.AreEqual(HttpMethod.Put, handler.Last.Method);
        Assert.AreEqual(Publish, handler.Last.Uri.AbsoluteUri);
        Assert.AreEqual(NuGetFeedClientTests.ApiKey, handler.Last.Headers["X-NuGet-ApiKey"]);
        StringAssert.StartsWith(handler.Last.Headers["Content-Type"], "multipart/form-data");
        StringAssert.Matches(handler.Last.Body!, new System.Text.RegularExpressions.Regex("name=\"?package\"?; filename=\"?package.nupkg\"?"));
    }

    [TestMethod]
    public async Task Push_Path_Accepted202()
    {
        (NuGetTools tools, NuGetRecordingHandler handler, NuGetFeedClient client) = Create();
        using NuGetFeedClient _ = client;
        handler.Enqueue(HttpStatusCode.Accepted, "");
        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".nupkg");
        await File.WriteAllBytesAsync(file, NuGetPackageFileTests.Package(), TestContext.CancellationToken);
        try
        {
            string result = await tools.PushPackage("internal", path: file, cancellationToken: TestContext.CancellationToken);
            Assert.AreEqual("""{"pushed":{"id":"My.Pkg","version":"1.0.0"},"skipped":false}""", result);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [TestMethod]
    public async Task Push_Conflict_FailsWithReason_OrSkips()
    {
        (NuGetTools tools, NuGetRecordingHandler handler, NuGetFeedClient client) = Create();
        using NuGetFeedClient _ = client;
        string content = Convert.ToBase64String(NuGetPackageFileTests.Package());
        handler.Enqueue(HttpStatusCode.Conflict, "", "text/plain", r => r.ReasonPhrase = "A package with ID 'My.Pkg' and version '1.0.0' already exists");
        handler.Enqueue(HttpStatusCode.Conflict, "");

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() => tools.PushPackage("internal", contentBase64: content, cancellationToken: TestContext.CancellationToken));
        string skipped = await tools.PushPackage("internal", contentBase64: content, skipDuplicate: true, cancellationToken: TestContext.CancellationToken);

        Assert.AreEqual("NuGet 'internal' package 'My.Pkg' 1.0.0 already exists (HTTP 409): A package with ID 'My.Pkg' and version '1.0.0' already exists. Use skipDuplicate to ignore.", ex.Message);
        Assert.AreEqual("""{"pushed":{"id":"My.Pkg","version":"1.0.0"},"skipped":true}""", skipped);
    }

    [TestMethod]
    public async Task Push_BadRequest_UsesReasonPhrase()
    {
        (NuGetTools tools, NuGetRecordingHandler handler, NuGetFeedClient client) = Create();
        using NuGetFeedClient _ = client;
        handler.Enqueue(HttpStatusCode.BadRequest, "", "text/plain", r => r.ReasonPhrase = "The package manifest is invalid");

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() =>
            tools.PushPackage("internal", contentBase64: Convert.ToBase64String(NuGetPackageFileTests.Package()), cancellationToken: TestContext.CancellationToken));

        Assert.AreEqual($"NuGet 'internal' PUT {Publish} failed (HTTP 400): The package manifest is invalid", ex.Message);
    }

    [TestMethod]
    [DataRow(null, null, "Specify exactly one of path or contentBase64.")]
    [DataRow(@"C:\a.nupkg", "AQID", "Specify exactly one of path or contentBase64.")]
    [DataRow(null, "!!", "contentBase64 is not valid Base64.")]
    [DataRow(@"\\host\share\a.nupkg", null, "path must be an absolute local file path; UNC and device paths are not allowed.")]
    [DataRow(null, "AQID", "package is not a valid .nupkg (zip): ")]
    public async Task Push_InvalidInput_NoRequest(string? path, string? content, string message)
    {
        (NuGetTools tools, NuGetRecordingHandler handler, NuGetFeedClient client) = Create();
        using NuGetFeedClient _ = client;

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() => tools.PushPackage("internal", path, content, cancellationToken: TestContext.CancellationToken));

        StringAssert.StartsWith(ex.Message, message);
        Assert.HasCount(0, handler.Requests);
    }

    private const string Symbols = "https://pkgs.example.com/api/v2/symbolpackage";

    private static (NuGetTools, NuGetRecordingHandler, NuGetFeedClient) CreateWithSymbols() => NuGetToolsReadTests.Create(NuGetFeedClientTests.Index(
        ("PackagePublish/2.0.0", Publish),
        ("SymbolPackagePublish/4.9.0", Symbols)));

    private static string SymbolsBase64() => Convert.ToBase64String(NuGetPackageFileTests.Package(NuGetPackageFileTests.SymbolsNuspec));

    [TestMethod]
    public async Task PushSymbol_Base64_SendsToSymbolResourceWithApiKey()
    {
        (NuGetTools tools, NuGetRecordingHandler handler, NuGetFeedClient client) = CreateWithSymbols();
        using NuGetFeedClient _ = client;
        handler.Enqueue(HttpStatusCode.Created, "");

        string result = await tools.PushSymbolPackage("internal", contentBase64: SymbolsBase64(), cancellationToken: TestContext.CancellationToken);

        Assert.AreEqual("""{"pushed":{"id":"My.Pkg","version":"1.0.0"},"skipped":false}""", result);
        Assert.AreEqual(HttpMethod.Put, handler.Last.Method);
        Assert.AreEqual(Symbols, handler.Last.Uri.AbsoluteUri);
        Assert.AreEqual(NuGetFeedClientTests.ApiKey, handler.Last.Headers["X-NuGet-ApiKey"]);
        StringAssert.StartsWith(handler.Last.Headers["Content-Type"], "multipart/form-data");
        StringAssert.Matches(handler.Last.Body!, new System.Text.RegularExpressions.Regex("name=\"?package\"?; filename=\"?package.snupkg\"?"));
    }

    [TestMethod]
    public async Task PushSymbol_Path_Accepted202()
    {
        (NuGetTools tools, NuGetRecordingHandler handler, NuGetFeedClient client) = CreateWithSymbols();
        using NuGetFeedClient _ = client;
        handler.Enqueue(HttpStatusCode.Accepted, "");
        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".snupkg");
        await File.WriteAllBytesAsync(file, NuGetPackageFileTests.Package(NuGetPackageFileTests.SymbolsNuspec), TestContext.CancellationToken);
        try
        {
            string result = await tools.PushSymbolPackage("internal", path: file, cancellationToken: TestContext.CancellationToken);
            Assert.AreEqual("""{"pushed":{"id":"My.Pkg","version":"1.0.0"},"skipped":false}""", result);
            Assert.AreEqual(Symbols, handler.Last.Uri.AbsoluteUri);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [TestMethod]
    public async Task PushSymbol_Conflict_FailsWithReason_OrSkips()
    {
        (NuGetTools tools, NuGetRecordingHandler handler, NuGetFeedClient client) = CreateWithSymbols();
        using NuGetFeedClient _ = client;
        handler.Enqueue(HttpStatusCode.Conflict, "", "text/plain", r => r.ReasonPhrase = "Symbol package is being validated");
        handler.Enqueue(HttpStatusCode.Conflict, "");

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() => tools.PushSymbolPackage("internal", contentBase64: SymbolsBase64(), cancellationToken: TestContext.CancellationToken));
        string skipped = await tools.PushSymbolPackage("internal", contentBase64: SymbolsBase64(), skipDuplicate: true, cancellationToken: TestContext.CancellationToken);

        Assert.AreEqual("NuGet 'internal' symbol package 'My.Pkg' 1.0.0 was pushed but is not available yet (HTTP 409): Symbol package is being validated. Use skipDuplicate to ignore.", ex.Message);
        Assert.AreEqual("""{"pushed":{"id":"My.Pkg","version":"1.0.0"},"skipped":true}""", skipped);
    }

    [TestMethod]
    public async Task PushSymbol_FeedWithoutSymbolResource_Fails()
    {
        (NuGetTools tools, NuGetRecordingHandler handler, NuGetFeedClient client) = Create();
        using NuGetFeedClient _ = client;

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() => tools.PushSymbolPackage("internal", contentBase64: SymbolsBase64(), cancellationToken: TestContext.CancellationToken));

        Assert.AreEqual("NuGet 'internal' service index has no symbol publish resource (SymbolPackagePublish/4.9.0).", ex.Message);
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    public async Task PushSymbol_InvalidInput_NoRequest()
    {
        (NuGetTools tools, NuGetRecordingHandler handler, NuGetFeedClient client) = CreateWithSymbols();
        using NuGetFeedClient _ = client;
        string root = OperatingSystem.IsWindows() ? @"C:\" : "/";

        Assert.AreEqual(".nuspec has no SymbolsPackage packageType; use nuget_push_package for .nupkg.", (await Assert.ThrowsExactlyAsync<McpException>(() =>
            tools.PushSymbolPackage("internal", contentBase64: Convert.ToBase64String(NuGetPackageFileTests.Package()), cancellationToken: TestContext.CancellationToken))).Message);
        Assert.AreEqual("path must be a .snupkg file.", (await Assert.ThrowsExactlyAsync<McpException>(() =>
            tools.PushSymbolPackage("internal", path: root + "a.nupkg", cancellationToken: TestContext.CancellationToken))).Message);
        Assert.AreEqual("Specify exactly one of path or contentBase64.", (await Assert.ThrowsExactlyAsync<McpException>(() =>
            tools.PushSymbolPackage("internal", cancellationToken: TestContext.CancellationToken))).Message);
        StringAssert.StartsWith((await Assert.ThrowsExactlyAsync<McpException>(() =>
            tools.PushSymbolPackage("internal", contentBase64: "AQID", cancellationToken: TestContext.CancellationToken))).Message, "package is not a valid .snupkg (zip): ");
        Assert.HasCount(0, handler.Requests);
    }

    [TestMethod]
    public async Task WriteTools_NoApiKey_NoRequest()
    {
        (NuGetTools tools, NuGetRecordingHandler handler, NuGetFeedClient client) = Create();
        using NuGetFeedClient _ = client;
        const string message = "NuGet 'org' has no ApiKey; write tools are unavailable.";

        Assert.AreEqual(message, (await Assert.ThrowsExactlyAsync<McpException>(() => tools.PushPackage("org", contentBase64: "AQID", cancellationToken: TestContext.CancellationToken))).Message);
        Assert.AreEqual(message, (await Assert.ThrowsExactlyAsync<McpException>(() => tools.PushSymbolPackage("org", contentBase64: "AQID", cancellationToken: TestContext.CancellationToken))).Message);
        Assert.AreEqual(message, (await Assert.ThrowsExactlyAsync<McpException>(() => tools.UnlistPackage("org", "A", "1.0.0", TestContext.CancellationToken))).Message);
        Assert.AreEqual(message, (await Assert.ThrowsExactlyAsync<McpException>(() => tools.RelistPackage("org", "A", "1.0.0", TestContext.CancellationToken))).Message);
        Assert.HasCount(0, handler.Requests);
    }

    [TestMethod]
    public async Task Unlist_Relist_UrlsAndResults()
    {
        (NuGetTools tools, NuGetRecordingHandler handler, NuGetFeedClient client) = Create();
        using NuGetFeedClient _ = client;
        handler.Enqueue(HttpStatusCode.NoContent, "");
        handler.Enqueue(HttpStatusCode.OK, "");

        string unlisted = await tools.UnlistPackage("internal", "My.Pkg", "1.0", TestContext.CancellationToken);
        Assert.AreEqual(HttpMethod.Delete, handler.Last.Method);
        Assert.AreEqual(Publish + "/My.Pkg/1.0.0", handler.Last.Uri.AbsoluteUri);
        Assert.AreEqual(NuGetFeedClientTests.ApiKey, handler.Last.Headers["X-NuGet-ApiKey"]);

        string relisted = await tools.RelistPackage("internal", "My.Pkg", "1.0.0", TestContext.CancellationToken);
        Assert.AreEqual(HttpMethod.Post, handler.Last.Method);
        Assert.AreEqual(Publish + "/My.Pkg/1.0.0", handler.Last.Uri.AbsoluteUri);

        Assert.AreEqual("""{"unlisted":{"id":"My.Pkg","version":"1.0.0"}}""", unlisted);
        Assert.AreEqual("""{"relisted":{"id":"My.Pkg","version":"1.0.0"}}""", relisted);
    }

    [TestMethod]
    [DataRow(HttpStatusCode.NotFound, "missing", "NuGet 'internal' DELETE " + Publish + "/A/1.0.0 failed (HTTP 404): missing")]
    [DataRow(HttpStatusCode.Conflict, "busy", "NuGet 'internal' DELETE " + Publish + "/A/1.0.0 failed (HTTP 409): busy")]
    [DataRow(HttpStatusCode.Found, "", "NuGet 'internal' DELETE " + Publish + "/A/1.0.0 failed (HTTP 302): redirected to (none)")]
    public async Task Unlist_Failures(HttpStatusCode status, string body, string message)
    {
        (NuGetTools tools, NuGetRecordingHandler handler, NuGetFeedClient client) = Create();
        using NuGetFeedClient _ = client;
        handler.Enqueue(status, body);

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() => tools.UnlistPackage("internal", "A", "1.0.0", TestContext.CancellationToken));

        Assert.AreEqual(message, ex.Message);
    }

    [TestMethod]
    public async Task Unlist_InvalidArguments_NoRequest()
    {
        (NuGetTools tools, NuGetRecordingHandler handler, NuGetFeedClient client) = Create();
        using NuGetFeedClient _ = client;

        await Assert.ThrowsExactlyAsync<McpException>(() => tools.UnlistPackage("internal", "..", "1.0.0", TestContext.CancellationToken));
        await Assert.ThrowsExactlyAsync<McpException>(() => tools.UnlistPackage("internal", "A", null, TestContext.CancellationToken));
        await Assert.ThrowsExactlyAsync<McpException>(() => tools.RelistPackage("internal", null, "1.0.0", TestContext.CancellationToken));

        Assert.HasCount(0, handler.Requests);
    }
}
