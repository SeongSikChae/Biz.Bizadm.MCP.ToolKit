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

    /// <summary>입력 전압과 감쇠, 앰프 이득으로 부하에 걸리는 출력 전압과 전력을 계산한다.</summary>
    /// <param name="inputVrms">입력 전압(Vrms)</param>
    /// <param name="inputAttenuationDb">입력 감쇠(dB), 양수가 감쇠</param>
    /// <param name="impedanceOhm">스피커 임피던스(Ω)</param>
    /// <param name="voltageGainDb">앰프 전압 이득(dB)</param>
    /// <param name="maxOutputVrms">앰프 최대 출력 전압(Vrms)</param>
    /// <returns>입력·실질·출력 전압(Vrms/Vpp), 입력 감쇠(dB), 임피던스, 출력 전력(W)</returns>
    /// <exception cref="McpException">입력이 허용 범위 밖이거나 유한수가 아니다.</exception>
    [McpServerTool(Name = "calculate_amp_load_output", UseStructuredContent = true,
        ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("입력 전압(Vrms), 입력 감쇠(dB), 스피커 임피던스(Ω), 앰프 전압 이득(dB), 최대 출력 전압(Vrms)으로 실질 입력 전압, 출력 전압(Vrms/Vpp), 출력 전력(W)을 계산한다. 출력 전압은 최대 출력 전압으로 제한한다.")]
    public static AmpLoadOutputResult CalculateLoadOutput(
        [Description("입력 전압(Vrms)")] double inputVrms,
        [Description("입력 감쇠(dB), 0 이상, 양수가 감쇠")] double inputAttenuationDb,
        [Description("스피커 임피던스(Ω)")] double impedanceOhm,
        [Description("앰프 전압 이득(dB), calculate_amp_max_output의 voltageGainDb")] double voltageGainDb,
        [Description("앰프 최대 출력 전압(Vrms), calculate_amp_max_output의 maxOutputVrms")] double maxOutputVrms)
    {
        EnsurePositiveFinite(inputVrms, nameof(inputVrms));
        EnsureNonNegativeFinite(inputAttenuationDb, nameof(inputAttenuationDb));
        EnsurePositiveFinite(impedanceOhm, nameof(impedanceOhm));
        EnsureFinite(voltageGainDb, nameof(voltageGainDb));
        EnsurePositiveFinite(maxOutputVrms, nameof(maxOutputVrms));

        double effectiveVrms = inputVrms * Math.Pow(10, -inputAttenuationDb / 20);
        double outputVrms = Math.Min(effectiveVrms * Math.Pow(10, voltageGainDb / 20), maxOutputVrms);
        return new AmpLoadOutputResult(
            inputVrms * PeakToPeakPerRms,
            inputVrms,
            inputAttenuationDb,
            effectiveVrms * PeakToPeakPerRms,
            effectiveVrms,
            impedanceOhm,
            outputVrms * PeakToPeakPerRms,
            outputVrms,
            outputVrms * outputVrms / impedanceOhm);
    }

    private static void EnsurePositiveFinite(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new McpException($"{name} must be a positive finite number: {value}");
    }

    private static void EnsureNonNegativeFinite(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new McpException($"{name} must be a non-negative finite number: {value}");
    }

    private static void EnsureFinite(double value, string name)
    {
        if (!double.IsFinite(value))
            throw new McpException($"{name} must be a finite number: {value}");
    }
}
