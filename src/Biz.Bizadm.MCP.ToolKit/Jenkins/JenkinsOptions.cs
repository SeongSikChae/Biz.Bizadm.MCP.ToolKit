namespace Biz.Bizadm.MCP.ToolKit.Jenkins;

/// <summary>Jenkins MCP 중계 Tool 옵션</summary>
public sealed class JenkinsOptions
{
    /// <summary>등록할 Jenkins 인스턴스 목록. 1개 이상이어야 하며 기본 인스턴스는 없다.</summary>
    public IList<JenkinsInstance> Instances { get; set; } = [];
}
