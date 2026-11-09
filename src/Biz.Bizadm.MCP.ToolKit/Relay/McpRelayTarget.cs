namespace Biz.Bizadm.MCP.ToolKit.Relay;

/// <summary>중계할 MCP Server 한 대. 서비스별 인스턴스 설정에서 만든다.</summary>
/// <param name="Name">인스턴스 이름. Tool의 <c>instance</c> 인자 값이며 대소문자를 구분하지 않는다.</param>
/// <param name="Endpoint">MCP 주소 전체(절대 http/https URI)</param>
/// <param name="Description">용도 설명(선택)</param>
/// <param name="Headers">요청마다 보낼 헤더(인증 등). 결과·메시지에 노출하지 않는다.</param>
internal sealed record McpRelayTarget(string Name, string Endpoint, string? Description, IReadOnlyDictionary<string, string> Headers);
