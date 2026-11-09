using ModelContextProtocol.Client;

namespace Biz.Bizadm.MCP.ToolKit.Relay;

/// <summary>중계 대상 하나에 대한 MCP client와 그 client가 정리하지 않는 소유 자원</summary>
/// <remarks>SDK 2.3.1의 <see cref="McpClient"/>는 dispose할 때 세션 전송만 정리하고 <see cref="HttpClientTransport"/>가 소유한 HttpClient는 정리하지 않으므로 transport를 함께 보관한다.</remarks>
internal sealed class McpRelayConnection(McpClient client, IAsyncDisposable? owner = null) : IAsyncDisposable
{
    private int _toolsListed;

    public McpClient Client => client;

    /// <summary>이 연결에서 tools/list를 받았는지. SDK는 같은 client가 받은 Tool 스키마로만 <c>Mcp-Param-*</c> 헤더를 붙인다.</summary>
    public bool ToolsListed => Volatile.Read(ref _toolsListed) == 1;

    public void MarkToolsListed() => Volatile.Write(ref _toolsListed, 1);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            if (owner is not null)
                await owner.DisposeAsync().ConfigureAwait(false);
        }
    }
}
