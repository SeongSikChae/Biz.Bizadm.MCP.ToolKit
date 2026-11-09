namespace Biz.Bizadm.MCP.ToolKit.Jenkins;

/// <summary>중계할 Jenkins MCP Server 한 대의 접속 정보</summary>
public sealed class JenkinsInstance
{
    /// <summary>인스턴스 이름. Tool의 <c>instance</c> 인자 값이며 대소문자를 구분하지 않는다.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Jenkins MCP 주소 전체(절대 http/https URI, 예: <c>http://jenkins:8080/mcp-server/mcp</c>)</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Basic 인증에 쓸 Jenkins 사용자</summary>
    public string User { get; set; } = string.Empty;

    /// <summary>Basic 인증에 쓸 Jenkins API Token</summary>
    public string ApiToken { get; set; } = string.Empty;

    /// <summary>용도 설명(선택). 에이전트가 상황에 맞는 인스턴스를 고르는 근거로 <c>jenkins_list_instances</c>에 노출된다.</summary>
    public string? Description { get; set; }
}
