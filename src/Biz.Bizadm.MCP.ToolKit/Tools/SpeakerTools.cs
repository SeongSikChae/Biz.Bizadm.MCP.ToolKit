using System.ComponentModel;
using Biz.Bizadm.MCP.ToolKit.Models.Speaker;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Biz.Bizadm.MCP.ToolKit.Tools;

/// <summary>스피커 계산 Tool</summary>
[McpServerToolType]
public sealed class SpeakerTools
{
    /// <summary>Nominal SPL과 입력 전력, 거리, 음원 유형으로 해당 거리의 SPL을 계산한다.</summary>
    /// <param name="nominalSplDb">Nominal SPL(dB SPL), 1W/1m 감도</param>
    /// <param name="inputPowerW">입력 전력(W)</param>
    /// <param name="distanceM">거리(m)</param>
    /// <param name="sourceType">음원 유형</param>
    /// <returns>입력값과 Output SPL(dB SPL)</returns>
    /// <exception cref="McpException">입력이 허용 범위 밖이거나 유한수가 아니다.</exception>
    [McpServerTool(Name = "calculate_speaker_spl", UseStructuredContent = true,
        ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("스피커 Nominal SPL(dB SPL, 1W/1m 감도), 입력 전력(W), 거리(m), 음원 유형(Point/Line)으로 해당 거리의 Output SPL(dB SPL)을 계산한다. Point는 거리 2배당 −6dB, Line은 −3dB 감쇠한다.")]
    public static SpeakerSplResult CalculateSpl(
        [Description("Nominal SPL(dB SPL), 1W/1m 감도")] double nominalSplDb,
        [Description("입력 전력(W), calculate_amp_load_output의 outputPowerW")] double inputPowerW,
        [Description("거리(m)")] double distanceM,
        [Description("음원 유형: Point(점음원, 역제곱) 또는 Line(라인어레이)")] SpeakerSourceType sourceType)
    {
        EnsureFinite(nominalSplDb, nameof(nominalSplDb));
        EnsurePositiveFinite(inputPowerW, nameof(inputPowerW));
        EnsurePositiveFinite(distanceM, nameof(distanceM));
        if (!Enum.IsDefined(sourceType))
            throw new McpException($"{nameof(sourceType)} must be Point or Line: {sourceType}");

        double distanceFactor = sourceType == SpeakerSourceType.Line ? 10 : 20;
        return new SpeakerSplResult(
            nominalSplDb,
            inputPowerW,
            distanceM,
            sourceType,
            nominalSplDb + 10 * Math.Log10(inputPowerW) - distanceFactor * Math.Log10(distanceM));
    }

    private static void EnsurePositiveFinite(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new McpException($"{name} must be a positive finite number: {value}");
    }

    private static void EnsureFinite(double value, string name)
    {
        if (!double.IsFinite(value))
            throw new McpException($"{name} must be a finite number: {value}");
    }
}
