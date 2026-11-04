using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.AgentResource;

/// <summary>종류 루트 아래 상대 경로를 정규화하고 루트 밖으로 나가는 경로를 막는다.</summary>
internal static class AgentResourcePath
{
    /// <summary>앞뒤 공백을 자르고 구분자를 '/'로 바꾸고 앞쪽 '/'를 없앤다.</summary>
    public static string Normalize(string? value) => (value ?? string.Empty).Trim().Replace('\\', '/').TrimStart('/');

    /// <summary>정규화된 상대 경로를 <paramref name="kindRoot"/> 아래 절대 경로로 바꾼다.</summary>
    /// <exception cref="McpException">비었거나 절대 경로이거나 루트 밖을 가리킨다.</exception>
    public static string Resolve(string kindRoot, string relative, string kindName)
    {
        if (string.IsNullOrWhiteSpace(relative))
            throw new McpException("name is required");
        if (Path.IsPathRooted(relative) || relative.Contains(':'))
            throw new McpException("Absolute paths are not allowed");
        if (relative.Split('/').Any(segment => segment == ".."))
            throw new McpException($"Path escapes the {kindName} directory");
        // Windows는 세그먼트 끝의 '.'·공백을 잘라내므로 "create-pr."가 "create-pr"의 별칭이 된다.
        if (relative.Split('/').Any(segment => segment.Length > 0 && (segment.EndsWith('.') || char.IsWhiteSpace(segment[^1]))))
            throw new McpException("Path segments must not end with '.' or whitespace");

        string root = Path.GetFullPath(kindRoot).TrimEnd(Path.DirectorySeparatorChar);
        string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        // Windows는 끝의 '.'·공백을 잘라내므로 "./", "..." 같은 입력은 루트 자체가 된다.
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(full.TrimEnd(Path.DirectorySeparatorChar), root, StringComparison.OrdinalIgnoreCase))
            throw new McpException($"Path escapes the {kindName} directory");
        return full;
    }
}
