using System.Text.Json.Serialization;

namespace Biz.Bizadm.MCP.ToolKit.Models.Audio;

/// <summary>0dBu 기준 전압</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DbuReference>))]
public enum DbuReference
{
    /// <summary>0dBu = 0.775Vrms (실무 관행값)</summary>
    Nominal,

    /// <summary>0dBu = √0.6 ≈ 0.7745967Vrms (1mW @ 600Ω 정의값)</summary>
    Exact,
}
