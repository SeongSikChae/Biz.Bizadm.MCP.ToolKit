using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.Redmine;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tools.Redmine.V7;

/// <summary>Redmine 7.0 첨부 정보 수정·삭제 Tool</summary>
[McpServerToolType]
public sealed class AttachmentManageTools
{
    private readonly RedmineClient _redmine;

    /// <summary>Redmine 클라이언트로 Tool을 만든다.</summary>
    /// <param name="redmine">Redmine REST API 클라이언트</param>
    public AttachmentManageTools(RedmineClient redmine)
    {
        _redmine = redmine;
    }

    /// <summary>Redmine 첨부의 파일명이나 설명을 바꾼다. 성공 시 본문이 비어 있다.</summary>
    [McpServerTool(Name = "update_attachment", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
    [Description("Redmine 첨부의 파일명이나 설명을 바꾼다. 파일 내용은 바꿀 수 없다. 성공 시 본문이 비어 있다. " + RedmineToolDescriptions.Mutating)]
    public Task<string> UpdateAttachment(
        [Description("첨부 ID")] int id,
        [Description("새 파일명")] string? filename = null,
        [Description("새 설명")] string? description = null,
        CancellationToken cancellationToken = default)
    {
        string path = $"attachments/{RedminePaths.Id(id, nameof(id))}.json";
        JsonObject attachment = new();
        RedmineJson.Set(attachment, "filename", filename);
        RedmineJson.Set(attachment, "description", description);
        if (attachment.Count == 0)
            throw new McpException("수정할 필드를 하나 이상 지정해야 합니다.");
        return _redmine.PatchAsync(path, "attachment", attachment, cancellationToken);
    }

    /// <summary>Redmine 첨부를 삭제한다. 성공 시 본문이 비어 있다.</summary>
    [McpServerTool(Name = "delete_attachment", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
    [Description("Redmine 첨부를 삭제한다. 되돌릴 수 없다. 성공 시 본문이 비어 있다. " + RedmineToolDescriptions.Mutating)]
    public Task<string> DeleteAttachment(
        [Description("첨부 ID")] int id,
        CancellationToken cancellationToken = default)
    {
        return _redmine.DeleteAsync($"attachments/{RedminePaths.Id(id, nameof(id))}.json", cancellationToken);
    }
}
