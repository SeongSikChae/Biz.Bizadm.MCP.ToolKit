namespace Biz.Bizadm.MCP.ToolKit.Models.Audio;

/// <summary>dBFS → dBu 환산 결과</summary>
/// <param name="FullScaleDbu">0dBFS에 해당하는 기준 레벨(dBu)</param>
/// <param name="Dbfs">디지털 레벨(dBFS)</param>
/// <param name="Dbu">레벨(dBu)</param>
public sealed record DbfsToDbuResult(
    double FullScaleDbu,
    double Dbfs,
    double Dbu);
