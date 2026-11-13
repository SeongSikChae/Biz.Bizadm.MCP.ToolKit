using System.ComponentModel;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Biz.Bizadm.MCP.ToolKit.NuGet;
using global::NuGet.Versioning;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Biz.Bizadm.MCP.ToolKit.Tools.NuGet;

/// <summary>NuGet Server API v3로 패키지를 조회하고 push·unlist·relist하는 Tool</summary>
[McpServerToolType]
public sealed class NuGetTools
{
    private const string InstanceDescription = "NuGet 인스턴스 이름(필수). nuget_list_instances로 확인";
    private const string IdDescription = "패키지 ID(필수, 예: Newtonsoft.Json). 영문·숫자·_와 사이의 . - 만 허용, 최대 100자";

    private static readonly Regex PackageId = new(@"^\w+([.-]\w+)*$", RegexOptions.CultureInvariant);

    private readonly NuGetFeedClient _client;

    /// <summary>DI에 등록한 NuGet client로 Tool을 만든다. <see cref="NuGetMcpServerBuilderExtensions.WithNuGetTools"/>로 등록해야 한다.</summary>
    /// <param name="services">서비스 공급자</param>
    public NuGetTools(IServiceProvider services)
        : this(services.GetRequiredService<NuGetFeedClient>())
    {
    }

    internal NuGetTools(NuGetFeedClient client)
    {
        _client = client;
    }

    /// <summary>등록한 NuGet 인스턴스 목록을 조회한다.</summary>
    [McpServerTool(Name = "nuget_list_instances", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("등록한 NuGet 피드 인스턴스의 이름, 용도 설명, service index 주소, 쓰기 가능 여부(canWrite = ApiKey 설정 여부)를 조회한다. 다른 nuget_* Tool의 instance 인자로 이름을 쓴다.")]
    public string ListInstances() => new JsonArray([.. _client.Instances.Select(i => (JsonNode)new JsonObject
    {
        ["name"] = i.Name,
        ["description"] = i.Description,
        ["serviceIndex"] = i.ServiceIndex,
        ["canWrite"] = i.ApiKey is not null,
    })]).ToJsonString();

    /// <summary>패키지를 검색한다.</summary>
    [McpServerTool(Name = "nuget_search_packages", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("지정한 NuGet 피드에서 패키지를 검색하고 {totalHits, packages[{id, version, description, totalDownloads, verified, versionCount}]}를 반환한다. "
        + "description은 앞 300자다. packageType 필터는 SearchQueryService/3.5.0을 제공하는 피드만 지원하며 다른 피드는 무시할 수 있다. 피드가 주지 않는 필드는 null(수치는 0)이다.")]
    public async Task<string> SearchPackages(
        [Description(InstanceDescription)] string? instance = null,
        [Description("검색어(선택). 비우면 전체")] string? query = null,
        [Description("prerelease 포함 여부(기본 false)")] bool prerelease = false,
        [Description("패키지 타입 필터(선택, 예: Dependency, DotnetTool)")] string? packageType = null,
        [Description("건너뛸 결과 수(기본 0)")] int skip = 0,
        [Description("가져올 결과 수(기본 20, 1~100)")] int take = 20,
        CancellationToken cancellationToken = default)
    {
        NuGetInstance target = _client.Resolve(instance);
        if (skip < 0 || take is < 1 or > 100)
            throw new McpException("skip must be >= 0 and take must be 1..100.");
        Uri search = await _client.GetResourceAsync(target, NuGetResource.Search, cancellationToken).ConfigureAwait(false);

        UriBuilder builder = new(search);
        string q = $"q={Uri.EscapeDataString(query?.Trim() ?? string.Empty)}&skip={skip}&take={take}&prerelease={(prerelease ? "true" : "false")}&semVerLevel=2.0.0"
            + (string.IsNullOrWhiteSpace(packageType) ? string.Empty : "&packageType=" + Uri.EscapeDataString(packageType.Trim()));
        builder.Query = string.IsNullOrEmpty(builder.Query) ? q : builder.Query.TrimStart('?') + "&" + q;

        JsonNode? root = await _client.GetJsonAsync(target, builder.Uri, cancellationToken, notFoundAsNull: false).ConfigureAwait(false);
        if (Json.Prop(root, "data") is not JsonArray data)
            throw NuGetFeedClient.Unexpected(target, search, "data array is missing");

        JsonArray packages = [];
        foreach (JsonNode? p in data)
        {
            packages.Add(new JsonObject
            {
                ["id"] = Required(target, search, p, "id"),
                ["version"] = Required(target, search, p, "version"),
                ["description"] = Truncate(Json.String(Json.Prop(p, "description")), 300),
                ["totalDownloads"] = Json.Long(Json.Prop(p, "totalDownloads")) ?? 0,
                ["verified"] = Json.Bool(Json.Prop(p, "verified")),
                ["versionCount"] = (Json.Prop(p, "versions") as JsonArray)?.Count ?? 0,
            });
        }
        return new JsonObject { ["totalHits"] = Json.Long(Json.Prop(root, "totalHits")) ?? 0, ["packages"] = packages }.ToJsonString();
    }

    /// <summary>패키지의 버전 목록을 조회한다.</summary>
    [McpServerTool(Name = "nuget_list_versions", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("지정한 NuGet 피드에서 패키지의 버전 목록을 오름차순으로 조회하고 {id, versions}를 반환한다. unlisted 버전도 포함하므로 listed 여부는 nuget_get_package_metadata로 확인한다.")]
    public async Task<string> ListVersions(
        [Description(InstanceDescription)] string? instance = null,
        [Description(IdDescription)] string? id = null,
        [Description("prerelease 버전 포함 여부(기본 true)")] bool prerelease = true,
        CancellationToken cancellationToken = default)
    {
        NuGetInstance target = _client.Resolve(instance);
        string packageId = RequireId(id);
        Uri baseAddress = await _client.GetResourceAsync(target, NuGetResource.PackageBase, cancellationToken).ConfigureAwait(false);
        Uri uri = NuGetFeedClient.Child(baseAddress, $"{Uri.EscapeDataString(packageId.ToLowerInvariant())}/index.json");
        JsonNode root = await _client.GetJsonAsync(target, uri, cancellationToken).ConfigureAwait(false)
            ?? throw new McpException($"NuGet '{target.Name}' package '{packageId}' was not found.");
        if (Json.Prop(root, "versions") is not JsonArray raw)
            throw NuGetFeedClient.Unexpected(target, uri, "versions array is missing");

        List<(string Text, NuGetVersion? Parsed)> versions = [];
        foreach (JsonNode? v in raw)
        {
            string text = Json.String(v) ?? throw NuGetFeedClient.Unexpected(target, uri, "versions must contain strings");
            versions.Add((text, NuGetVersion.TryParse(text, out NuGetVersion? parsed) ? parsed : null));
        }
        IEnumerable<string> ordered = versions
            .Where(v => v.Parsed is not null && (prerelease || !v.Parsed.IsPrerelease))
            .OrderBy(v => v.Parsed)
            .Select(v => v.Text)
            .Concat(versions.Where(v => v.Parsed is null).Select(v => v.Text)); // 파싱 불가 원문은 끝에
        return new JsonObject { ["id"] = packageId, ["versions"] = new JsonArray([.. ordered.Select(v => (JsonNode)v)]) }.ToJsonString();
    }

    /// <summary>패키지 ID 인자를 검사한다(NuGet ID 규칙, 최대 100자).</summary>
    internal static string RequireId(string? id)
    {
        string value = id?.Trim() ?? string.Empty;
        if (value.Length == 0)
            throw new McpException("id is required.");
        if (value.Length > 100 || !PackageId.IsMatch(value))
            throw new McpException($"id '{value}' is not a valid NuGet package id.");
        return value;
    }

    /// <summary>버전 인자를 정규화한다.</summary>
    internal static string RequireVersion(string? version)
    {
        string value = version?.Trim() ?? string.Empty;
        if (value.Length == 0)
            throw new McpException("version is required.");
        return NuGetVersion.TryParse(value, out NuGetVersion? parsed)
            ? parsed.ToNormalizedString()
            : throw new McpException($"version '{value}' is not a valid NuGet version.");
    }

    private static string Required(NuGetInstance target, Uri uri, JsonNode? node, string name)
        => Json.String(Json.Prop(node, name)) ?? throw NuGetFeedClient.Unexpected(target, uri, $"{name} is missing or not a string");

    private static string? Truncate(string? value, int max) => value is not null && value.Length > max ? value[..max] : value;
}
