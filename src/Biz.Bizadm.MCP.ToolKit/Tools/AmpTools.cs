using System.ComponentModel;
using Biz.Bizadm.MCP.ToolKit.Models.Amp;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Biz.Bizadm.MCP.ToolKit.Tools;

/// <summary>앰프 계산 Tool</summary>
[McpServerToolType]
public sealed class AmpTools
{
    private static readonly double PeakToPeakPerRms = 2 * Math.Sqrt(2);

    /// <summary>정격 출력과 입력 감도로 최대 출력 전압과 전압 이득을 계산한다.</summary>
    /// <param name="impedanceOhm">스피커 임피던스(Ω)</param>
    /// <param name="powerW">정격 출력(W)</param>
    /// <param name="inputSensitivityVrms">입력 감도(Vrms)</param>
    /// <returns>입력값, 입력 감도(Vpp), 최대 출력 전압(Vrms/Vpp), 전압 이득(dB)</returns>
    /// <exception cref="McpException">입력이 0 이하이거나 유한수가 아니다.</exception>
    [McpServerTool(Name = "calculate_amp_max_output", UseStructuredContent = true,
        ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("앰프의 임피던스(Ω), 정격 출력(W), 입력 감도(Vrms)로 입력 감도(Vpp), 최대 출력 전압(Vrms/Vpp), 전압 이득(dB)을 계산한다.")]
    public static AmpMaxOutputResult CalculateMaxOutput(
        [Description("스피커 임피던스(Ω)")] double impedanceOhm,
        [Description("정격 출력(W)")] double powerW,
        [Description("입력 감도(Vrms)")] double inputSensitivityVrms)
    {
        EnsurePositiveFinite(impedanceOhm, nameof(impedanceOhm));
        EnsurePositiveFinite(powerW, nameof(powerW));
        EnsurePositiveFinite(inputSensitivityVrms, nameof(inputSensitivityVrms));

        double maxOutputVrms = Math.Sqrt(powerW * impedanceOhm);
        return new AmpMaxOutputResult(
            impedanceOhm,
            powerW,
            inputSensitivityVrms,
            inputSensitivityVrms * PeakToPeakPerRms,
            maxOutputVrms,
            maxOutputVrms * PeakToPeakPerRms,
            20 * Math.Log10(maxOutputVrms / inputSensitivityVrms));
    }

    private static void EnsurePositiveFinite(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new McpException($"{name} must be a positive finite number: {value}");
    }
}
