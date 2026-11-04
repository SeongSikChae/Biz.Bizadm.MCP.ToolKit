using Biz.Bizadm.MCP.ToolKit.AgentResource;
using Biz.Bizadm.MCP.ToolKit.Tools.AgentResource;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>AgentResource Tool(rule·skill·tool·docs 문서)을 MCP Server에 등록하는 확장 메서드</summary>
public static class AgentResourceMcpServerBuilderExtensions
{
    /// <summary>AgentResource Tool을 등록한다. <see cref="BizadmToolKitMcpServerBuilderExtensions.WithBizadmToolKit"/>에는 포함되지 않는다.</summary>
    /// <param name="builder">Tool을 등록할 MCP Server 빌더</param>
    /// <param name="configure">문서 루트 옵션 설정</param>
    /// <returns>같은 <paramref name="builder"/></returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> 또는 <paramref name="configure"/>가 null이다.</exception>
    /// <exception cref="ArgumentException"><see cref="AgentResourceOptions.RootPath"/>가 비었거나 절대 경로가 아니다.</exception>
    public static IMcpServerBuilder WithAgentResourceTools(this IMcpServerBuilder builder, Action<AgentResourceOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        AgentResourceOptions options = new();
        configure(options);
        if (string.IsNullOrWhiteSpace(options.RootPath) || !Path.IsPathFullyQualified(options.RootPath))
            throw new ArgumentException("RootPath must be an absolute path", nameof(configure));
        options.RootPath = Path.GetFullPath(options.RootPath);

        builder.Services.AddSingleton(options);
        builder.WithTools<RuleDocumentTools>();
        builder.WithTools<SkillDocumentTools>();
        builder.WithTools<ToolDocumentTools>();
        builder.WithTools<DocDocumentTools>();
        return builder;
    }
}
