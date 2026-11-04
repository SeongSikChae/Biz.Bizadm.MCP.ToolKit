using Biz.Bizadm.MCP.ToolKit.Redmine;
using Biz.Bizadm.MCP.ToolKit.Tools.Redmine;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Redmine Tool을 MCP Server에 등록하는 확장 메서드</summary>
public static class RedmineMcpServerBuilderExtensions
{
    /// <summary>Redmine Tool(9개 클래스, 27개)을 접속 옵션과 함께 등록한다.</summary>
    /// <param name="builder">Tool을 등록할 MCP Server 빌더</param>
    /// <param name="configure">Redmine 접속 옵션 설정</param>
    /// <returns>같은 <paramref name="builder"/></returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> 또는 <paramref name="configure"/>가 null이다.</exception>
    /// <exception cref="ArgumentException"><see cref="RedmineOptions.ApiEndpoint"/>가 절대 http/https URI가 아니거나 <see cref="RedmineOptions.ApiKey"/>가 비었다.</exception>
    public static IMcpServerBuilder WithRedmineTools(this IMcpServerBuilder builder, Action<RedmineOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        RedmineOptions options = new();
        configure(options);
        if (!Uri.TryCreate(options.ApiEndpoint?.Trim(), UriKind.Absolute, out Uri? endpoint)
            || endpoint.Scheme is not ("http" or "https"))
            throw new ArgumentException("ApiEndpoint must be an absolute http or https URI", nameof(configure));
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new ArgumentException("ApiKey is required", nameof(configure));

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(sp => new RedmineClient(sp.GetRequiredService<RedmineOptions>()));
        builder.WithTools<IssueTools>();
        builder.WithTools<RelationTools>();
        builder.WithTools<TimeEntryTools>();
        builder.WithTools<WikiTools>();
        builder.WithTools<ProjectTools>();
        builder.WithTools<UserTools>();
        builder.WithTools<MetadataTools>();
        builder.WithTools<AttachmentTools>();
        builder.WithTools<TextGuideTools>();
        return builder;
    }
}
