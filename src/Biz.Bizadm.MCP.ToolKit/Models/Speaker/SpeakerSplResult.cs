namespace Biz.Bizadm.MCP.ToolKit.Models.Speaker;

/// <summary>스피커 SPL 계산 결과</summary>
/// <param name="NominalSplDb">Nominal SPL(dB SPL, 1W/1m)</param>
/// <param name="InputPowerW">입력 전력(W)</param>
/// <param name="DistanceM">거리(m)</param>
/// <param name="SourceType">음원 유형</param>
/// <param name="OutputSplDb">Output SPL(dB SPL)</param>
public sealed record SpeakerSplResult(
    double NominalSplDb,
    double InputPowerW,
    double DistanceM,
    SpeakerSourceType SourceType,
    double OutputSplDb);
