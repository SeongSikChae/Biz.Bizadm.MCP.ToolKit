using System.Text.Json;
using System.Text.Json.Nodes;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Biz.Bizadm.MCP.ToolKit.Relay;

/// <summary>중계 게이트웨이 Tool(목록·Tool 목록·호출)의 공통 본문</summary>
internal static class McpRelayTools
{
    /// <summary>인스턴스 이름·설명·Endpoint JSON 배열. 헤더(자격 증명)는 넣지 않는다.</summary>
    public static string ListInstances(McpRelayPool pool)
    {
        JsonArray items = [];
        foreach (McpRelayTarget instance in pool.Instances)
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

    /// <summary>인스턴스 Tool 목록 JSON <c>{instance, tools:[...]}</c></summary>
    public static async Task<string> ListToolsAsync(McpRelayPool pool, string? instance, bool refresh, CancellationToken cancellationToken)
    {
        McpRelayTarget target = pool.Resolve(instance);
        IReadOnlyList<Tool> tools = await pool.ListToolsAsync(target, refresh, cancellationToken).ConfigureAwait(false);
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

    /// <summary>인스턴스의 Tool을 호출하고 upstream 결과를 그대로 반환한다.</summary>
    public static Task<CallToolResult> CallToolAsync(McpRelayPool pool, string? instance, string? tool, JsonElement? arguments, CancellationToken cancellationToken)
    {
        McpRelayTarget target = pool.Resolve(instance);
        if (string.IsNullOrWhiteSpace(tool))
            throw new McpException("tool is required.");
        return pool.CallToolAsync(target, tool.Trim(), ToArguments(arguments), cancellationToken);
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
