using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.Redmine;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tools.Redmine;

/// <summary>Redmine 첨부 다운로드·업로드 Tool</summary>
[McpServerToolType]
public sealed class AttachmentTools
{
    private readonly RedmineClient _redmine;

    /// <summary>Redmine 클라이언트로 Tool을 만든다.</summary>
    /// <param name="redmine">Redmine REST API 클라이언트</param>
    public AttachmentTools(RedmineClient redmine)
    {
        _redmine = redmine;
    }

    /// <summary>Redmine 첨부파일 본문을 다운로드한다. 응답 JSON에 text 또는 contentBase64를 넣으며, 5MiB를 넘으면 outputPath가 필요하다. outputPath는 MCP 서버 호스트 경로이다.</summary>
    [McpServerTool(Name = "download_attachment", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("Redmine 첨부파일 본문을 다운로드한다. 응답 JSON에 text 또는 contentBase64를 넣으며, 5MiB를 넘으면 outputPath가 필요하다. outputPath는 MCP 서버 호스트 경로이다.")]
    public async Task<string> DownloadAttachment(
        [Description("첨부 ID. issueId+filename과 둘 중 하나가 필요하다. 둘 다 있으면 attachmentId를 우선한다.")] int? attachmentId = null,
        [Description("일감 ID. attachmentId가 없을 때 filename과 함께 첨부를 찾는다.")] int? issueId = null,
        [Description("첨부 파일명. issueId와 함께 사용한다. 대소문자를 구분한다.")] string? filename = null,
        [Description("본문 다운로드 URL. 있으면 메타 content_url 대신 사용한다. Redmine API endpoint와 같은 origin만 허용한다.")] string? contentUrl = null,
        [Description("MCP 서버 호스트에 저장할 경로. 5MiB 초과 시 필수이다.")] string? outputPath = null,
        CancellationToken cancellationToken = default)
    {
        int id = await ResolveAttachmentIdAsync(attachmentId, issueId, filename, cancellationToken).ConfigureAwait(false);

        string metaJson = await _redmine.GetAsync(
            $"attachments/{id.ToString(CultureInfo.InvariantCulture)}.json",
            cancellationToken).ConfigureAwait(false);
        JsonObject attachment = ParseAttachmentObject(metaJson);
        string resolvedFilename = attachment["filename"]?.GetValue<string>()
            ?? throw new McpException("Attachment metadata is missing filename.");
        string? contentType = attachment["content_type"]?.GetValue<string>();
        string downloadUrl = string.IsNullOrWhiteSpace(contentUrl)
            ? attachment["content_url"]?.GetValue<string>()
                ?? throw new McpException("Attachment metadata is missing content_url.")
            : contentUrl.Trim();

        byte[] bytes = await _redmine.GetBytesAsync(downloadUrl, cancellationToken).ConfigureAwait(false);
        bool exceedsInline = AttachmentDownload.ExceedsInlineLimit(bytes.Length);
        if (exceedsInline && string.IsNullOrWhiteSpace(outputPath))
        {
            throw new McpException(
                $"Attachment is larger than {AttachmentDownload.MaxInlineBytes} bytes. Provide outputPath to save on the MCP server host.");
        }

        string? savedPath = null;
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            savedPath = Path.GetFullPath(outputPath);
            string? directory = Path.GetDirectoryName(savedPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllBytesAsync(savedPath, bytes, cancellationToken).ConfigureAwait(false);
        }

        return AttachmentDownload.BuildResponseJson(
            id,
            resolvedFilename,
            contentType,
            bytes,
            includeBody: !exceedsInline,
            savedPath);
    }

    /// <summary>파일을 Redmine에 업로드하고 토큰을 반환한다. 토큰을 create_issue, update_issue, create_subtask, put_wiki_page의 uploads로 넘겨야 첨부가 된다. content, contentBase64, sourcePath 중 하나만 지정한다. content와 contentBase64는 5MiB까지이고, 더 크면 sourcePath를 쓴다. sourcePath는 MCP 서버 호스트 경로이다. 요청 제한 시간은 100초이다.</summary>
    [McpServerTool(Name = "upload_file", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = true)]
    [Description("파일을 Redmine에 업로드하고 토큰을 반환한다. 토큰을 create_issue, update_issue, create_subtask, put_wiki_page의 uploads로 넘겨야 첨부가 된다. content, contentBase64, sourcePath 중 하나만 지정한다. content와 contentBase64는 5MiB까지이고, 더 크면 sourcePath를 쓴다. sourcePath는 MCP 서버 호스트 경로이다. 요청 제한 시간은 100초이다. " + RedmineToolDescriptions.Mutating)]
    public async Task<string> UploadFile(
        [Description("UTF-8 텍스트 본문")] string? content = null,
        [Description("base64로 인코딩한 본문")] string? contentBase64 = null,
        [Description("MCP 서버 호스트의 파일 경로")] string? sourcePath = null,
        CancellationToken cancellationToken = default)
    {
        int provided = (content is not null ? 1 : 0)
            + (contentBase64 is not null ? 1 : 0)
            + (!string.IsNullOrWhiteSpace(sourcePath) ? 1 : 0);
        if (provided != 1)
        {
            throw new McpException("Provide exactly one of content, contentBase64, or sourcePath.");
        }

        if (!string.IsNullOrWhiteSpace(sourcePath))
        {
            string fullPath = Path.GetFullPath(sourcePath);
            await using FileStream file = File.OpenRead(fullPath);
            return await _redmine.PostOctetStreamAsync("uploads.json", file, cancellationToken).ConfigureAwait(false);
        }

        byte[] bytes = content is not null
            ? Encoding.UTF8.GetBytes(content)
            : DecodeBase64(contentBase64!);
        if (AttachmentDownload.ExceedsInlineLimit(bytes.Length))
        {
            throw new McpException(
                $"Inline content is larger than {AttachmentDownload.MaxInlineBytes} bytes. Use sourcePath.");
        }

        using MemoryStream stream = new(bytes, writable: false);
        return await _redmine.PostOctetStreamAsync("uploads.json", stream, cancellationToken).ConfigureAwait(false);
    }

    private static byte[] DecodeBase64(string contentBase64)
    {
        try
        {
            return Convert.FromBase64String(contentBase64);
        }
        catch (FormatException exception)
        {
            throw new McpException("contentBase64 is not valid base64.", exception);
        }
    }

    private async Task<int> ResolveAttachmentIdAsync(
        int? attachmentId,
        int? issueId,
        string? filename,
        CancellationToken cancellationToken)
    {
        if (attachmentId is not null)
        {
            if (attachmentId.Value < 1)
            {
                throw new McpException("attachmentId must be positive.");
            }

            return attachmentId.Value;
        }

        if (issueId is null || string.IsNullOrWhiteSpace(filename))
        {
            throw new McpException("Provide attachmentId, or both issueId and filename.");
        }

        if (issueId.Value < 1)
        {
            throw new McpException("issueId must be positive.");
        }

        string issueJson = await _redmine.GetAsync(
            $"issues/{issueId.Value.ToString(CultureInfo.InvariantCulture)}.json",
            cancellationToken,
            ("include", "attachments")).ConfigureAwait(false);
        JsonNode root = JsonNode.Parse(issueJson)
            ?? throw new McpException("Issue response was empty.");
        JsonArray? attachments = root["issue"]?["attachments"]?.AsArray();
        if (attachments is null || attachments.Count == 0)
        {
            throw new McpException($"Issue {issueId.Value} has no attachments matching '{filename}'.");
        }

        List<int> matches = [];
        foreach (JsonNode? node in attachments)
        {
            if (node is null)
            {
                continue;
            }

            string? name = node["filename"]?.GetValue<string>();
            if (string.Equals(name, filename, StringComparison.Ordinal)
                && node["id"] is JsonNode idNode)
            {
                matches.Add(idNode.GetValue<int>());
            }
        }

        if (matches.Count == 0)
        {
            throw new McpException($"Issue {issueId.Value} has no attachments matching '{filename}'.");
        }

        if (matches.Count > 1)
        {
            throw new McpException(
                $"Issue {issueId.Value} has multiple attachments named '{filename}'. Use attachmentId.");
        }

        return matches[0];
    }

    private static JsonObject ParseAttachmentObject(string metaJson)
    {
        JsonNode root = JsonNode.Parse(metaJson)
            ?? throw new McpException("Attachment response was empty.");
        JsonObject? attachment = root["attachment"]?.AsObject();
        if (attachment is null)
        {
            throw new McpException("Attachment response did not contain an attachment object.");
        }

        return attachment;
    }
}
