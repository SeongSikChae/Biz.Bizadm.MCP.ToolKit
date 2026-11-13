using System.ComponentModel;
using System.Text.Json.Nodes;

using Biz.Bizadm.MCP.ToolKit.NuGet;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Biz.Bizadm.MCP.ToolKit.Tools.NuGet;

/// <summary>NuGet Server API v3로 패키지를 조회하고 push·unlist·relist하는 Tool</summary>
[McpServerToolType]
public sealed class NuGetTools
{
    private readonly NuGetFeedClient _client;

    /// <summary>DI에 등록한 NuGet client로 Tool을 만든다. <see cref="NuGetMcpServerBuilderExtensions.WithNuGetTools"/>로 등록해야 한다.</summary>
    /// <param name="services">서비스 공급자</param>
    public NuGetTools(IServiceProvider services)
        : this(services.GetRequiredService<NuGetFeedClient>())
    {
    }

    internal NuGetTools(NuGetFeedClient client)
    {
        _client = client;
    }

    /// <summary>등록한 NuGet 인스턴스 목록을 조회한다.</summary>
    [McpServerTool(Name = "nuget_list_instances", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("등록한 NuGet 피드 인스턴스의 이름, 용도 설명, service index 주소, 쓰기 가능 여부(canWrite = ApiKey 설정 여부)를 조회한다. 다른 nuget_* Tool의 instance 인자로 이름을 쓴다.")]
    public string ListInstances() => new JsonArray([.. _client.Instances.Select(i => (JsonNode)new JsonObject
    {
        ["name"] = i.Name,
        ["description"] = i.Description,
        ["serviceIndex"] = i.ServiceIndex,
        ["canWrite"] = i.ApiKey is not null,
    })]).ToJsonString();
}
