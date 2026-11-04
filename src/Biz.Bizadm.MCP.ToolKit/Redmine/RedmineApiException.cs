using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Redmine;

/// <summary>Redmine REST API 요청이 실패했을 때 던지는 예외</summary>
/// <param name="statusCode">HTTP 상태 코드</param>
/// <param name="responseBody">응답 본문</param>
public sealed class RedmineApiException(int statusCode, string responseBody)
    : McpException($"Redmine API request failed with status {statusCode}. {responseBody}")
{
    /// <summary>HTTP 상태 코드</summary>
    public int StatusCode { get; } = statusCode;

    /// <summary>응답 본문</summary>
    public string ResponseBody { get; } = responseBody;
}
