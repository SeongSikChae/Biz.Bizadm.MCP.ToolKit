using System.ComponentModel;
using Biz.Bizadm.MCP.ToolKit.Models.Audio;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Biz.Bizadm.MCP.ToolKit.Tools;

/// <summary>오디오 레벨 단위 환산 Tool</summary>
[McpServerToolType]
public sealed class AudioLevelTools
{
    /// <summary>전압(Vrms)을 dBu로 환산한다.</summary>
    /// <param name="vrms">전압(Vrms)</param>
    /// <param name="reference">0dBu 기준</param>
    /// <returns>입력값, 기준 전압(Vrms)과 레벨(dBu)</returns>
    /// <exception cref="McpException">입력이 허용 범위 밖이거나 유한수가 아니다.</exception>
    [McpServerTool(Name = "convert_vrms_to_dbu", UseStructuredContent = true,
        ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("전압(Vrms)을 dBu로 환산한다. 0dBu 기준은 Nominal(0.775V, 기본) 또는 Exact(√0.6V)이다.")]
    public static VrmsToDbuResult ConvertVrmsToDbu(
        [Description("전압(Vrms)")] double vrms,
        [Description("0dBu 기준: Nominal(0.775V, 기본) 또는 Exact(√0.6V)")] DbuReference reference = DbuReference.Nominal)
    {
        EnsurePositiveFinite(vrms, nameof(vrms));
        double referenceVrms = GetReferenceVrms(reference);
        return new VrmsToDbuResult(vrms, reference, referenceVrms, 20 * Math.Log10(vrms / referenceVrms));
    }

    /// <summary>레벨(dBu)을 전압(Vrms)으로 환산한다.</summary>
    /// <param name="dbu">레벨(dBu)</param>
    /// <param name="reference">0dBu 기준</param>
    /// <returns>입력값, 기준 전압(Vrms)과 전압(Vrms)</returns>
    /// <exception cref="McpException">입력이 허용 범위 밖이거나 유한수가 아니다.</exception>
    [McpServerTool(Name = "convert_dbu_to_vrms", UseStructuredContent = true,
        ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("dBu를 전압(Vrms)으로 환산한다. 0dBu 기준은 Nominal(0.775V, 기본) 또는 Exact(√0.6V)이다.")]
    public static DbuToVrmsResult ConvertDbuToVrms(
        [Description("레벨(dBu)")] double dbu,
        [Description("0dBu 기준: Nominal(0.775V, 기본) 또는 Exact(√0.6V)")] DbuReference reference = DbuReference.Nominal)
    {
        EnsureFinite(dbu, nameof(dbu));
        double referenceVrms = GetReferenceVrms(reference);
        return new DbuToVrmsResult(dbu, reference, referenceVrms, referenceVrms * Math.Pow(10, dbu / 20));
    }

    private static double GetReferenceVrms(DbuReference reference) => reference switch
    {
        DbuReference.Nominal => 0.775,
        DbuReference.Exact => Math.Sqrt(0.6),
        _ => throw new McpException($"{nameof(reference)} must be Nominal or Exact: {reference}"),
    };

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
