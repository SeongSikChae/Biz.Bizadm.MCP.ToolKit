namespace Biz.Bizadm.MCP.ToolKit.Models.Audio;

/// <summary>dBu → dBFS 환산 결과</summary>
/// <param name="FullScaleDbu">0dBFS에 해당하는 기준 레벨(dBu)</param>
/// <param name="Dbu">레벨(dBu)</param>
/// <param name="Dbfs">디지털 레벨(dBFS)</param>
public sealed record DbuToDbfsResult(
    double FullScaleDbu,
    double Dbu,
    double Dbfs);
