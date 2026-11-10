using System.Text;

using Biz.Bizadm.MCP.ToolKit.Jenkins;
using Biz.Bizadm.MCP.ToolKit.Relay;
using Biz.Bizadm.MCP.ToolKit.Tools.Jenkins;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Jenkins MCP 중계 Tool을 MCP Server에 등록하는 확장 메서드</summary>
public static class JenkinsMcpServerBuilderExtensions
{
    internal const string Label = "Jenkins";

    /// <summary>Jenkins MCP 중계 Tool(jenkins_list_instances, jenkins_list_tools, jenkins_call_tool)과 Job 설정 REST Tool(jenkins_get_job_config, jenkins_update_job_config, jenkins_update_pipeline_script, jenkins_delete_job)을 인스턴스 옵션과 함께 등록한다.</summary>
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
        builder.Services.AddKeyedSingleton(JenkinsTools.PoolKey,
            (sp, _) => new McpRelayPool(Label, sp.GetRequiredService<JenkinsOptions>().Instances.Select(ToTarget)));
        builder.Services.AddSingleton(sp => new JenkinsRestClient(sp.GetRequiredService<JenkinsOptions>()));
        builder.WithTools<JenkinsTools>();
        builder.WithTools<JenkinsJobTools>();
        return builder;
    }

    internal static McpRelayTarget ToTarget(JenkinsInstance instance) => new(
        instance.Name,
        instance.Endpoint,
        instance.Description,
        new Dictionary<string, string>
        {
            ["Authorization"] = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{instance.User}:{instance.ApiToken}")),
        });

    private static JenkinsOptions Validate(JenkinsOptions options)
    {
        McpRelayValidation.RequireInstances(Label, options.Instances);

        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        List<JenkinsInstance> copies = [];
        foreach (JenkinsInstance? item in options.Instances)
        {
            JenkinsInstance instance = McpRelayValidation.NotNull(Label, item);
            (string name, string endpoint) = McpRelayValidation.NameAndEndpoint(Label, names, instance.Name, instance.Endpoint, "User and ApiToken");
            if (string.IsNullOrWhiteSpace(instance.User))
                throw new ArgumentException($"Jenkins instance '{name}' User is required", nameof(options));
            if (string.IsNullOrWhiteSpace(instance.ApiToken))
                throw new ArgumentException($"Jenkins instance '{name}' ApiToken is required", nameof(options));

            copies.Add(new JenkinsInstance
            {
                Name = name,
                Endpoint = endpoint,
                User = instance.User.Trim(),
                ApiToken = instance.ApiToken,
                Description = string.IsNullOrWhiteSpace(instance.Description) ? null : instance.Description.Trim(),
            });
        }

        return new JenkinsOptions { Instances = copies };
    }
}
