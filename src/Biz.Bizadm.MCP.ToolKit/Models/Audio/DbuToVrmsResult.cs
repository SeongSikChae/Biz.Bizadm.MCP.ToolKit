namespace Biz.Bizadm.MCP.ToolKit.Models.Audio;

/// <summary>dBu → Vrms 환산 결과</summary>
/// <param name="Dbu">레벨(dBu)</param>
/// <param name="Reference">0dBu 기준</param>
/// <param name="ReferenceVrms">0dBu 기준 전압(Vrms)</param>
/// <param name="Vrms">전압(Vrms)</param>
public sealed record DbuToVrmsResult(
    double Dbu,
    DbuReference Reference,
    double ReferenceVrms,
    double Vrms);
