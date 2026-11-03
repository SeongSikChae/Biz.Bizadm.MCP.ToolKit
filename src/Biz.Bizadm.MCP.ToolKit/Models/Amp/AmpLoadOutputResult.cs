namespace Biz.Bizadm.MCP.ToolKit.Models.Amp;

/// <summary>앰프 부하 출력 계산 결과</summary>
/// <param name="InputVpp">입력 전압(Vpp)</param>
/// <param name="InputVrms">입력 전압(Vrms)</param>
/// <param name="InputAttenuationDb">입력 감쇠(dB)</param>
/// <param name="EffectiveVpp">실질 전압(Vpp)</param>
/// <param name="EffectiveVrms">실질 전압(Vrms)</param>
/// <param name="ImpedanceOhm">스피커 임피던스(Ω)</param>
/// <param name="OutputVpp">출력 전압(Vpp)</param>
/// <param name="OutputVrms">출력 전압(Vrms)</param>
/// <param name="OutputPowerW">출력 전력(W)</param>
public sealed record AmpLoadOutputResult(
    double InputVpp,
    double InputVrms,
    double InputAttenuationDb,
    double EffectiveVpp,
    double EffectiveVrms,
    double ImpedanceOhm,
    double OutputVpp,
    double OutputVrms,
    double OutputPowerW);
