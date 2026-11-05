using System.Globalization;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Redmine;

/// <summary>Redmine API 경로 작성·ID 검사 helper</summary>
internal static class RedminePaths
{
    /// <summary><c>projects/{projectId}/{suffix}</c> 경로를 만든다. projectId는 trim 후 이스케이프한다.</summary>
    public static string Project(string projectId, string suffix)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            throw new McpException("projectId is required.");
        return $"projects/{Uri.EscapeDataString(projectId.Trim())}/{suffix}";
    }

    /// <summary>경로 조각으로 쓸 값을 trim 후 이스케이프한다. 비었으면 McpException이다.</summary>
    public static string Segment(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new McpException($"{name} is required.");
        return Uri.EscapeDataString(value.Trim());
    }

    /// <summary>양수 ID를 경로 문자열로 바꾼다. 1보다 작으면 McpException이다.</summary>
    public static string Id(int value, string name)
    {
        EnsurePositive(value, name);
        return value.ToString(CultureInfo.InvariantCulture);
    }

    public static void EnsurePositive(int value, string name)
    {
        if (value < 1)
            throw new McpException($"{name} must be positive.");
    }
}
