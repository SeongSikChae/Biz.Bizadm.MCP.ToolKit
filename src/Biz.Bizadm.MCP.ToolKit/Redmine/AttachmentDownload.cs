using System.Text;
using System.Text.Json.Nodes;

namespace Biz.Bizadm.MCP.ToolKit.Redmine;

/// <summary>첨부 다운로드 응답 작성 helper</summary>
internal static class AttachmentDownload
{
    public const int MaxInlineBytes = 5 * 1024 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly HashSet<string> TextContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/json",
        "application/xml",
        "application/javascript",
        "application/yaml",
        "application/x-yaml"
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md",
        ".txt",
        ".csv",
        ".json",
        ".xml",
        ".yaml",
        ".yml",
        ".log",
        ".textile"
    };

    public static bool ExceedsInlineLimit(int byteLength) => byteLength > MaxInlineBytes;

    public static bool IsTextLike(string? contentType, string? filename)
    {
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            string media = contentType.Split(';', 2)[0].Trim();
            if (media.StartsWith("text/", StringComparison.OrdinalIgnoreCase) || TextContentTypes.Contains(media))
                return true;
        }

        if (!string.IsNullOrWhiteSpace(filename))
        {
            string extension = Path.GetExtension(filename);
            if (TextExtensions.Contains(extension))
                return true;
        }

        return false;
    }

    public static bool TryDecodeUtf8Text(ReadOnlySpan<byte> bytes, out string text)
    {
        try
        {
            text = StrictUtf8.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }
    }

    public static string BuildResponseJson(
        int id,
        string filename,
        string? contentType,
        ReadOnlySpan<byte> bytes,
        bool includeBody,
        string? savedPath)
    {
        JsonObject result = new()
        {
            ["id"] = id,
            ["filename"] = filename,
            ["contentType"] = contentType,
            ["filesize"] = bytes.Length
        };

        if (!string.IsNullOrEmpty(savedPath))
            result["savedPath"] = savedPath;

        if (includeBody)
        {
            if (IsTextLike(contentType, filename) && TryDecodeUtf8Text(bytes, out string text))
                result["text"] = text;
            else
                result["contentBase64"] = Convert.ToBase64String(bytes);
        }

        return result.ToJsonString();
    }
}
