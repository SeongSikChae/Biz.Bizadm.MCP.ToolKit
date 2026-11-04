namespace Biz.Bizadm.MCP.ToolKit.Models.AgentResource;

/// <summary>생성·수정·삭제한 문서</summary>
/// <param name="Path">종류 루트(rules, skills, tools, docs) 기준 상대 경로</param>
public sealed record AgentResourceWriteResult(string Path);
