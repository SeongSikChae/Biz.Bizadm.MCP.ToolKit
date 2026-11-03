using System.Text.Json.Serialization;

namespace Biz.Bizadm.MCP.ToolKit.Models.Speaker;

/// <summary>스피커 음원 유형</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SpeakerSourceType>))]
public enum SpeakerSourceType
{
    /// <summary>점음원, 거리 2배당 −6dB (역제곱 법칙)</summary>
    Point,

    /// <summary>선음원(라인어레이), 거리 2배당 −3dB</summary>
    Line,
}
