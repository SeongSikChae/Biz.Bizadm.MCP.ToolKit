using System.Text.Json;
using Biz.Bizadm.MCP.ToolKit.Models.AgentResource;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Biz.Bizadm.MCP.ToolKit.Tests.AgentResource;

[TestClass]
public sealed class AgentResourceRegistrationTests
{
    private static readonly string[] ToolNames =
    [
        "list_rule_documents", "get_rule_document", "create_rule_document", "update_rule_document", "delete_rule_document",
        "list_skill_documents", "get_skill_document", "create_skill_document", "update_skill_document", "delete_skill_document",
        "list_tool_documents", "get_tool_document_usage", "get_tool_document_registration_guide",
        "register_tool_document", "update_tool_document", "delete_tool_document",
        "list_doc_documents", "get_doc_document", "create_doc_document", "update_doc_document", "delete_doc_document",
    ];

    private static List<McpServerTool> AgentResourceTools()
    {
        ServiceCollection services = new();
        services.AddMcpServer().WithAgentResourceTools(o => o.RootPath = Path.GetTempPath());
        using ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetServices<McpServerTool>().ToList();
    }

    [TestMethod]
    public void WithAgentResourceTools_RegistersAll21Tools()
    {
        List<string> names = AgentResourceTools().Select(t => t.ProtocolTool.Name).ToList();
        Assert.HasCount(21, names);
        CollectionAssert.AreEquivalent(ToolNames, names);
    }

    [TestMethod]
    public void WithBizadmToolKit_DoesNotRegisterAgentResourceTools()
    {
        ServiceCollection services = new();
        services.AddMcpServer().WithBizadmToolKit();
        using ServiceProvider provider = services.BuildServiceProvider();
        List<string> names = provider.GetServices<McpServerTool>().Select(t => t.ProtocolTool.Name).ToList();
        foreach (string name in ToolNames)
            CollectionAssert.DoesNotContain(names, name);
    }

    [TestMethod]
    public void WithAgentResourceTools_NullArguments_Throw()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => AgentResourceMcpServerBuilderExtensions.WithAgentResourceTools(null!, _ => { }));
        ServiceCollection services = new();
        Assert.ThrowsExactly<ArgumentNullException>(() => services.AddMcpServer().WithAgentResourceTools(null!));
    }

    [TestMethod]
    public void ToolAnnotations_MatchDesignTable()
    {
        foreach (McpServerTool tool in AgentResourceTools())
        {
            string name = tool.ProtocolTool.Name;
            var annotations = tool.ProtocolTool.Annotations!;
            bool read = name.StartsWith("list_") || name.StartsWith("get_");
            bool create = name.StartsWith("create_") || name.StartsWith("register_");
            bool update = name.StartsWith("update_");
            Assert.AreEqual(read, annotations.ReadOnlyHint, name);
            Assert.AreEqual(read || update, annotations.IdempotentHint, name);
            Assert.AreEqual(!read && !create, annotations.DestructiveHint, name);
            Assert.AreEqual(false, annotations.OpenWorldHint, name);
            Assert.IsNotNull(tool.ProtocolTool.OutputSchema, name);
        }
    }

    [TestMethod]
    [DataRow("list_rule_documents")]
    [DataRow("list_skill_documents")]
    [DataRow("list_tool_documents")]
    [DataRow("get_tool_document_usage")]
    [DataRow("list_doc_documents")]
    public void NullableProperties_SerializeAllRequiredSchemaProperties(string toolName)
    {
        object value = toolName switch
        {
            "list_rule_documents" => new RuleDocumentListResult([new RuleDocumentSummary("a.md", null)]),
            "list_skill_documents" => new SkillDocumentListResult([new SkillDocumentSummary("a", null, null)]),
            "list_tool_documents" => new ToolDocumentListResult([new ToolDocumentSummary("a", null, null, null, null)]),
            "get_tool_document_usage" => new ToolDocumentUsage("a", null, "u"),
            _ => new DocDocumentListResult([new DocDocumentSummary("a/0001-b-c.md", "a", 1, null, null, null)]),
        };
        JsonElement schema = AgentResourceTools().Single(t => t.ProtocolTool.Name == toolName).ProtocolTool.OutputSchema!.Value;
        JsonElement json = JsonSerializer.SerializeToElement(value, value.GetType(), McpJsonUtilities.DefaultOptions);

        // 목록 결과는 항목 스키마(items)의 required까지 검사해야 nullable 속성 직렬화를 확인한 것이다.
        int expectedMinimum = toolName == "get_tool_document_usage" ? 3 : 2;
        Assert.IsGreaterThanOrEqualTo(expectedMinimum, AssertRequiredPresent(schema, json));
    }

    private static int AssertRequiredPresent(JsonElement schema, JsonElement json)
    {
        int checkedCount = 0;
        if (schema.TryGetProperty("required", out JsonElement required))
        {
            foreach (JsonElement name in required.EnumerateArray())
            {
                Assert.IsTrue(json.TryGetProperty(name.GetString()!, out _), name.GetString());
                checkedCount++;
            }
        }

        if (!schema.TryGetProperty("properties", out JsonElement properties))
            return checkedCount;
        foreach (JsonProperty property in properties.EnumerateObject())
        {
            if (json.TryGetProperty(property.Name, out JsonElement child)
                && child.ValueKind == JsonValueKind.Array
                && property.Value.TryGetProperty("items", out JsonElement items))
            {
                foreach (JsonElement element in child.EnumerateArray())
                    checkedCount += AssertRequiredPresent(items, element);
            }
        }
        return checkedCount;
    }
}
