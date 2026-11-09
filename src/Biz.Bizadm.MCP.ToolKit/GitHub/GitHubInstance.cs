namespace Biz.Bizadm.MCP.ToolKit.GitHub;

/// <summary>중계할 GitHub MCP Server 한 대의 접속 정보</summary>
public sealed class GitHubInstance
{
    /// <summary>인스턴스 이름. Tool의 <c>instance</c> 인자 값이며 대소문자를 구분하지 않는다.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>GitHub MCP 주소 전체(절대 http/https URI, 예: <c>https://api.githubcopilot.com/mcp/</c>)</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Bearer 인증에 쓸 GitHub Personal Access Token</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>용도 설명(선택). 에이전트가 상황에 맞는 인스턴스를 고르는 근거로 <c>github_list_instances</c>에 노출된다.</summary>
    public string? Description { get; set; }
}
