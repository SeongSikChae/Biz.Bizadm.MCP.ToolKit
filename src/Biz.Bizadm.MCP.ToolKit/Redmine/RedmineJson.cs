using System.Globalization;
using System.Text.Json.Nodes;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Redmine;

/// <summary>Redmine 요청 Body 작성 helper</summary>
internal static class RedmineJson
{
    public static void Set(JsonObject target, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            target[name] = value;
    }

    public static void Set(JsonObject target, string name, int? value)
    {
        if (value is not null)
            target[name] = value.Value;
    }

    public static void Set(JsonObject target, string name, double? value)
    {
        if (value is not null)
            target[name] = value.Value;
    }

    public static void SetUploads(JsonObject target, IReadOnlyList<UploadReference>? uploads, string name = "uploads")
    {
        if (uploads is null || uploads.Count == 0)
            return;

        JsonArray array = [];
        foreach (UploadReference upload in uploads)
        {
            if (upload is null)
                throw new McpException("uploads must not contain null items.");
            if (string.IsNullOrWhiteSpace(upload.Token))
                throw new McpException("uploads token is required.");
            if (string.IsNullOrWhiteSpace(upload.Filename))
                throw new McpException("uploads filename is required.");

            JsonObject item = new();
            Set(item, "token", upload.Token);
            Set(item, "filename", upload.Filename);
            Set(item, "content_type", upload.ContentType);
            Set(item, "description", upload.Description);
            array.Add(item);
        }

        target[name] = array;
    }

    public static void SetId(JsonObject target, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        string trimmed = value.Trim();
        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
            target[name] = number;
        else
            target[name] = trimmed;
    }
}
