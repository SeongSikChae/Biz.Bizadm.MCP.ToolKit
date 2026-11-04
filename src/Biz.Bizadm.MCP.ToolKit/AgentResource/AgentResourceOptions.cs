namespace Biz.Bizadm.MCP.ToolKit.AgentResource;

/// <summary>AgentResource Tool이 rule·skill·tool·docs 문서를 다루는 루트 폴더 옵션</summary>
public sealed class AgentResourceOptions
{
    /// <summary>문서 루트 폴더 절대 경로. 아래에 rules, skills, tools, docs 폴더를 둔다.</summary>
    public string RootPath { get; set; } = string.Empty;
}
