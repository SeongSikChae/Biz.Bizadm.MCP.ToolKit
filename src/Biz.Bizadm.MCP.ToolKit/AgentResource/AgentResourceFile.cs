using System.Text;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.AgentResource;

/// <summary>문서 파일을 BOM 없는 UTF-8로 읽고 쓴다. 파일 오류는 <see cref="McpException"/>으로 바꾼다.</summary>
internal static class AgentResourceFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>새 파일을 만든다.</summary>
    /// <exception cref="McpException">파일이 이미 있으면 <paramref name="alreadyExistsMessage"/>, 그 밖의 파일 오류면 실패 메시지</exception>
    public static void CreateNew(string path, string content, string alreadyExistsMessage)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write);
            using StreamWriter writer = new(stream, Utf8NoBom);
            writer.Write(content);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (ex is IOException && File.Exists(path))
                throw new McpException(alreadyExistsMessage, ex);
            throw new McpException($"Failed to write document: {ex.Message}", ex);
        }
    }

    /// <summary>기존 파일 내용을 바꾼다.</summary>
    public static void Overwrite(string path, string content) =>
        Guard(() => File.WriteAllText(path, content, Utf8NoBom), "write");

    /// <summary>파일 전문을 읽는다.</summary>
    public static string Read(string path)
    {
        string content = string.Empty;
        Guard(() => content = File.ReadAllText(path, Encoding.UTF8), "read");
        return content;
    }

    /// <summary>파일을 지운다.</summary>
    public static void Delete(string path) => Guard(() => File.Delete(path), "delete");

    /// <summary>파일을 지우고, 부모 폴더가 비었고 <paramref name="kindRoot"/>가 아니면 폴더도 지운다.</summary>
    public static void DeleteAndPruneDirectory(string path, string kindRoot) => Guard(() =>
    {
        File.Delete(path);
        string? directory = Path.GetDirectoryName(path);
        string root = Path.GetFullPath(kindRoot).TrimEnd(Path.DirectorySeparatorChar);
        if (!string.IsNullOrEmpty(directory)
            && !string.Equals(directory.TrimEnd(Path.DirectorySeparatorChar), root, StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(directory)
            && !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
        }
    }, "delete");

    private static void Guard(Action action, string verb)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new McpException($"Failed to {verb} document: {ex.Message}", ex);
        }
    }
}
