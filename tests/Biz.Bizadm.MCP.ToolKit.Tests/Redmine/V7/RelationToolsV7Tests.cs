using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.Tools.Redmine.V7;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tests.Redmine.V7;

[TestClass]
public sealed class RelationToolsV7Tests
{
    [TestMethod]
    [DataRow("copied_to")]
    [DataRow("copied_from")]
    public async Task CreateIssueRelation_AllowsCopyTypes(string type)
    {
        using V7RecordingHandler handler = new();
        RelationToolsV7 tools = new(handler.CreateClient());

        await tools.CreateIssueRelation(1, 2, type, cancellationToken: CancellationToken.None);

        Assert.AreEqual("https://redmine.example/issues/1/relations.json", handler.Last.Uri.ToString());
        JsonObject relation = JsonNode.Parse(handler.Last.Body!)!["relation"]!.AsObject();
        Assert.AreEqual(type, relation["relation_type"]!.GetValue<string>());
        Assert.AreEqual(2, relation["issue_to_id"]!.GetValue<int>());
    }

    [TestMethod]
    public async Task CreateIssueRelation_InvalidInput_ThrowsWithoutRequest()
    {
        using V7RecordingHandler handler = new();
        RelationToolsV7 tools = new(handler.CreateClient());

        await Assert.ThrowsExactlyAsync<McpException>(() => tools.CreateIssueRelation(1, 2, "unknown", cancellationToken: CancellationToken.None));
        await Assert.ThrowsExactlyAsync<McpException>(() => tools.CreateIssueRelation(1, 2, "copied_to", delay: 1, cancellationToken: CancellationToken.None));
        await Assert.ThrowsExactlyAsync<McpException>(() => tools.CreateIssueRelation(1, 0, "relates", cancellationToken: CancellationToken.None));
        Assert.IsEmpty(handler.Requests);
    }

    [TestMethod]
    public async Task ListAndDelete_UseExpectedRoutes()
    {
        using V7RecordingHandler handler = new();
        RelationToolsV7 tools = new(handler.CreateClient());

        await tools.ListIssueRelations(3, CancellationToken.None);
        Assert.AreEqual("https://redmine.example/issues/3/relations.json", handler.Last.Uri.ToString());

        await tools.DeleteIssueRelation(9, CancellationToken.None);
        Assert.AreEqual(HttpMethod.Delete, handler.Last.Method);
        Assert.AreEqual("https://redmine.example/relations/9.json", handler.Last.Uri.ToString());
    }
}
