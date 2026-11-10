using System.Net;
using System.Text.Json.Nodes;
using System.Xml.Linq;

using Biz.Bizadm.MCP.ToolKit.Jenkins;
using Biz.Bizadm.MCP.ToolKit.Tests.GitHub;
using Biz.Bizadm.MCP.ToolKit.Tools.Jenkins;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tests.Jenkins;

[TestClass]
public sealed class JenkinsJobToolsTests
{
    private const string Base = "http://jenkins.example:8080/";

    public TestContext TestContext { get; set; } = null!;

    private static (JenkinsJobTools Tools, GitHubRecordingHandler Handler, JenkinsRestClient Client) Create()
    {
        GitHubRecordingHandler handler = new();
        JenkinsRestClient client = new(JenkinsRestClientTests.Options(), handler);
        return (new JenkinsJobTools(client), handler, client);
    }

    [TestMethod]
    public async Task GetJobConfig_ReturnsRawXml()
    {
        (JenkinsJobTools tools, GitHubRecordingHandler handler, JenkinsRestClient client) = Create();
        using JenkinsRestClient _ = client;
        handler.Enqueue(HttpStatusCode.OK, PipelineScriptTests.Config, "application/xml");

        string xml = await tools.GetJobConfig("prod", "team/app", TestContext.CancellationToken);

        Assert.AreEqual(PipelineScriptTests.Config, xml);
        Assert.AreEqual(HttpMethod.Get, handler.Last.Method);
        Assert.AreEqual(Base + "job/team/job/app/config.xml", handler.Last.Uri.AbsoluteUri);
    }

    [TestMethod]
    public async Task UpdateJobConfig_PostsXmlAsIs()
    {
        (JenkinsJobTools tools, GitHubRecordingHandler handler, JenkinsRestClient client) = Create();
        using JenkinsRestClient _ = client;
        handler.Enqueue(HttpStatusCode.OK, "");

        string result = await tools.UpdateJobConfig("prod", "app", "<project>한글</project>", TestContext.CancellationToken);

        Assert.AreEqual("""{"updated":"app"}""", result);
        Assert.AreEqual(HttpMethod.Post, handler.Last.Method);
        Assert.AreEqual(Base + "job/app/config.xml", handler.Last.Uri.AbsoluteUri);
        Assert.AreEqual("<project>한글</project>", handler.Last.Body);
        Assert.AreEqual("application/xml; charset=utf-8", handler.Last.Headers["Content-Type"]);
    }

    [TestMethod]
    public async Task UpdatePipelineScript_GetsReplacesAndPosts()
    {
        (JenkinsJobTools tools, GitHubRecordingHandler handler, JenkinsRestClient client) = Create();
        using JenkinsRestClient _ = client;
        handler.Enqueue(HttpStatusCode.OK, PipelineScriptTests.Config, "application/xml");
        handler.Enqueue(HttpStatusCode.OK, "");

        string result = await tools.UpdatePipelineScript("prod", "app", "node {\r\n  echo 'x'\r\n}", TestContext.CancellationToken);

        JsonNode json = JsonNode.Parse(result)!;
        Assert.AreEqual("app", (string?)json["updated"]);
        Assert.AreEqual("pipeline { agent any }".Length, (int)json["previousScriptLength"]!);
        Assert.AreEqual("node {\n  echo 'x'\n}".Length, (int)json["scriptLength"]!);
        Assert.HasCount(2, handler.Requests);
        Assert.AreEqual(HttpMethod.Get, handler.Requests[0].Method);
        Assert.AreEqual(HttpMethod.Post, handler.Requests[1].Method);
        Assert.AreEqual(Base + "job/app/config.xml", handler.Requests[1].Uri.AbsoluteUri);
        Assert.AreEqual("application/xml; charset=utf-8", handler.Requests[1].Headers["Content-Type"]);
        string posted = handler.Requests[1].Body!;
        Assert.StartsWith("<?xml version='1.1' encoding='UTF-8'?>", posted);
        XDocument doc = XDocument.Parse(posted[(posted.IndexOf("?>", StringComparison.Ordinal) + 2)..]);
        Assert.AreEqual("node {\r\n  echo 'x'\r\n}", doc.Root!.Element("definition")!.Element("script")!.Value);
    }

    [TestMethod]
    public async Task UpdatePipelineScript_ScmJob_DoesNotPost()
    {
        (JenkinsJobTools tools, GitHubRecordingHandler handler, JenkinsRestClient client) = Create();
        using JenkinsRestClient _ = client;
        handler.Enqueue(HttpStatusCode.OK, "<flow-definition><definition class=\"org.jenkinsci.plugins.workflow.cps.CpsScmFlowDefinition\"/></flow-definition>");

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() => tools.UpdatePipelineScript("prod", "app", "x", TestContext.CancellationToken));

        Assert.AreEqual("Jenkins 'prod' job 'app' is not an inline Pipeline script job (definition class: org.jenkinsci.plugins.workflow.cps.CpsScmFlowDefinition).", ex.Message);
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    public async Task UpdatePipelineScript_InvalidXmlOrScript_DoesNotPost()
    {
        (JenkinsJobTools tools, GitHubRecordingHandler handler, JenkinsRestClient client) = Create();
        using JenkinsRestClient _ = client;
        handler.Enqueue(HttpStatusCode.OK, "<flow-definition>");
        handler.Enqueue(HttpStatusCode.OK, PipelineScriptTests.Config);

        McpException invalidXml = await Assert.ThrowsExactlyAsync<McpException>(() => tools.UpdatePipelineScript("prod", "app", "x", TestContext.CancellationToken));
        McpException invalidScript = await Assert.ThrowsExactlyAsync<McpException>(() => tools.UpdatePipelineScript("prod", "app", "a\u0001b", TestContext.CancellationToken));

        Assert.StartsWith("Jenkins 'prod' job 'app' config.xml is not valid XML 1.0:", invalidXml.Message);
        Assert.AreEqual("Jenkins 'prod' job 'app' script contains characters not allowed in XML 1.0.", invalidScript.Message);
        Assert.IsTrue(handler.Requests.All(r => r.Method == HttpMethod.Get));
    }

    [TestMethod]
    [DataRow(HttpStatusCode.Found)]
    [DataRow(HttpStatusCode.OK)]
    public async Task DeleteJob_VerifiedBy404_Succeeds(HttpStatusCode deleteStatus)
    {
        (JenkinsJobTools tools, GitHubRecordingHandler handler, JenkinsRestClient client) = Create();
        using JenkinsRestClient _ = client;
        handler.Enqueue(deleteStatus, configure: r => r.Headers.Location = new Uri(Base + "job/team/"));
        handler.Enqueue(HttpStatusCode.NotFound);

        string result = await tools.DeleteJob("prod", "team/app", TestContext.CancellationToken);

        Assert.AreEqual("""{"deleted":"team/app"}""", result);
        Assert.AreEqual(HttpMethod.Post, handler.Requests[0].Method);
        Assert.AreEqual(Base + "job/team/job/app/doDelete", handler.Requests[0].Uri.AbsoluteUri);
        Assert.IsNull(handler.Requests[0].Body);
        Assert.AreEqual(HttpMethod.Get, handler.Requests[1].Method);
        Assert.AreEqual(Base + "job/team/job/app/config.xml", handler.Requests[1].Uri.AbsoluteUri);
    }

    [TestMethod]
    public async Task DeleteJob_LoginRedirectAndJobStillExists_Fails()
    {
        (JenkinsJobTools tools, GitHubRecordingHandler handler, JenkinsRestClient client) = Create();
        using JenkinsRestClient _ = client;
        handler.Enqueue(HttpStatusCode.Found, configure: r => r.Headers.Location = new Uri("/login", UriKind.Relative));
        handler.Enqueue(HttpStatusCode.OK, "<project/>");

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() => tools.DeleteJob("prod", "app", TestContext.CancellationToken));

        Assert.AreEqual($"Jenkins 'prod' POST job/app/doDelete returned HTTP 302 ({Base}login); job 'app' was not deleted or could not be verified (GET config.xml: HTTP 200).", ex.Message);
    }

    [TestMethod]
    public async Task DeleteJob_VerifyForbiddenOrTransportError_Fails()
    {
        (JenkinsJobTools tools, GitHubRecordingHandler handler, JenkinsRestClient client) = Create();
        using JenkinsRestClient _ = client;
        handler.Enqueue(HttpStatusCode.OK);
        handler.Enqueue(HttpStatusCode.Forbidden);
        handler.Enqueue(HttpStatusCode.OK);
        handler.EnqueueException(new HttpRequestException("reset"));

        McpException forbidden = await Assert.ThrowsExactlyAsync<McpException>(() => tools.DeleteJob("prod", "app", TestContext.CancellationToken));
        McpException transport = await Assert.ThrowsExactlyAsync<McpException>(() => tools.DeleteJob("prod", "app", TestContext.CancellationToken));

        Assert.AreEqual("Jenkins 'prod' POST job/app/doDelete returned HTTP 200; job 'app' was not deleted or could not be verified (GET config.xml: HTTP 403).", forbidden.Message);
        Assert.AreEqual("Jenkins 'prod' POST job/app/doDelete returned HTTP 200; job 'app' was not deleted or could not be verified (GET config.xml: Jenkins 'prod' GET job/app/config.xml failed: reset).", transport.Message);
    }

    [TestMethod]
    public async Task DeleteJob_ForbiddenDelete_FailsWithoutVerify()
    {
        (JenkinsJobTools tools, GitHubRecordingHandler handler, JenkinsRestClient client) = Create();
        using JenkinsRestClient _ = client;
        handler.Enqueue(HttpStatusCode.Forbidden, "builder is missing the Job/Delete permission", "text/plain");

        McpException ex = await Assert.ThrowsExactlyAsync<McpException>(() => tools.DeleteJob("prod", "app", TestContext.CancellationToken));

        Assert.AreEqual("Jenkins 'prod' POST job/app/doDelete failed (HTTP 403): builder is missing the Job/Delete permission", ex.Message);
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    public async Task InvalidInputs_SendNoRequest()
    {
        (JenkinsJobTools tools, GitHubRecordingHandler handler, JenkinsRestClient client) = Create();
        using JenkinsRestClient _ = client;
        CancellationToken ct = TestContext.CancellationToken;

        await Assert.ThrowsExactlyAsync<McpException>(() => tools.GetJobConfig(null, "app", ct));
        await Assert.ThrowsExactlyAsync<McpException>(() => tools.GetJobConfig("unknown", "app", ct));
        await Assert.ThrowsExactlyAsync<McpException>(() => tools.GetJobConfig("other", "app", ct));
        await Assert.ThrowsExactlyAsync<McpException>(() => tools.GetJobConfig("prod", " ", ct));
        await Assert.ThrowsExactlyAsync<McpException>(() => tools.GetJobConfig("prod", "a/../b", ct));
        await Assert.ThrowsExactlyAsync<McpException>(() => tools.UpdateJobConfig("prod", "app", "", ct));
        await Assert.ThrowsExactlyAsync<McpException>(() => tools.UpdateJobConfig("other", "app", "<x/>", ct));
        await Assert.ThrowsExactlyAsync<McpException>(() => tools.UpdatePipelineScript("prod", "app", null, ct));
        await Assert.ThrowsExactlyAsync<McpException>(() => tools.DeleteJob("prod", null, ct));

        Assert.IsEmpty(handler.Requests);
    }
}
