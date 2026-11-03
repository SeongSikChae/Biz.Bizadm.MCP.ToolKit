namespace Biz.Bizadm.MCP.ToolKit.Models.Audio;

/// <summary>Vrms → dBu 환산 결과</summary>
/// <param name="Vrms">전압(Vrms)</param>
/// <param name="Reference">0dBu 기준</param>
/// <param name="ReferenceVrms">0dBu 기준 전압(Vrms)</param>
/// <param name="Dbu">레벨(dBu)</param>
public sealed record VrmsToDbuResult(
    double Vrms,
    DbuReference Reference,
    double ReferenceVrms,
    double Dbu);
