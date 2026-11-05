using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.Tools.Redmine.V7;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tests.Redmine.V7;

[TestClass]
public sealed class TimeEntryJournalV7Tests
{
    [TestMethod]
    public async Task ListTimeEntries_SendsUserId()
    {
        using V7RecordingHandler handler = new();
        TimeEntryToolsV7 tools = new(handler.CreateClient());

        await tools.ListTimeEntries(projectId: "demo", userId: "me", from: "2026-01-01", cancellationToken: CancellationToken.None);

        string query = handler.Last.Uri.Query;
        StringAssert.Contains(query, "project_id=demo", StringComparison.Ordinal);
        StringAssert.Contains(query, "user_id=me", StringComparison.Ordinal);
        StringAssert.Contains(query, "from=2026-01-01", StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task CreateTimeEntry_WritesUserId()
    {
        using V7RecordingHandler handler = new();
        TimeEntryToolsV7 tools = new(handler.CreateClient());

        await tools.CreateTimeEntry(1.5, issueId: 3, userId: 9, cancellationToken: CancellationToken.None);

        JsonObject entry = JsonNode.Parse(handler.Last.Body!)!["time_entry"]!.AsObject();
        Assert.AreEqual(1.5, entry["hours"]!.GetValue<double>());
        Assert.AreEqual(3, entry["issue_id"]!.GetValue<int>());
        Assert.AreEqual(9, entry["user_id"]!.GetValue<int>());
    }

    [TestMethod]
    public async Task DeleteTimeEntry_UsesDeleteRoute()
    {
        using V7RecordingHandler handler = new();
        TimeEntryToolsV7 tools = new(handler.CreateClient());

        await tools.DeleteTimeEntry(4, CancellationToken.None);

        Assert.AreEqual(HttpMethod.Delete, handler.Last.Method);
        Assert.AreEqual("https://redmine.example/time_entries/4.json", handler.Last.Uri.ToString());
        await Assert.ThrowsExactlyAsync<McpException>(() => tools.DeleteTimeEntry(0, CancellationToken.None));
    }

    [TestMethod]
    public async Task UpdateJournal_SendsNotesAndPrivateFlag()
    {
        using V7RecordingHandler handler = new();
        JournalTools tools = new(handler.CreateClient());

        await tools.UpdateJournal(12, "고친 댓글", privateNotes: false, cancellationToken: CancellationToken.None);

        Assert.AreEqual(HttpMethod.Put, handler.Last.Method);
        Assert.AreEqual("https://redmine.example/journals/12.json", handler.Last.Uri.ToString());
        JsonObject journal = JsonNode.Parse(handler.Last.Body!)!["journal"]!.AsObject();
        Assert.AreEqual("고친 댓글", journal["notes"]!.GetValue<string>());
        Assert.IsFalse(journal["private_notes"]!.GetValue<bool>());
    }

    [TestMethod]
    public async Task UpdateJournal_EmptyNotesAreSent()
    {
        using V7RecordingHandler handler = new();
        JournalTools tools = new(handler.CreateClient());

        await tools.UpdateJournal(12, "", cancellationToken: CancellationToken.None);

        Assert.AreEqual("""{"journal":{"notes":""}}""", handler.Last.Body);
    }

    [TestMethod]
    public async Task UpdateJournal_InvalidInput_ThrowsWithoutRequest()
    {
        using V7RecordingHandler handler = new();
        JournalTools tools = new(handler.CreateClient());

        await Assert.ThrowsExactlyAsync<McpException>(() => tools.UpdateJournal(12, cancellationToken: CancellationToken.None));
        await Assert.ThrowsExactlyAsync<McpException>(() => tools.UpdateJournal(0, "x", cancellationToken: CancellationToken.None));
        Assert.IsEmpty(handler.Requests);
    }
}
