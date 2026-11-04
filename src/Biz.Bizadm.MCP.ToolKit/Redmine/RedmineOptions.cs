namespace Biz.Bizadm.MCP.ToolKit.Redmine;

/// <summary>Redmine Tool 접속 옵션</summary>
public sealed class RedmineOptions
{
    /// <summary>Redmine API 엔드포인트(절대 http/https URI, 하위 경로 허용)</summary>
    public string ApiEndpoint { get; set; } = string.Empty;

    /// <summary>X-Redmine-API-Key 헤더로 보낼 API 키</summary>
    public string ApiKey { get; set; } = string.Empty;
}
