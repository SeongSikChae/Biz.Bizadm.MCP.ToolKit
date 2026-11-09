using Biz.Bizadm.MCP.ToolKit.GitHub;
using Biz.Bizadm.MCP.ToolKit.Relay;
using Biz.Bizadm.MCP.ToolKit.Tools.GitHub;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>GitHub MCP 중계 Tool을 MCP Server에 등록하는 확장 메서드</summary>
public static class GitHubMcpServerBuilderExtensions
{
    internal const string Label = "GitHub";

    /// <summary>GitHub MCP 중계 Tool(github_list_instances, github_list_tools, github_call_tool), REST 태그·ref Tool(github_create_lightweight_tag, github_create_annotated_tag, github_update_ref, github_delete_ref), REST 릴리스 Tool(github_create_release, github_update_release, github_delete_release, github_upload_release_asset, github_delete_release_asset)을 인스턴스 옵션과 함께 등록한다.</summary>
    /// <param name="builder">Tool을 등록할 MCP Server 빌더</param>
    /// <param name="configure">GitHub 인스턴스 옵션 설정</param>
    /// <returns>같은 <paramref name="builder"/></returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> 또는 <paramref name="configure"/>가 null이다.</exception>
    /// <exception cref="ArgumentException">인스턴스가 없거나, null 항목·빈 이름·중복 이름(대소문자 무시)이 있거나, Endpoint·ApiEndpoint가 절대 http/https URI가 아니거나 사용자 정보를 포함하거나, Token이 비었다.</exception>
    public static IMcpServerBuilder WithGitHubTools(this IMcpServerBuilder builder, Action<GitHubOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        GitHubOptions options = new();
        configure(options);
        GitHubOptions validated = Validate(options);

        builder.Services.AddSingleton(validated);
        builder.Services.AddKeyedSingleton(GitHubTools.PoolKey,
            (sp, _) => new McpRelayPool(Label, sp.GetRequiredService<GitHubOptions>().Instances.Select(ToTarget)));
        builder.Services.AddSingleton(sp => new GitHubRestClient(sp.GetRequiredService<GitHubOptions>()));
        builder.WithTools<GitHubTools>();
        builder.WithTools<GitHubRefTools>();
        builder.WithTools<GitHubReleaseTools>();
        return builder;
    }

    internal static McpRelayTarget ToTarget(GitHubInstance instance) => new(
        instance.Name,
        instance.Endpoint,
        instance.Description,
        new Dictionary<string, string> { ["Authorization"] = $"Bearer {instance.Token}" });

    private static GitHubOptions Validate(GitHubOptions options)
    {
        McpRelayValidation.RequireInstances(Label, options.Instances);

        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        List<GitHubInstance> copies = [];
        foreach (GitHubInstance? item in options.Instances)
        {
            GitHubInstance instance = McpRelayValidation.NotNull(Label, item);
            (string name, string endpoint) = McpRelayValidation.NameAndEndpoint(Label, names, instance.Name, instance.Endpoint, "Token");
            string apiEndpoint = McpRelayValidation.AbsoluteHttpUri(Label, name, "ApiEndpoint", instance.ApiEndpoint, "Token");
            Uri apiUri = new(apiEndpoint);
            if (apiUri.Query.Length > 0 || apiUri.Fragment.Length > 0)
                throw new ArgumentException($"GitHub instance '{name}' ApiEndpoint must not contain a query or fragment", nameof(options));
            // 상대 경로(repos/...)를 붙일 수 있게 끝을 /로 맞춘다
            if (!apiEndpoint.EndsWith('/'))
                apiEndpoint += "/";
            if (string.IsNullOrWhiteSpace(instance.Token))
                throw new ArgumentException($"GitHub instance '{name}' Token is required", nameof(options));

            copies.Add(new GitHubInstance
            {
                Name = name,
                Endpoint = endpoint,
                ApiEndpoint = apiEndpoint,
                Token = instance.Token.Trim(),
                Description = string.IsNullOrWhiteSpace(instance.Description) ? null : instance.Description.Trim(),
            });
        }

        return new GitHubOptions { Instances = copies };
    }
}
