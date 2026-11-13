namespace Biz.Bizadm.MCP.ToolKit.NuGet;

/// <summary>NuGet 피드 Tool 옵션</summary>
public sealed class NuGetOptions
{
    /// <summary>등록할 NuGet 피드 인스턴스 목록. 1개 이상이어야 하며 기본 인스턴스는 없다.</summary>
    public IList<NuGetInstance> Instances { get; set; } = [];
}
