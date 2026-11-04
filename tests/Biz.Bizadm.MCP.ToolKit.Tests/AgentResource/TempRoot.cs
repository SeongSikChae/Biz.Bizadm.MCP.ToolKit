using Biz.Bizadm.MCP.ToolKit.AgentResource;

namespace Biz.Bizadm.MCP.ToolKit.Tests.AgentResource;

/// <summary>테스트마다 따로 쓰는 임시 문서 루트</summary>
internal sealed class TempRoot : IDisposable
{
    public TempRoot()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "agent-resource-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
        Options = new AgentResourceOptions { RootPath = RootPath };
    }

    public string RootPath { get; }

    public AgentResourceOptions Options { get; }

    public void Dispose()
    {
        if (Directory.Exists(RootPath))
            Directory.Delete(RootPath, recursive: true);
    }
}
