using System.ComponentModel;
using System.Globalization;
using System.Net.Http.Headers;
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
    private const string IdDescription = "패키지 ID(필수, 예: Newtonsoft.Json). 문자(유니코드 포함)·숫자·_와 그 사이의 . - 만 허용, 최대 100자";

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

    /// <summary>패키지 메타데이터를 조회한다.</summary>
    [McpServerTool(Name = "nuget_get_package_metadata", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("지정한 NuGet 피드의 registration에서 패키지 메타데이터를 조회한다. version이 없으면 {id, versions[{version, listed, published, deprecated, vulnerable}]} 요약, "
        + "있으면 그 버전의 {id, version, listed, published, projectUrl, dependencyGroups, deprecated, deprecation, vulnerable, vulnerabilities} 상세를 반환한다. "
        + "deprecated·vulnerable·vulnerabilities가 null이면 피드가 취약점·deprecation 정보를 주지 않는다는 뜻이다(사설 피드). "
        + "RegistrationsBaseUrl/3.6.0이 없는 피드는 SemVer 2.0.0 버전이 빠질 수 있다.")]
    public async Task<string> GetPackageMetadata(
        [Description(InstanceDescription)] string? instance = null,
        [Description(IdDescription)] string? id = null,
        [Description("버전(선택, 예: 1.2.3). 없으면 전체 버전 요약")] string? version = null,
        CancellationToken cancellationToken = default)
    {
        NuGetInstance target = _client.Resolve(instance);
        string packageId = RequireId(id);
        string? normalized = string.IsNullOrWhiteSpace(version) ? null : RequireVersion(version);
        (List<JsonObject> entries, Uri indexUri) = await ReadCatalogEntriesAsync(target, packageId, cancellationToken).ConfigureAwait(false);
        bool provides = await _client.ProvidesVulnerabilityInfoAsync(target, cancellationToken).ConfigureAwait(false);

        if (normalized is null)
        {
            JsonArray versions = [];
            foreach (JsonObject entry in entries)
            {
                Safety safety = SafetyOf(entry, provides);
                versions.Add(new JsonObject
                {
                    ["version"] = Required(target, indexUri, entry, "version"),
                    ["listed"] = Json.Bool(Json.Prop(entry, "listed")) ?? true,
                    ["published"] = Published(entry),
                    ["deprecated"] = safety.Deprecated,
                    ["vulnerable"] = safety.Vulnerable,
                });
            }
            return new JsonObject { ["id"] = packageId, ["versions"] = versions }.ToJsonString();
        }

        JsonObject found = Find(target, indexUri, entries, packageId, normalized);
        return Detail(target, indexUri, packageId, found, provides).ToJsonString();
    }

    /// <summary>특정 버전의 취약점·deprecation을 확인한다.</summary>
    [McpServerTool(Name = "nuget_check_vulnerabilities", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("지정한 NuGet 피드의 registration 정보로 특정 버전의 알려진 취약점과 deprecation을 확인하고 {id, version, vulnerable, vulnerabilities[{severity, advisoryUrl}], deprecated}를 반환한다. "
        + "severity는 low·moderate·high·critical이다. 값이 null이면 피드가 취약점·deprecation 정보를 주지 않는다는 뜻이다(사설 피드).")]
    public async Task<string> CheckVulnerabilities(
        [Description(InstanceDescription)] string? instance = null,
        [Description(IdDescription)] string? id = null,
        [Description("버전(필수, 예: 1.2.3)")] string? version = null,
        CancellationToken cancellationToken = default)
    {
        NuGetInstance target = _client.Resolve(instance);
        string packageId = RequireId(id);
        string normalized = RequireVersion(version);
        (List<JsonObject> entries, Uri indexUri) = await ReadCatalogEntriesAsync(target, packageId, cancellationToken).ConfigureAwait(false);
        bool provides = await _client.ProvidesVulnerabilityInfoAsync(target, cancellationToken).ConfigureAwait(false);

        JsonObject found = Find(target, indexUri, entries, packageId, normalized);
        Safety safety = SafetyOf(found, provides);
        return new JsonObject
        {
            ["id"] = packageId,
            ["version"] = normalized,
            ["vulnerable"] = safety.Vulnerable,
            ["vulnerabilities"] = safety.Vulnerabilities,
            ["deprecated"] = safety.Deprecated,
        }.ToJsonString();
    }

    /// <summary>패키지를 올린다.</summary>
    [McpServerTool(Name = "nuget_push_package", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = true)]
    [Description("지정한 NuGet 피드에 .nupkg를 올리고 {pushed{id, version}, skipped}를 반환한다. path(MCP 서버 호스트의 절대 로컬 경로, UNC·장치 경로 불가)와 contentBase64 중 정확히 하나를 준다. "
        + "패키지 상한은 250MB, .snupkg는 지원하지 않는다. 이미 있으면 HTTP 409 오류이며 skipDuplicate=true면 skipped=true로 성공 처리한다. 인스턴스에 ApiKey가 필요하다.")]
    public Task<string> PushPackage(
        [Description(InstanceDescription)] string? instance = null,
        [Description("MCP 서버 호스트의 .nupkg 절대 경로(path·contentBase64 중 하나)")] string? path = null,
        [Description(".nupkg 내용 Base64(path·contentBase64 중 하나)")] string? contentBase64 = null,
        [Description("이미 있는 버전(HTTP 409)을 성공으로 볼지 여부(기본 false)")] bool skipDuplicate = false,
        CancellationToken cancellationToken = default)
        => PushAsync(instance, path, contentBase64, skipDuplicate, symbols: false, cancellationToken);

    /// <summary>심볼 패키지를 올린다.</summary>
    [McpServerTool(Name = "nuget_push_symbol_package", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = true)]
    [Description("지정한 NuGet 피드의 심볼 리소스(SymbolPackagePublish/4.9.0)에 .snupkg를 올리고 {pushed{id, version}, skipped}를 반환한다. path(MCP 서버 호스트의 절대 로컬 경로, UNC·장치 경로 불가)와 contentBase64 중 정확히 하나를 준다. "
        + "패키지 상한은 250MB이고 .nuspec에 SymbolsPackage packageType이 있어야 한다. 대응하는 .nupkg를 먼저 올린다. 같은 버전은 다시 올릴 수 있고, HTTP 409(이전 심볼이 아직 사용 가능하지 않음)는 오류이며 skipDuplicate=true면 skipped=true로 성공 처리한다. "
        + "피드에 심볼 리소스가 없으면 오류다. 인스턴스에 ApiKey가 필요하다.")]
    public Task<string> PushSymbolPackage(
        [Description(InstanceDescription)] string? instance = null,
        [Description("MCP 서버 호스트의 .snupkg 절대 경로(path·contentBase64 중 하나)")] string? path = null,
        [Description(".snupkg 내용 Base64(path·contentBase64 중 하나)")] string? contentBase64 = null,
        [Description("HTTP 409(이전 심볼 검증 중)를 성공으로 볼지 여부(기본 false)")] bool skipDuplicate = false,
        CancellationToken cancellationToken = default)
        => PushAsync(instance, path, contentBase64, skipDuplicate, symbols: true, cancellationToken);

    private async Task<string> PushAsync(string? instance, string? path, string? contentBase64, bool skipDuplicate, bool symbols, CancellationToken cancellationToken)
    {
        NuGetInstance target = RequireWritable(instance);
        if (string.IsNullOrWhiteSpace(path) == string.IsNullOrWhiteSpace(contentBase64))
            throw new McpException("Specify exactly one of path or contentBase64.");
        byte[] bytes = !string.IsNullOrWhiteSpace(path)
            ? await NuGetPackageFile.ReadAsync(path.Trim(), cancellationToken, symbols).ConfigureAwait(false)
            : NuGetPackageFile.Decode(contentBase64!);
        (string id, string version) = NuGetPackageFile.ReadIdentity(bytes, symbols);

        Uri publish = await _client.GetResourceAsync(target, symbols ? NuGetResource.SymbolPublish : NuGetResource.Publish, cancellationToken).ConfigureAwait(false);
        using MultipartFormDataContent form = new();
        ByteArrayContent file = new(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "package", symbols ? "package.snupkg" : "package.nupkg");
        (int status, string body, string? reason) = await _client.SendPublishAsync(target, HttpMethod.Put, publish, form, cancellationToken).ConfigureAwait(false);
        if (status == 409 && !skipDuplicate)
        {
            string why = NuGetFeedClient.Summarize(body) is { Length: > 0 } s ? s : reason ?? "Conflict";
            throw new McpException(symbols
                ? $"NuGet '{target.Name}' symbol package '{id}' {version} was pushed but is not available yet (HTTP 409): {why}. Use skipDuplicate to ignore."
                : $"NuGet '{target.Name}' package '{id}' {version} already exists (HTTP 409): {why}. Use skipDuplicate to ignore.");
        }
        return new JsonObject
        {
            ["pushed"] = new JsonObject { ["id"] = id, ["version"] = version },
            ["skipped"] = status == 409,
        }.ToJsonString();
    }

    /// <summary>버전을 unlist한다.</summary>
    [McpServerTool(Name = "nuget_unlist_package", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
    [Description("지정한 NuGet 피드에서 패키지 버전을 unlist(DELETE)하고 {unlisted{id, version}}를 반환한다. nuget.org에서는 검색에서만 숨겨지고 nuget_relist_package로 되돌릴 수 있지만, "
        + "일부 사설 피드(BaGet, Nexus 설정 등)는 버전을 실제로 삭제할 수 있다. 인스턴스에 ApiKey가 필요하다.")]
    public Task<string> UnlistPackage(
        [Description(InstanceDescription)] string? instance = null,
        [Description(IdDescription)] string? id = null,
        [Description("버전(필수, 예: 1.2.3)")] string? version = null,
        CancellationToken cancellationToken = default)
        => ChangeListingAsync(instance, id, version, HttpMethod.Delete, "unlisted", cancellationToken);

    /// <summary>unlist한 버전을 다시 listed로 바꾼다.</summary>
    [McpServerTool(Name = "nuget_relist_package", ReadOnly = false, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("지정한 NuGet 피드에서 unlist한 패키지 버전을 relist(POST)하고 {relisted{id, version}}를 반환한다. 인스턴스에 ApiKey가 필요하다.")]
    public Task<string> RelistPackage(
        [Description(InstanceDescription)] string? instance = null,
        [Description(IdDescription)] string? id = null,
        [Description("버전(필수, 예: 1.2.3)")] string? version = null,
        CancellationToken cancellationToken = default)
        => ChangeListingAsync(instance, id, version, HttpMethod.Post, "relisted", cancellationToken);

    private async Task<string> ChangeListingAsync(string? instance, string? id, string? version, HttpMethod method, string key, CancellationToken cancellationToken)
    {
        NuGetInstance target = RequireWritable(instance);
        string packageId = RequireId(id);
        string normalized = RequireVersion(version);
        Uri publish = await _client.GetResourceAsync(target, NuGetResource.Publish, cancellationToken).ConfigureAwait(false);
        Uri uri = NuGetFeedClient.Child(publish, $"{Uri.EscapeDataString(packageId)}/{Uri.EscapeDataString(normalized)}");
        (int status, string body, string? reason) = await _client.SendPublishAsync(target, method, uri, null, cancellationToken).ConfigureAwait(false);
        if (status == 409)
            throw NuGetFeedClient.Failure(target, method, uri, 409, body, reason, null, publish: true);
        return new JsonObject { [key] = new JsonObject { ["id"] = packageId, ["version"] = normalized } }.ToJsonString();
    }

    // ApiKey가 없으면 service index를 읽기 전에 실패
    private NuGetInstance RequireWritable(string? instance)
    {
        NuGetInstance target = _client.Resolve(instance);
        return target.ApiKey is null ? throw new McpException($"NuGet '{target.Name}' has no ApiKey; write tools are unavailable.") : target;
    }

    // registration index → page(items 없으면 page @id GET) → leaf.catalogEntry
    private async Task<(List<JsonObject> Entries, Uri IndexUri)> ReadCatalogEntriesAsync(NuGetInstance target, string id, CancellationToken cancellationToken)
    {
        Uri reg = await _client.GetResourceAsync(target, NuGetResource.Registrations, cancellationToken).ConfigureAwait(false);
        Uri indexUri = NuGetFeedClient.Child(reg, $"{Uri.EscapeDataString(id.ToLowerInvariant())}/index.json");
        JsonNode index = await _client.GetJsonAsync(target, indexUri, cancellationToken).ConfigureAwait(false)
            ?? throw new McpException($"NuGet '{target.Name}' package '{id}' was not found.");
        if (Json.Prop(index, "items") is not JsonArray pages)
            throw NuGetFeedClient.Unexpected(target, indexUri, "items array is missing");

        List<JsonObject> entries = [];
        foreach (JsonNode? page in pages)
        {
            if (page is not JsonObject)
                throw NuGetFeedClient.Unexpected(target, indexUri, "registration page must be an object");
            Uri source = indexUri;
            if (Json.Prop(page, "items") is not JsonArray items)
            {
                if (!Uri.TryCreate(Json.String(Json.Prop(page, "@id")), UriKind.Absolute, out Uri? pageUri) || pageUri.Scheme is not ("http" or "https"))
                    throw NuGetFeedClient.Unexpected(target, indexUri, "registration page @id is missing or not an absolute http(s) URI");
                JsonNode pageRoot = await _client.GetJsonAsync(target, pageUri, cancellationToken).ConfigureAwait(false)
                    ?? throw new McpException($"NuGet '{target.Name}' registration page for '{id}' was not found.");
                items = Json.Prop(pageRoot, "items") as JsonArray ?? throw NuGetFeedClient.Unexpected(target, pageUri, "items array is missing");
                source = pageUri;
            }
            foreach (JsonNode? leaf in items)
                entries.Add(Json.Prop(leaf, "catalogEntry") as JsonObject ?? throw NuGetFeedClient.Unexpected(target, source, "catalogEntry is missing"));
        }
        return (entries, indexUri);
    }

    // catalogEntry.version은 필수. 파싱 불가 값은 비교에서 제외
    private static JsonObject Find(NuGetInstance target, Uri indexUri, List<JsonObject> entries, string id, string normalized)
    {
        foreach (JsonObject entry in entries)
        {
            string raw = Required(target, indexUri, entry, "version");
            if (NuGetVersion.TryParse(raw, out NuGetVersion? v) && v.ToNormalizedString().Equals(normalized, StringComparison.OrdinalIgnoreCase))
                return entry;
        }
        throw new McpException($"NuGet '{target.Name}' package '{id}' version '{normalized}' was not found.");
    }

    // 결과 노드는 모두 새로 만든다(피드 노드를 옮기지 않음)
    private static JsonObject Detail(NuGetInstance target, Uri indexUri, string id, JsonObject entry, bool provides)
    {
        Safety safety = SafetyOf(entry, provides);
        JsonArray groups = [];
        if (Json.Prop(entry, "dependencyGroups") is JsonArray dependencyGroups)
        {
            foreach (JsonNode? group in dependencyGroups)
            {
                JsonArray dependencies = [];
                if (Json.Prop(group, "dependencies") is JsonArray deps)
                    foreach (JsonNode? dep in deps)
                        dependencies.Add(new JsonObject { ["id"] = Json.String(Json.Prop(dep, "id")), ["range"] = Json.String(Json.Prop(dep, "range")) });
                groups.Add(new JsonObject { ["targetFramework"] = Json.String(Json.Prop(group, "targetFramework")), ["dependencies"] = dependencies });
            }
        }

        JsonObject? deprecation = null;
        if (Json.Prop(entry, "deprecation") is JsonObject dep2)
        {
            JsonArray reasons = [];
            if (Json.Prop(dep2, "reasons") is JsonArray rs)
                foreach (JsonNode? r in rs)
                    if (Json.String(r) is { } reason)
                        reasons.Add(reason);
            JsonNode? alt = Json.Prop(dep2, "alternatePackage");
            deprecation = new JsonObject
            {
                ["reasons"] = reasons,
                ["message"] = Json.String(Json.Prop(dep2, "message")),
                ["alternatePackage"] = alt is JsonObject ? new JsonObject { ["id"] = Json.String(Json.Prop(alt, "id")), ["range"] = Json.String(Json.Prop(alt, "range")) } : null,
            };
        }

        string raw = Required(target, indexUri, entry, "version");
        return new JsonObject
        {
            ["id"] = id,
            ["version"] = NuGetVersion.TryParse(raw, out NuGetVersion? v) ? v.ToNormalizedString() : raw,
            ["listed"] = Json.Bool(Json.Prop(entry, "listed")) ?? true,
            ["published"] = Published(entry),
            ["projectUrl"] = Json.String(Json.Prop(entry, "projectUrl")),
            ["dependencyGroups"] = groups,
            ["deprecated"] = safety.Deprecated,
            ["deprecation"] = deprecation,
            ["vulnerable"] = safety.Vulnerable,
            ["vulnerabilities"] = safety.Vulnerabilities,
        };
    }

    private sealed record Safety(bool? Deprecated, bool? Vulnerable, JsonArray? Vulnerabilities);

    // 정보 제공 피드(VulnerabilityInfo 리소스)는 필드가 없으면 false·빈 배열, 그 밖은 null(정보 없음)
    private static Safety SafetyOf(JsonObject entry, bool provides)
    {
        JsonArray? vulnerabilities = null;
        if (Json.Prop(entry, "vulnerabilities") is JsonArray va)
        {
            vulnerabilities = [];
            foreach (JsonNode? x in va)
                vulnerabilities.Add(new JsonObject { ["severity"] = Severity(Json.String(Json.Prop(x, "severity"))), ["advisoryUrl"] = Json.String(Json.Prop(x, "advisoryUrl")) });
        }
        else if (provides)
        {
            vulnerabilities = [];
        }
        bool? deprecated = Json.Prop(entry, "deprecation") is JsonObject ? true : provides ? false : null;
        return new Safety(deprecated, vulnerabilities is null ? null : vulnerabilities.Count > 0, vulnerabilities);
    }

    internal static string? Severity(string? raw) => raw switch
    {
        "0" => "low",
        "1" => "moderate",
        "2" => "high",
        "3" => "critical",
        _ => raw,
    };

    // nuget.org는 unlisted 버전의 published를 1900년으로 둔다
    private static string? Published(JsonObject entry)
    {
        string? raw = Json.String(Json.Prop(entry, "published"));
        return raw is not null && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset p) && p.Year == 1900 ? null : raw;
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
