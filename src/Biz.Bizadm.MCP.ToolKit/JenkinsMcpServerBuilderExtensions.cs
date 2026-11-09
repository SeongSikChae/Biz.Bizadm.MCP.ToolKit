using Biz.Bizadm.MCP.ToolKit.Jenkins;
using Biz.Bizadm.MCP.ToolKit.Tools.Jenkins;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Jenkins MCP 중계 Tool을 MCP Server에 등록하는 확장 메서드</summary>
public static class JenkinsMcpServerBuilderExtensions
{
    /// <summary>Jenkins MCP 중계 Tool(jenkins_list_instances, jenkins_list_tools, jenkins_call_tool)을 인스턴스 옵션과 함께 등록한다.</summary>
    /// <param name="builder">Tool을 등록할 MCP Server 빌더</param>
    /// <param name="configure">Jenkins 인스턴스 옵션 설정</param>
    /// <returns>같은 <paramref name="builder"/></returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> 또는 <paramref name="configure"/>가 null이다.</exception>
    /// <exception cref="ArgumentException">인스턴스가 없거나, null 항목·빈 이름·중복 이름(대소문자 무시)이 있거나, Endpoint가 절대 http/https URI가 아니거나 사용자 정보를 포함하거나, User·ApiToken이 비었다.</exception>
    public static IMcpServerBuilder WithJenkinsTools(this IMcpServerBuilder builder, Action<JenkinsOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        JenkinsOptions options = new();
        configure(options);
        JenkinsOptions validated = Validate(options);

        builder.Services.AddSingleton(validated);
        builder.Services.AddSingleton(sp => new JenkinsMcpClientPool(sp.GetRequiredService<JenkinsOptions>()));
        builder.WithTools<JenkinsTools>();
        return builder;
    }

    private static JenkinsOptions Validate(JenkinsOptions options)
    {
        if (options.Instances is null || options.Instances.Count == 0)
            throw new ArgumentException("At least one Jenkins instance is required", nameof(options));

        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        List<JenkinsInstance> copies = [];
        foreach (JenkinsInstance? instance in options.Instances)
        {
            if (instance is null)
                throw new ArgumentException("Jenkins instance must not be null", nameof(options));
            string name = instance.Name?.Trim() ?? string.Empty;
            if (name.Length == 0)
                throw new ArgumentException("Jenkins instance Name is required", nameof(options));
            if (!names.Add(name))
                throw new ArgumentException($"Duplicate Jenkins instance name: {name}", nameof(options));
            if (!Uri.TryCreate(instance.Endpoint?.Trim(), UriKind.Absolute, out Uri? endpoint)
                || endpoint.Scheme is not ("http" or "https"))
                throw new ArgumentException($"Jenkins instance '{name}' Endpoint must be an absolute http or https URI", nameof(options));
            // 사용자 정보가 있으면 jenkins_list_instances로 자격 증명이 노출된다
            if (!string.IsNullOrEmpty(endpoint.UserInfo))
                throw new ArgumentException($"Jenkins instance '{name}' Endpoint must not contain user info. Use User and ApiToken", nameof(options));
            if (string.IsNullOrWhiteSpace(instance.User))
                throw new ArgumentException($"Jenkins instance '{name}' User is required", nameof(options));
            if (string.IsNullOrWhiteSpace(instance.ApiToken))
                throw new ArgumentException($"Jenkins instance '{name}' ApiToken is required", nameof(options));

            copies.Add(new JenkinsInstance
            {
                Name = name,
                Endpoint = endpoint.AbsoluteUri,
                User = instance.User.Trim(),
                ApiToken = instance.ApiToken,
                Description = string.IsNullOrWhiteSpace(instance.Description) ? null : instance.Description.Trim(),
            });
        }

        return new JenkinsOptions { Instances = copies };
    }
}
