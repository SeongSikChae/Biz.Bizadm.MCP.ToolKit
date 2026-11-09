using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.Jenkins;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Biz.Bizadm.MCP.ToolKit.Tools.Jenkins;

/// <summary>등록한 Jenkins MCP Server들을 중계하는 게이트웨이 Tool</summary>
[McpServerToolType]
public sealed class JenkinsTools
{
    private readonly JenkinsMcpClientPool _pool;

    /// <summary>DI에 등록한 Jenkins client 풀로 Tool을 만든다. <see cref="Microsoft.Extensions.DependencyInjection.JenkinsMcpServerBuilderExtensions.WithJenkinsTools"/>로 등록해야 한다.</summary>
    /// <param name="services">서비스 공급자</param>
    public JenkinsTools(IServiceProvider services)
        : this(services.GetRequiredService<JenkinsMcpClientPool>())
    {
    }

    internal JenkinsTools(JenkinsMcpClientPool pool)
    {
        _pool = pool;
    }

    /// <summary>등록한 Jenkins 인스턴스 목록을 조회한다.</summary>
    [McpServerTool(Name = "jenkins_list_instances", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("등록한 Jenkins 인스턴스의 이름, 용도 설명, MCP Endpoint를 조회한다. 다른 jenkins_* Tool의 instance 인자로 이름을 쓴다.")]
    public string ListInstances()
    {
        JsonArray items = [];
        foreach (JenkinsInstance instance in _pool.Instances)
        {
            items.Add(new JsonObject
            {
                ["name"] = instance.Name,
                ["description"] = instance.Description,
                ["endpoint"] = instance.Endpoint,
            });
        }
        return items.ToJsonString();
    }

    /// <summary>지정한 Jenkins 인스턴스의 MCP Tool 목록을 조회한다.</summary>
    [McpServerTool(Name = "jenkins_list_tools", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("지정한 Jenkins 인스턴스의 MCP Tool 목록(이름, 설명, 입력 스키마, 주석)을 조회한다. 결과는 인스턴스별로 캐시된다.")]
    public async Task<string> ListTools(
        [Description("Jenkins 인스턴스 이름(필수). jenkins_list_instances로 확인")] string? instance = null,
        [Description("true면 캐시를 무시하고 다시 조회")] bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        JenkinsInstance target = _pool.Resolve(instance);
        IReadOnlyList<Tool> tools = await _pool.ListToolsAsync(target, refresh, cancellationToken).ConfigureAwait(false);
        JsonArray items = [];
        foreach (Tool tool in tools)
        {
            items.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["title"] = tool.Title,
                ["description"] = tool.Description,
                ["inputSchema"] = JsonNode.Parse(tool.InputSchema.GetRawText()),
                ["annotations"] = tool.Annotations is null ? null : JsonSerializer.SerializeToNode(tool.Annotations, McpJsonUtilities.DefaultOptions),
            });
        }
        return new JsonObject { ["instance"] = target.Name, ["tools"] = items }.ToJsonString();
    }

    /// <summary>지정한 Jenkins 인스턴스의 MCP Tool을 호출한다.</summary>
    [McpServerTool(Name = "jenkins_call_tool", ReadOnly = false, Idempotent = false, Destructive = true, OpenWorld = true)]
    [Description("지정한 Jenkins 인스턴스의 MCP Tool을 호출하고 결과를 그대로 반환한다. Tool 이름과 인자는 jenkins_list_tools로 확인한다.")]
    public Task<CallToolResult> CallTool(
        [Description("Jenkins 인스턴스 이름(필수). jenkins_list_instances로 확인")] string? instance = null,
        [Description("호출할 Jenkins MCP Tool 이름(필수). jenkins_list_tools로 확인")] string? tool = null,
        [Description("Tool 인자. JSON 객체 또는 JSON 객체 문자열")] JsonElement? arguments = null,
        CancellationToken cancellationToken = default)
    {
        JenkinsInstance target = _pool.Resolve(instance);
        if (string.IsNullOrWhiteSpace(tool))
            throw new McpException("tool is required.");
        return _pool.CallToolAsync(target, tool.Trim(), ToArguments(arguments), cancellationToken);
    }

    internal static Dictionary<string, object?>? ToArguments(JsonElement? arguments)
    {
        if (arguments is not { } value || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (value.ValueKind == JsonValueKind.String)
        {
            string? text = value.GetString();
            if (string.IsNullOrWhiteSpace(text))
                return null;
            try
            {
                using JsonDocument document = JsonDocument.Parse(text);
                value = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                throw new McpException("arguments must be a JSON object or a JSON object string.");
            }
        }
        if (value.ValueKind != JsonValueKind.Object)
            throw new McpException("arguments must be a JSON object or a JSON object string.");
        return value.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
    }
}
