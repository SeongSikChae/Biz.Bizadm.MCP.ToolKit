namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Biz.Bizadm.MCP.ToolKit의 Tool을 MCP Server에 등록하는 확장 메서드</summary>
public static class BizadmToolKitMcpServerBuilderExtensions
{
    /// <summary>Biz.Bizadm.MCP.ToolKit의 모든 Tool을 등록한다.</summary>
    /// <param name="builder">Tool을 등록할 MCP Server 빌더</param>
    /// <returns>같은 <paramref name="builder"/></returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/>가 null이다.</exception>
    public static IMcpServerBuilder WithBizadmToolKit(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        // Tool 클래스 추가 시: builder.WithTools<XxxTools>();
        return builder;
    }
}
