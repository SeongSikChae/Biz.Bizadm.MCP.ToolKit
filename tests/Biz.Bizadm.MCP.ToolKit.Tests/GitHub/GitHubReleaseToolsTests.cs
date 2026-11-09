using System.Net;
using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.GitHub;
using Biz.Bizadm.MCP.ToolKit.Tools.GitHub;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tests.GitHub;

[TestClass]
public sealed class GitHubReleaseToolsTests
{
    public TestContext TestContext { get; set; } = null!;

    private static (GitHubReleaseTools Tools, GitHubRecordingHandler Handler, GitHubRestClient Client) Create()
    {
        GitHubRecordingHandler handler = new();
        GitHubRestClient client = new(GitHubRestClientTests.Options(), handler);
        return (new GitHubReleaseTools(client), handler, client);
    }

    internal static string Release(long id, string tag, string uploadHost = "https://uploads.github.com")
        => "{\"id\":" + id + ",\"tag_name\":\"" + tag + "\",\"upload_url\":\"" + uploadHost + "/repos/o/r/releases/" + id + "/assets{?name,label}\"}";

    [TestMethod]
    public async Task CreateRelease_PostsOnlyGivenOptionalFields()
    {
        (GitHubReleaseTools tools, GitHubRecordingHandler handler, GitHubRestClient client) = Create();
        using GitHubRestClient _ = client;
        handler.Enqueue(HttpStatusCode.Created, Release(10, "v1.4.0"));
        handler.Enqueue(HttpStatusCode.Created, Release(11, "v1.4.1"));

        string result = await tools.CreateRelease("public", "o", "r", " v1.4.0 ", "main", "1.4.0", " notes\n", draft: true, cancellationToken: TestContext.CancellationToken);
        await tools.CreateRelease("public", "o", "r", "v1.4.1", generateReleaseNotes: true, cancellationToken: TestContext.CancellationToken);

        Assert.AreEqual(Release(10, "v1.4.0"), result);
        Assert.AreEqual(HttpMethod.Post, handler.Requests[0].Method);
        Assert.AreEqual(new Uri("https://api.github.com/repos/o/r/releases"), handler.Requests[0].Uri);
        Assert.AreEqual("""{"tag_name":"v1.4.0","target_commitish":"main","name":"1.4.0","body":" notes\n","draft":true,"prerelease":false,"generate_release_notes":false}""", handler.Requests[0].Body);
        Assert.AreEqual("""{"tag_name":"v1.4.1","draft":false,"prerelease":false,"generate_release_notes":true}""", handler.Requests[1].Body);
    }

    [TestMethod]
    public async Task UpdateRelease_ById_PatchesOnlyGivenFieldsWithoutLookup()
    {
        (GitHubReleaseTools tools, GitHubRecordingHandler handler, GitHubRestClient client) = Create();
        using GitHubRestClient _ = client;
        handler.Enqueue(HttpStatusCode.OK, Release(10, "v1"));

        await tools.UpdateRelease("public", "o", "r", releaseId: 10, name: "renamed", draft: false, cancellationToken: TestContext.CancellationToken);

        Assert.HasCount(1, handler.Requests);
        Assert.AreEqual(HttpMethod.Patch, handler.Last.Method);
        Assert.AreEqual(new Uri("https://api.github.com/repos/o/r/releases/10"), handler.Last.Uri);
        Assert.AreEqual("""{"name":"renamed","draft":false}""", handler.Last.Body);
    }

    [TestMethod]
    public async Task UpdateRelease_ByTag_LooksUpIdFirst()
    {
        (GitHubReleaseTools tools, GitHubRecordingHandler handler, GitHubRestClient client) = Create();
        using GitHubRestClient _ = client;
        handler.Enqueue(HttpStatusCode.OK, Release(42, "v 1"));
        handler.Enqueue(HttpStatusCode.OK, Release(42, "v 1"));

        await tools.UpdateRelease("enterprise", "o", "r", tag: "v 1", body: "", cancellationToken: TestContext.CancellationToken);

        Assert.AreEqual(HttpMethod.Get, handler.Requests[0].Method);
        Assert.AreEqual("https://ghe.example/api/v3/repos/o/r/releases/tags/v%201", handler.Requests[0].Uri.AbsoluteUri);
        Assert.AreEqual(new Uri("https://ghe.example/api/v3/repos/o/r/releases/42"), handler.Requests[1].Uri);
        Assert.AreEqual("""{"body":""}""", handler.Requests[1].Body);
    }

    [TestMethod]
    public async Task TagWithSlash_KeepsSlashAsSeparator()
    {
        (GitHubReleaseTools tools, GitHubRecordingHandler handler, GitHubRestClient client) = Create();
        using GitHubRestClient _ = client;
        handler.Enqueue(HttpStatusCode.OK, Release(8, "release/1 0"));
        handler.Enqueue(HttpStatusCode.NoContent);

        await tools.DeleteRelease("public", "o", "r", tag: "release/1 0", cancellationToken: TestContext.CancellationToken);

        Assert.AreEqual("https://api.github.com/repos/o/r/releases/tags/release/1%200", handler.Requests[0].Uri.AbsoluteUri);
        await Assert.ThrowsExactlyAsync<McpException>(() => tools.DeleteRelease("public", "o", "r", tag: "release/../x"));
        await Assert.ThrowsExactlyAsync<McpException>(() => tools.DeleteRelease("public", "o", "r", tag: "release//x"));
        Assert.HasCount(2, handler.Requests);
    }

    [TestMethod]
    public async Task DeleteRelease_AndAsset_ReturnDeletedIds()
    {
        (GitHubReleaseTools tools, GitHubRecordingHandler handler, GitHubRestClient client) = Create();
        using GitHubRestClient _ = client;
        handler.Enqueue(HttpStatusCode.OK, Release(7, "v1"));
        handler.Enqueue(HttpStatusCode.NoContent);
        handler.Enqueue(HttpStatusCode.NoContent);

        string release = await tools.DeleteRelease("public", "o", "r", tag: "v1", cancellationToken: TestContext.CancellationToken);
        string asset = await tools.DeleteReleaseAsset("public", "o", "r", 99, TestContext.CancellationToken);

        Assert.AreEqual("""{"deleted":{"releaseId":7}}""", release);
        Assert.AreEqual("""{"deleted":{"assetId":99}}""", asset);
        Assert.AreEqual(HttpMethod.Delete, handler.Requests[1].Method);
        Assert.AreEqual(new Uri("https://api.github.com/repos/o/r/releases/7"), handler.Requests[1].Uri);
        Assert.AreEqual(new Uri("https://api.github.com/repos/o/r/releases/assets/99"), handler.Requests[2].Uri);
    }

    [TestMethod]
    public async Task TagLookup_WithoutId_Fails()
    {
        (GitHubReleaseTools tools, GitHubRecordingHandler handler, GitHubRestClient client) = Create();
        using GitHubRestClient _ = client;
        handler.Enqueue(HttpStatusCode.OK, """{"tag_name":"v1"}""");

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() => tools.DeleteRelease("public", "o", "r", tag: "v1", cancellationToken: TestContext.CancellationToken));

        Assert.AreEqual("GitHub 'public' GET repos/o/r/releases/tags/v1 response has no release id.", ex.Message);
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    public async Task UploadAsset_ById_LooksUpUploadUrlAndPostsBytes()
    {
        (GitHubReleaseTools tools, GitHubRecordingHandler handler, GitHubRestClient client) = Create();
        using GitHubRestClient _ = client;
        handler.Enqueue(HttpStatusCode.OK, Release(5, "v1"));
        handler.Enqueue(HttpStatusCode.Created, """{"id":77,"name":"notes 1.txt"}""");

        string result = await tools.UploadReleaseAsset("public", "o", "r", releaseId: 5, name: "notes 1.txt",
            contentBase64: Convert.ToBase64String("hello"u8.ToArray()), contentType: "text/plain", label: "Notes", cancellationToken: TestContext.CancellationToken);

        Assert.AreEqual("""{"id":77,"name":"notes 1.txt"}""", result);
        Assert.AreEqual(new Uri("https://api.github.com/repos/o/r/releases/5"), handler.Requests[0].Uri);
        Assert.AreEqual(HttpMethod.Post, handler.Requests[1].Method);
        Assert.AreEqual("https://uploads.github.com/repos/o/r/releases/5/assets?name=notes%201.txt&label=Notes", handler.Requests[1].Uri.AbsoluteUri);
        Assert.AreEqual("hello", handler.Requests[1].Body);
        Assert.AreEqual("text/plain", handler.Requests[1].Headers["Content-Type"]);
        Assert.AreEqual("5", handler.Requests[1].Headers["Content-Length"]);
        Assert.StartsWith("Bearer ", handler.Requests[1].Headers["Authorization"]);
    }

    [TestMethod]
    public async Task UploadAsset_ByTag_UsesSingleLookup_AndDefaultContentType()
    {
        (GitHubReleaseTools tools, GitHubRecordingHandler handler, GitHubRestClient client) = Create();
        using GitHubRestClient _ = client;
        handler.Enqueue(HttpStatusCode.OK, Release(6, "v2", "https://ghe.example/api/uploads"));
        handler.Enqueue(HttpStatusCode.Created, """{"id":1}""");

        await tools.UploadReleaseAsset("enterprise", "o", "r", tag: "v2", name: "a.bin", contentBase64: "AAEC", cancellationToken: TestContext.CancellationToken);

        Assert.HasCount(2, handler.Requests);
        Assert.AreEqual(new Uri("https://ghe.example/api/v3/repos/o/r/releases/tags/v2"), handler.Requests[0].Uri);
        Assert.AreEqual("https://ghe.example/api/uploads/repos/o/r/releases/6/assets?name=a.bin", handler.Requests[1].Uri.AbsoluteUri);
        Assert.AreEqual("application/octet-stream", handler.Requests[1].Headers["Content-Type"]);
    }

    [TestMethod]
    public async Task UploadAsset_UploadHostMismatch_DoesNotUpload()
    {
        (GitHubReleaseTools tools, GitHubRecordingHandler handler, GitHubRestClient client) = Create();
        using GitHubRestClient _ = client;
        handler.Enqueue(HttpStatusCode.OK, Release(5, "v1", "https://evil.example"));

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() => tools.UploadReleaseAsset("public", "o", "r", releaseId: 5, name: "a", contentBase64: "AAEC", cancellationToken: TestContext.CancellationToken));

        Assert.AreEqual("GitHub 'public' upload_url host 'evil.example' does not match ApiEndpoint.", ex.Message);
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    public async Task UploadAsset_NonStringUploadUrl_ReportsUploadUrl()
    {
        (GitHubReleaseTools tools, GitHubRecordingHandler handler, GitHubRestClient client) = Create();
        using GitHubRestClient _ = client;
        handler.Enqueue(HttpStatusCode.OK, """{"id":5,"upload_url":123}""");

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() => tools.UploadReleaseAsset("public", "o", "r", releaseId: 5, name: "a", contentBase64: "AAEC", cancellationToken: TestContext.CancellationToken));

        Assert.AreEqual("GitHub 'public' release has no valid upload_url.", ex.Message);
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    public void DecodeAsset_EnforcesLimitAndFormat()
    {
        Assert.HasCount(GitHubReleaseTools.MaxAssetBytes, GitHubReleaseTools.DecodeAsset(Convert.ToBase64String(new byte[GitHubReleaseTools.MaxAssetBytes])));
        Assert.AreEqual("contentBase64 exceeds 10485760 bytes.",
            Assert.ThrowsExactly<McpException>(() => GitHubReleaseTools.DecodeAsset(Convert.ToBase64String(new byte[GitHubReleaseTools.MaxAssetBytes + 1]))).Message);
        Assert.AreEqual("contentBase64 is not valid base64.", Assert.ThrowsExactly<McpException>(() => GitHubReleaseTools.DecodeAsset("not base64!")).Message);
        Assert.AreEqual("contentBase64 is empty.", Assert.ThrowsExactly<McpException>(() => GitHubReleaseTools.DecodeAsset("  ")).Message);
        CollectionAssert.AreEqual(new byte[] { 0, 1, 2 }, GitHubReleaseTools.DecodeAsset("AA\r\nEC"));
    }

    [TestMethod]
    public async Task UploadAsset_InvalidInput_ThrowsWithoutRequest()
    {
        (GitHubReleaseTools tools, GitHubRecordingHandler handler, GitHubRestClient client) = Create();
        using GitHubRestClient _ = client;

        string[] messages =
        [
            (await Assert.ThrowsExactlyAsync<McpException>(() => tools.UploadReleaseAsset("public", "o", "r", name: "a", contentBase64: "AAEC"))).Message,
            (await Assert.ThrowsExactlyAsync<McpException>(() => tools.UploadReleaseAsset("public", "o", "r", releaseId: 1, contentBase64: "AAEC"))).Message,
            (await Assert.ThrowsExactlyAsync<McpException>(() => tools.UploadReleaseAsset("public", "o", "r", releaseId: 1, name: "a"))).Message,
            (await Assert.ThrowsExactlyAsync<McpException>(() => tools.UploadReleaseAsset("public", "o", "r", releaseId: 1, name: "a", contentBase64: "%%%"))).Message,
            (await Assert.ThrowsExactlyAsync<McpException>(() => tools.UploadReleaseAsset("public", "o", "r", releaseId: 1, name: "a", contentBase64: "AAEC", contentType: "not a type"))).Message,
        ];

        Assert.IsEmpty(handler.Requests);
        CollectionAssert.AreEqual(new[]
        {
            "exactly one of releaseId or tag is required.", "name is required.", "contentBase64 is required.",
            "contentBase64 is not valid base64.", "contentType is not a valid media type.",
        }, messages);
    }

    [TestMethod]
    public async Task InvalidInput_ThrowsWithoutRequest()
    {
        (GitHubReleaseTools tools, GitHubRecordingHandler handler, GitHubRestClient client) = Create();
        using GitHubRestClient _ = client;

        string[] messages =
        [
            (await Assert.ThrowsExactlyAsync<McpException>(() => tools.CreateRelease("public", "o", "r", null))).Message,
            (await Assert.ThrowsExactlyAsync<McpException>(() => tools.UpdateRelease("public", "o", "r", name: "x"))).Message,
            (await Assert.ThrowsExactlyAsync<McpException>(() => tools.UpdateRelease("public", "o", "r", releaseId: 1, tag: "v1", name: "x"))).Message,
            (await Assert.ThrowsExactlyAsync<McpException>(() => tools.UpdateRelease("public", "o", "r", releaseId: 0, name: "x"))).Message,
            (await Assert.ThrowsExactlyAsync<McpException>(() => tools.UpdateRelease("public", "o", "r", releaseId: 1))).Message,
            (await Assert.ThrowsExactlyAsync<McpException>(() => tools.DeleteRelease("public", "o", "r", tag: ".."))).Message,
            (await Assert.ThrowsExactlyAsync<McpException>(() => tools.DeleteReleaseAsset("public", "o", "r", -1))).Message,
            (await Assert.ThrowsExactlyAsync<McpException>(() => tools.DeleteReleaseAsset("public", "o", "r"))).Message,
            (await Assert.ThrowsExactlyAsync<McpException>(() => tools.DeleteRelease("qa", "o", "r", releaseId: 1))).Message,
        ];

        Assert.IsEmpty(handler.Requests);
        CollectionAssert.AreEqual(new[]
        {
            "tagName is required.",
            "exactly one of releaseId or tag is required.",
            "exactly one of releaseId or tag is required.",
            "releaseId must be a positive integer.",
            "at least one field to update is required.",
            "tag must not contain empty, '.' or '..' path segments.",
            "assetId must be a positive integer.",
            "assetId must be a positive integer.",
            "Unknown GitHub instance 'qa'. Registered instances: public, enterprise",
        }, messages);
    }
}
