using ModelContextProtocol.Client;

namespace Biz.Bizadm.MCP.ToolKit.Jenkins;

/// <summary>Jenkins 인스턴스 하나에 대한 MCP client와 그 client가 정리하지 않는 소유 자원</summary>
/// <remarks>SDK 2.3.1의 <see cref="McpClient"/>는 dispose할 때 세션 전송만 정리하고 <see cref="HttpClientTransport"/>가 소유한 HttpClient는 정리하지 않으므로 transport를 함께 보관한다.</remarks>
internal sealed class JenkinsConnection(McpClient client, IAsyncDisposable? owner = null) : IAsyncDisposable
{
    public McpClient Client => client;

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
