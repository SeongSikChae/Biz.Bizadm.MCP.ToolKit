using Biz.Bizadm.MCP.ToolKit.Redmine;
using Biz.Bizadm.MCP.ToolKit.Tools.Redmine;
using Biz.Bizadm.MCP.ToolKit.Tools.Redmine.V7;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Redmine Tool을 MCP Server에 등록하는 확장 메서드</summary>
public static class RedmineMcpServerBuilderExtensions
{
    /// <summary>Redmine Tool을 접속 옵션과 함께 등록한다. <see cref="RedmineOptions.Version"/>이 V2_2면 27개, V7_0이면 61개다.</summary>
    /// <param name="builder">Tool을 등록할 MCP Server 빌더</param>
    /// <param name="configure">Redmine 접속 옵션 설정</param>
    /// <returns>같은 <paramref name="builder"/></returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> 또는 <paramref name="configure"/>가 null이다.</exception>
    /// <exception cref="ArgumentException"><see cref="RedmineOptions.ApiEndpoint"/>가 절대 http/https URI가 아니거나 <see cref="RedmineOptions.ApiKey"/>가 비었거나, Version·TextFormatting이 정의되지 않은 값이거나, V2_2에 CommonMark를 지정했다.</exception>
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
        if (!Enum.IsDefined(options.Version))
            throw new ArgumentException("Version is not a supported Redmine version", nameof(configure));
        if (!Enum.IsDefined(options.TextFormatting))
            throw new ArgumentException("TextFormatting is not supported", nameof(configure));
        if (options.Version == RedmineVersion.V2_2 && options.TextFormatting == RedmineTextFormatting.CommonMark)
            throw new ArgumentException("Redmine 2.2 supports only Textile", nameof(configure));

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(sp => new RedmineClient(sp.GetRequiredService<RedmineOptions>()));
        builder.WithTools<WikiTools>();
        builder.WithTools<ProjectTools>();
        builder.WithTools<UserTools>();
        builder.WithTools<MetadataTools>();
        builder.WithTools<AttachmentTools>();
        if (options.Version == RedmineVersion.V7_0)
        {
            builder.WithTools<IssueToolsV7>();
            builder.WithTools<RelationToolsV7>();
            builder.WithTools<TimeEntryToolsV7>();
            builder.WithTools<TextGuideToolsV7>();
            builder.WithTools<JournalTools>();
            builder.WithTools<WikiExtraTools>();
            builder.WithTools<VersionTools>();
            builder.WithTools<IssueCategoryTools>();
            builder.WithTools<MembershipTools>();
            builder.WithTools<SearchTools>();
            builder.WithTools<PrincipalTools>();
            builder.WithTools<CustomFieldTools>();
            builder.WithTools<ProjectContentTools>();
            builder.WithTools<AttachmentManageTools>();
        }
        else
        {
            builder.WithTools<IssueTools>();
            builder.WithTools<RelationTools>();
            builder.WithTools<TimeEntryTools>();
            builder.WithTools<TextGuideTools>();
        }

        return builder;
    }
}
