using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.Redmine;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tools.Redmine.V7;

/// <summary>Redmine 7.0 일감 댓글(journal) 수정 Tool</summary>
[McpServerToolType]
public sealed class JournalTools
{
    private readonly RedmineClient _redmine;

    /// <summary>Redmine 클라이언트로 Tool을 만든다.</summary>
    /// <param name="redmine">Redmine REST API 클라이언트</param>
    public JournalTools(RedmineClient redmine)
    {
        _redmine = redmine;
    }

    /// <summary>Redmine 일감 댓글(journal)을 수정한다.</summary>
    [McpServerTool(Name = "update_journal", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
    [Description("Redmine 일감 댓글(journal)을 수정한다. 댓글 ID는 get_issue(include=journals)의 journals[].id이다. 본인 댓글은 edit_own_issue_notes, 남의 댓글은 edit_issue_notes 권한이 필요하다. 성공 시 본문이 비어 있다. " + RedmineToolDescriptions.Mutating)]
    public Task<string> UpdateJournal(
        [Description("댓글(journal) ID")] int id,
        [Description("새 댓글 본문. 빈 문자열도 그대로 보낸다.")] string? notes = null,
        [Description("비공개 댓글 여부")] bool? privateNotes = null,
        CancellationToken cancellationToken = default)
    {
        string path = $"journals/{RedminePaths.Id(id, nameof(id))}.json";
        JsonObject journal = new();
        if (notes is not null)
            journal["notes"] = notes;
        RedmineJson.Set(journal, "private_notes", privateNotes);
        if (journal.Count == 0)
            throw new McpException("수정할 필드를 하나 이상 지정해야 합니다.");

        return _redmine.PutAsync(path, "journal", journal, cancellationToken);
    }
}
