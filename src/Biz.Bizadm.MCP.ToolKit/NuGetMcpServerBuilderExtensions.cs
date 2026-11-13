using Biz.Bizadm.MCP.ToolKit.NuGet;
using Biz.Bizadm.MCP.ToolKit.Relay;
using Biz.Bizadm.MCP.ToolKit.Tools.NuGet;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>NuGet 피드 Tool을 MCP Server에 등록하는 확장 메서드</summary>
public static class NuGetMcpServerBuilderExtensions
{
    internal const string Label = "NuGet";

    /// <summary>NuGet Server API v3 피드 Tool(nuget_list_instances, nuget_search_packages, nuget_list_versions, nuget_get_package_metadata, nuget_check_vulnerabilities, nuget_push_package, nuget_unlist_package, nuget_relist_package)을 인스턴스 옵션과 함께 등록한다.</summary>
    /// <param name="builder">Tool을 등록할 MCP Server 빌더</param>
    /// <param name="configure">NuGet 인스턴스 옵션 설정</param>
    /// <returns>같은 <paramref name="builder"/></returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> 또는 <paramref name="configure"/>가 null이다.</exception>
    /// <exception cref="ArgumentException">인스턴스가 없거나, null 항목·빈 이름·중복 이름(대소문자 무시)이 있거나, ServiceIndex가 절대 http/https URI가 아니거나 사용자 정보를 포함하거나, User·Password 중 하나만 있거나, User에 ':'가 있거나, User·Password·ApiKey에 제어 문자가 있다.</exception>
    public static IMcpServerBuilder WithNuGetTools(this IMcpServerBuilder builder, Action<NuGetOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        NuGetOptions options = new();
        configure(options);
        NuGetOptions validated = Validate(options);

        builder.Services.AddSingleton(validated);
        builder.Services.AddSingleton(sp => new NuGetFeedClient(sp.GetRequiredService<NuGetOptions>()));
        builder.WithTools<NuGetTools>();
        return builder;
    }

    private static NuGetOptions Validate(NuGetOptions options)
    {
        McpRelayValidation.RequireInstances(Label, options.Instances);

        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        List<NuGetInstance> copies = [];
        foreach (NuGetInstance? item in options.Instances)
        {
            NuGetInstance instance = McpRelayValidation.NotNull(Label, item);
            (string name, string serviceIndex) = McpRelayValidation.NameAndEndpoint(Label, names, instance.Name, instance.ServiceIndex,
                "User and Password", property: "ServiceIndex");
            string? user = Blank(instance.User)?.Trim();
            string? password = Blank(instance.Password);
            string? apiKey = Blank(instance.ApiKey);
            if ((user is null) != (password is null))
                throw new ArgumentException($"NuGet instance '{name}' User and Password must be set together", nameof(options));
            if (user is not null && user.Contains(':'))
                throw new ArgumentException($"NuGet instance '{name}' User must not contain ':'", nameof(options));
            RejectControl(name, "User", user);
            RejectControl(name, "Password", password);
            RejectControl(name, "ApiKey", apiKey);

            copies.Add(new NuGetInstance
            {
                Name = name,
                ServiceIndex = serviceIndex,
                Description = Blank(instance.Description)?.Trim(),
                ApiKey = apiKey,
                User = user,
                Password = password,
            });
        }

        return new NuGetOptions { Instances = copies };
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    // 값은 메시지에 넣지 않는다
    private static void RejectControl(string name, string property, string? value)
    {
        if (value is not null && value.Any(char.IsControl))
            throw new ArgumentException($"NuGet instance '{name}' {property} must not contain control characters", "options");
    }
}
