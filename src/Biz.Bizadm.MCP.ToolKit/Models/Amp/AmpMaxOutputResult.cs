namespace Biz.Bizadm.MCP.ToolKit.Models.Amp;

/// <summary>앰프 최대 출력 계산 결과</summary>
/// <param name="ImpedanceOhm">스피커 임피던스(Ω)</param>
/// <param name="PowerW">정격 출력(W)</param>
/// <param name="InputSensitivityVrms">입력 감도(Vrms)</param>
/// <param name="InputSensitivityVpp">입력 감도(Vpp)</param>
/// <param name="MaxOutputVrms">최대 출력 전압(Vrms)</param>
/// <param name="MaxOutputVpp">최대 출력 전압(Vpp)</param>
/// <param name="VoltageGainDb">전압 이득(dB)</param>
public sealed record AmpMaxOutputResult(
    double ImpedanceOhm,
    double PowerW,
    double InputSensitivityVrms,
    double InputSensitivityVpp,
    double MaxOutputVrms,
    double MaxOutputVpp,
    double VoltageGainDb);
