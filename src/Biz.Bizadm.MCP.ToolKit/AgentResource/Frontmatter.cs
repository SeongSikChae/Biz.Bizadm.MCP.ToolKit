using System.Text;
using System.Text.RegularExpressions;

namespace Biz.Bizadm.MCP.ToolKit.AgentResource;

/// <summary>문서 앞의 '---' front matter를 줄 단위 key: value로 읽고 만든다.</summary>
internal static class Frontmatter
{
    private static readonly Regex Pattern = new(
        @"^---\r?\n(.*?)\r?\n---(?:\r?\n|$)",
        RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>front matter 값을 대소문자 무시 키로 읽는다. 없으면 빈 사전이다.</summary>
    public static IReadOnlyDictionary<string, string> Parse(string content)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        Match match = Pattern.Match(content ?? string.Empty);
        if (!match.Success)
            return values;

        foreach (string rawLine in match.Groups[1].Value.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            int separator = line.IndexOf(':');
            if (separator <= 0)
                continue;
            string key = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim().Trim('"').Trim('\'');
            values.TryAdd(key, value);
        }

        return values;
    }

    /// <summary>front matter 뒤 본문을 앞뒤 공백 없이 돌려준다. front matter가 없으면 전체를 쓴다.</summary>
    public static string ExtractBody(string content)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;
        Match match = Pattern.Match(content);
        return match.Success ? content[match.Length..].Trim() : content.Trim();
    }

    /// <summary>필드 순서대로 front matter를 만들고 본문을 붙인다. 줄바꿈은 LF로 맞춘다.</summary>
    public static string Build(IEnumerable<(string Key, string Value)> fields, string body)
    {
        StringBuilder builder = new("---\n");
        foreach ((string key, string value) in fields)
            builder.Append(key).Append(": ").Append(OneLine(value)).Append('\n');
        builder.Append("---\n\n");
        builder.Append((body ?? string.Empty).Trim().Replace("\r\n", "\n").Replace('\r', '\n'));
        builder.Append('\n');
        return builder.ToString();
    }

    /// <summary>값의 줄바꿈을 공백으로 바꿔 한 줄로 만든다.</summary>
    public static string OneLine(string value) =>
        (value ?? string.Empty).Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
}
