namespace Biz.Bizadm.MCP.ToolKit.Redmine;

/// <summary>Redmine Tool 접속 옵션</summary>
public sealed class RedmineOptions
{
    /// <summary>Redmine API 엔드포인트(절대 http/https URI, 하위 경로 허용)</summary>
    public string ApiEndpoint { get; set; } = string.Empty;

    /// <summary>X-Redmine-API-Key 헤더로 보낼 API 키</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>대상 Redmine 서버 버전. 기본 <see cref="RedmineVersion.V2_2"/>이며 버전에 따라 등록할 Tool이 달라진다.</summary>
    public RedmineVersion Version { get; set; } = RedmineVersion.V2_2;

    /// <summary>서버 텍스트 서식. 기본 <see cref="RedmineTextFormatting.Textile"/>이며 서버 설정과 같은 값으로 둔다.</summary>
    public RedmineTextFormatting TextFormatting { get; set; } = RedmineTextFormatting.Textile;
}
