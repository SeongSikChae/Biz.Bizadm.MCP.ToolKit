using System.Text.Json;
using System.Text.Json.Nodes;

namespace Biz.Bizadm.MCP.ToolKit.NuGet;

/// <summary>Tool이 쓰는 service index 리소스 용도</summary>
internal enum NuGetResource
{
    Search,
    PackageBase,
    Registrations,
    Publish,
}

/// <summary>service index의 <c>resources[]</c>에서 용도별 리소스 주소를 고른다.</summary>
internal sealed class NuGetServiceIndex(IReadOnlyList<(string Type, Uri Id)> resources)
{
    internal const string VulnerabilityInfoType = "VulnerabilityInfo/6.7.0";

    private static readonly Dictionary<NuGetResource, (string Name, string[] Types)> Map = new()
    {
        [NuGetResource.Search] = ("search", ["SearchQueryService/3.5.0", "SearchQueryService/3.0.0-rc", "SearchQueryService/3.0.0-beta", "SearchQueryService"]),
        [NuGetResource.PackageBase] = ("package base address", ["PackageBaseAddress/3.0.0"]),
        [NuGetResource.Registrations] = ("registrations", ["RegistrationsBaseUrl/3.6.0", "RegistrationsBaseUrl/3.4.0",
            "RegistrationsBaseUrl/3.0.0-rc", "RegistrationsBaseUrl/3.0.0-beta", "RegistrationsBaseUrl"]),
        [NuGetResource.Publish] = ("publish", ["PackagePublish/2.0.0"]),
    };

    /// <summary>오류 문구에 쓰는 용도 이름</summary>
    public static string NameOf(NuGetResource resource) => Map[resource].Name;

    /// <summary>용도별 <c>@type</c> 우선순위</summary>
    public static IReadOnlyList<string> TypesOf(NuGetResource resource) => Map[resource].Types;

    /// <summary>취약점·deprecation 정보를 주는 피드인지(<c>VulnerabilityInfo/6.7.0</c> 리소스 존재). 리소스를 호출하지는 않는다.</summary>
    public bool ProvidesVulnerabilityInfo => resources.Any(r => r.Type == VulnerabilityInfoType);

    /// <summary>우선순위가 가장 높은 타입의 첫 리소스 주소. 없으면 null</summary>
    public Uri? Find(NuGetResource resource) => Map[resource].Types
        .Select(t => resources.FirstOrDefault(r => r.Type == t).Id)
        .FirstOrDefault(u => u is not null);

    /// <summary>service index JSON을 읽는다. 루트가 객체가 아니거나 <c>resources</c> 배열이 없으면 null.
    /// <c>@id</c>가 절대 http/https URI가 아닌 항목은 건너뛰고, <c>@type</c>이 문자열 배열이면 원소마다 항목으로 펼친다.</summary>
    public static NuGetServiceIndex? Parse(JsonNode? root)
    {
        if (Json.Prop(root, "resources") is not JsonArray array)
            return null;

        List<(string Type, Uri Id)> list = [];
        foreach (JsonNode? item in array)
        {
            if (Json.String(Json.Prop(item, "@id")) is not { } id
                || !Uri.TryCreate(id, UriKind.Absolute, out Uri? uri)
                || uri.Scheme is not ("http" or "https"))
                continue;

            switch (Json.Prop(item, "@type"))
            {
                case JsonValue value when Json.String(value) is { } type:
                    list.Add((type, uri));
                    break;
                case JsonArray types:
                    foreach (JsonNode? t in types)
                        if (Json.String(t) is { } type)
                            list.Add((type, uri));
                    break;
            }
        }
        return new NuGetServiceIndex(list);
    }
}

/// <summary>피드 JSON을 안전하게 읽는 도우미. 대상이 객체가 아니거나 형식이 다르면 예외 대신 null을 돌려준다.</summary>
internal static class Json
{
    /// <summary>객체의 속성. 객체가 아니면 null(<see cref="JsonNode"/> 인덱서의 <see cref="InvalidOperationException"/> 방지)</summary>
    public static JsonNode? Prop(JsonNode? node, string name) => (node as JsonObject)?[name];

    /// <summary>문자열 값. 문자열이 아니면 null</summary>
    public static string? String(JsonNode? node)
        => node is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

    /// <summary>정수 값. 숫자가 아니거나 범위를 넘으면 null</summary>
    public static long? Long(JsonNode? node)
        => node is JsonValue v && v.GetValueKind() == JsonValueKind.Number && v.TryGetValue(out long l) ? l
            : node is JsonValue d && d.GetValueKind() == JsonValueKind.Number && d.TryGetValue(out double x) && x is >= long.MinValue and <= long.MaxValue ? (long)x
            : null;

    /// <summary>bool 값. bool이 아니면 null</summary>
    public static bool? Bool(JsonNode? node)
        => node is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False ? v.GetValue<bool>() : null;
}
