using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.NuGet;

/// <summary>등록한 NuGet 피드의 NuGet Server API v3를 호출한다.</summary>
/// <remarks>
/// Basic은 ServiceIndex와 같은 origin 요청에만, ApiKey는 PackagePublish·SymbolPackagePublish 요청에만 붙인다. 자동 리다이렉트는 따라가지 않는다.
/// 오류는 자격 증명 없이 인스턴스 이름·메서드·URL(사용자 정보·query 제외)·HTTP 상태를 담은 <see cref="McpException"/>으로 던진다.
/// </remarks>
internal sealed class NuGetFeedClient : IDisposable
{
    internal const long DefaultMaxResponseBytes = 50L * 1024 * 1024;
    private const int MaxErrorBodyLength = 200;

    private static readonly string UserAgent = $"Biz.Bizadm.MCP.ToolKit/{typeof(NuGetFeedClient).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"}";
    private static readonly Regex Spaces = new("\\s+", RegexOptions.CultureInvariant);
    private static readonly JsonDocumentOptions ParseOptions = new() { AllowDuplicateProperties = false };

    private readonly NuGetInstance[] _ordered;
    private readonly Dictionary<string, NuGetInstance> _instances;
    private readonly Dictionary<string, Uri> _origins;
    private readonly ConcurrentDictionary<string, Lazy<Task<NuGetServiceIndex>>> _indexes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _http;
    private readonly TimeSpan _readTimeout;
    private readonly TimeSpan _pushTimeout;
    private readonly long _maxResponseBytes;

    /// <param name="options">검증한 NuGet 옵션</param>
    /// <param name="handler">테스트용 HTTP 처리기. null이면 <see cref="CreateDefaultHandler"/>를 쓴다.</param>
    /// <param name="readTimeout">읽기·unlist·relist 요청 시간 제한(기본 100초)</param>
    /// <param name="pushTimeout">push 요청 시간 제한(기본 10분)</param>
    /// <param name="maxResponseBytes">응답 본문 상한(기본 50MB)</param>
    public NuGetFeedClient(NuGetOptions options, HttpMessageHandler? handler = null, TimeSpan? readTimeout = null,
        TimeSpan? pushTimeout = null, long? maxResponseBytes = null)
    {
        _ordered = [.. options.Instances];
        _instances = _ordered.ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase);
        _origins = _ordered.ToDictionary(i => i.Name, i => new Uri(i.ServiceIndex), StringComparer.OrdinalIgnoreCase);
        _http = new HttpClient(handler ?? CreateDefaultHandler(), disposeHandler: handler is null)
        {
            Timeout = Timeout.InfiniteTimeSpan,
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        _readTimeout = readTimeout ?? TimeSpan.FromSeconds(100);
        _pushTimeout = pushTimeout ?? TimeSpan.FromMinutes(10);
        _maxResponseBytes = maxResponseBytes ?? DefaultMaxResponseBytes;
    }

    /// <summary>기본 처리기: 리다이렉트를 따라가지 않고(<c>X-NuGet-ApiKey</c>가 다른 호스트로 넘어가지 않게), 자동 압축 해제, 연결 수명 제한</summary>
    internal static SocketsHttpHandler CreateDefaultHandler() => new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All,
    };

    /// <summary>등록 순서의 인스턴스 목록</summary>
    public IReadOnlyList<NuGetInstance> Instances => _ordered;

    /// <summary>이름으로 인스턴스를 찾는다.</summary>
    /// <exception cref="McpException">미등록·공백 이름</exception>
    public NuGetInstance Resolve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || !_instances.TryGetValue(name.Trim(), out NuGetInstance? instance))
        {
            string label = string.IsNullOrWhiteSpace(name) ? "instance is required." : $"Unknown NuGet instance '{name.Trim()}'.";
            throw new McpException($"{label} Registered instances: {string.Join(", ", _ordered.Select(i => i.Name))}");
        }
        return instance;
    }

    /// <summary>용도에 맞는 리소스 주소. service index는 인스턴스마다 캐시한다.</summary>
    /// <exception cref="McpException">리소스 없음, https 인스턴스의 http publish·symbol publish 주소, 경로 결합 리소스의 query·fragment</exception>
    public async Task<Uri> GetResourceAsync(NuGetInstance instance, NuGetResource resource, CancellationToken cancellationToken)
    {
        NuGetServiceIndex index = await GetIndexAsync(instance, cancellationToken).ConfigureAwait(false);
        Uri uri = index.Find(resource) ?? throw new McpException($"NuGet '{instance.Name}' service index has no "
            + $"{NuGetServiceIndex.NameOf(resource)} resource ({string.Join(", ", NuGetServiceIndex.TypesOf(resource))}).");
        // scheme 하향 방지: https 인스턴스가 http publish·symbol publish 주소로 ApiKey를 보내지 않게
        if ((resource is NuGetResource.Publish or NuGetResource.SymbolPublish)
            && _origins[instance.Name].Scheme == Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttps)
            throw new McpException($"NuGet '{instance.Name}' {NuGetServiceIndex.NameOf(resource)} resource is not https; ApiKey would be sent in clear text.");
        // 경로 결합(Child)을 쓰는 리소스는 query·fragment가 있으면 주소를 만들 수 없다
        if (resource != NuGetResource.Search && (uri.Query.Length > 0 || uri.Fragment.Length > 0))
            throw Unexpected(instance, _origins[instance.Name], $"{NuGetServiceIndex.NameOf(resource)} @id must not contain query or fragment");
        return uri;
    }

    /// <summary>취약점·deprecation 정보를 주는 피드인지(service index의 <c>VulnerabilityInfo/6.7.0</c> 존재)</summary>
    public async Task<bool> ProvidesVulnerabilityInfoAsync(NuGetInstance instance, CancellationToken cancellationToken)
        => (await GetIndexAsync(instance, cancellationToken).ConfigureAwait(false)).ProvidesVulnerabilityInfo;

    private async Task<NuGetServiceIndex> GetIndexAsync(NuGetInstance instance, CancellationToken cancellationToken)
    {
        Lazy<Task<NuGetServiceIndex>> entry = _indexes.GetOrAdd(instance.Name, _ => new(() => LoadIndexAsync(instance)));
        // 앞선 호출자가 모두 취소한 뒤 실패한 entry는 아무도 지우지 않았을 수 있다: 지우고 새로 읽는다
        if (entry.IsValueCreated && entry.Value.IsFaulted)
        {
            _indexes.TryRemove(new KeyValuePair<string, Lazy<Task<NuGetServiceIndex>>>(instance.Name, entry));
            entry = _indexes.GetOrAdd(instance.Name, _ => new(() => LoadIndexAsync(instance)));
        }
        try
        {
            return await entry.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // 실패는 캐시하지 않는다(같은 entry일 때만 제거)
            _indexes.TryRemove(new KeyValuePair<string, Lazy<Task<NuGetServiceIndex>>>(instance.Name, entry));
            throw;
        }
    }

    /// <summary>테스트용: 캐시된 service index 로드가 실패로 끝났는지</summary>
    internal bool IsIndexLoadFaulted(string name)
        => _indexes.TryGetValue(name, out Lazy<Task<NuGetServiceIndex>>? entry) && entry.IsValueCreated && entry.Value.IsFaulted;

    // 공유 Task: 호출자 취소를 쓰지 않는다(읽기 시간 제한만)
    private async Task<NuGetServiceIndex> LoadIndexAsync(NuGetInstance instance)
    {
        Uri uri = _origins[instance.Name];
        JsonNode? root = await GetJsonAsync(instance, uri, CancellationToken.None, notFoundAsNull: false).ConfigureAwait(false);
        return NuGetServiceIndex.Parse(root) ?? throw Unexpected(instance, uri, "resources array is missing");
    }

    /// <summary>GET 후 JSON을 읽는다. 404는 <paramref name="notFoundAsNull"/>이면 null, 아니면 실패다.</summary>
    /// <exception cref="McpException">2xx·404 외 응답, 잘못된 JSON, 전송 오류·시간 초과·응답 크기 초과</exception>
    public async Task<JsonNode?> GetJsonAsync(NuGetInstance instance, Uri uri, CancellationToken cancellationToken, bool notFoundAsNull = true)
    {
        (int code, string body, string? reason, Uri? location) = await SendAsync(instance, HttpMethod.Get, uri, null, false, cancellationToken).ConfigureAwait(false);
        if (code == 404 && notFoundAsNull)
            return null;
        if (code is < 200 or >= 300)
            throw Failure(instance, HttpMethod.Get, uri, code, body, reason, location, publish: false);
        try
        {
            return JsonNode.Parse(body, documentOptions: ParseOptions) ?? throw Unexpected(instance, uri, "empty JSON");
        }
        catch (JsonException ex)
        {
            throw new McpException($"NuGet '{instance.Name}' {Display(uri)} returned invalid JSON: {ex.Message}", ex);
        }
    }

    /// <summary>PackagePublish·SymbolPackagePublish 요청(ApiKey 포함). 200·201·202·204·409는 상태·본문·사유를 돌려주고 그 밖은 실패다.</summary>
    public async Task<(int Status, string Body, string? Reason)> SendPublishAsync(NuGetInstance instance, HttpMethod method, Uri uri,
        HttpContent? content, CancellationToken cancellationToken)
    {
        (int code, string body, string? reason, Uri? location) = await SendAsync(instance, method, uri, content, true, cancellationToken).ConfigureAwait(false);
        return code is 200 or 201 or 202 or 204 or 409 ? (code, body, reason)
            : throw Failure(instance, method, uri, code, body, reason, location, publish: true);
    }

    private async Task<(int Status, string Body, string? Reason, Uri? Location)> SendAsync(NuGetInstance instance, HttpMethod method,
        Uri uri, HttpContent? content, bool publish, CancellationToken cancellationToken)
    {
        TimeSpan timeout = method == HttpMethod.Put ? _pushTimeout : _readTimeout;
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        try
        {
            // 헤더 설정도 try 안: 형식 예외도 McpException(값은 메시지에 넣지 않음)
            using HttpRequestMessage request = new(method, uri) { Content = content, Version = HttpVersion.Version11 };
            request.Headers.UserAgent.ParseAdd(UserAgent);
            request.Headers.Add("X-NuGet-Protocol-Version", "4.1.0");
            if (instance.User is not null && SameOrigin(uri, _origins[instance.Name]))
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes($"{instance.User}:{instance.Password}")));
            if (publish)
                request.Headers.Add("X-NuGet-ApiKey", instance.ApiKey);

            using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            string body = await ReadLimitedAsync(response.Content, _maxResponseBytes, cts.Token).ConfigureAwait(false)
                ?? throw new McpException($"NuGet '{instance.Name}' {method} {Display(uri)} response exceeded {_maxResponseBytes} bytes.");
            Uri? location = response.Headers.Location is { } l ? new Uri(uri, l) : null;
            return ((int)response.StatusCode, body, response.ReasonPhrase, location);
        }
        catch (McpException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // 호출자 취소
        }
        catch (OperationCanceledException ex) when (cts.IsCancellationRequested)
        {
            throw new McpException($"NuGet '{instance.Name}' {method} {Display(uri)} timed out after {(int)timeout.TotalSeconds}s.", ex);
        }
        catch (Exception ex)
        {
            // 헤더 FormatException 메시지에는 값이 들어갈 수 있다
            string why = ex is FormatException ? "invalid request header value" : ex.Message;
            throw new McpException($"NuGet '{instance.Name}' {method} {Display(uri)} failed: {why}", ex);
        }
    }

    // Content-Length나 읽은 바이트가 상한을 넘으면 null
    private static async Task<string?> ReadLimitedAsync(HttpContent content, long max, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > max)
            return null;
        using MemoryStream buffer = new();
        Stream stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            byte[] chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > max)
                    return null;
                buffer.Write(chunk, 0, read);
            }
        }
        return Decode(buffer);
    }

    // 자동 압축 해제 뒤 바이트. UTF-8 BOM은 건너뛴다(JsonNode.Parse(string)가 U+FEFF에서 실패)
    private static string Decode(MemoryStream buffer)
    {
        ReadOnlySpan<byte> bytes = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
            bytes = bytes[3..];
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>실패 응답 예외. 3xx면 Location(사용자 정보·query 제외), 아니면 본문 요약 또는 ReasonPhrase. 401·403이면 자격 증명 안내를 붙인다.</summary>
    internal static McpException Failure(NuGetInstance instance, HttpMethod method, Uri uri, int code, string body,
        string? reason, Uri? location, bool publish)
    {
        string summary = code is >= 300 and < 400
            ? $"redirected to {(location is null ? "(none)" : Display(location))}"
            : Summarize(body) is { Length: > 0 } s ? s : reason ?? string.Empty;
        if (code is 401 or 403)
            summary += publish
                ? " (check ApiKey and its package scope; Basic is sent only to the ServiceIndex origin)"
                : " (check User/Password; Basic is sent only to the ServiceIndex origin)";
        return new McpException($"NuGet '{instance.Name}' {method} {Display(uri)} failed (HTTP {code}): {summary}");
    }

    /// <summary>JSON 구조 오류 예외</summary>
    internal static McpException Unexpected(NuGetInstance instance, Uri uri, string detail)
        => new($"NuGet '{instance.Name}' {Display(uri)} returned unexpected JSON: {detail}");

    /// <summary>본문을 공백 정리 후 앞 200자로 줄인다.</summary>
    internal static string Summarize(string body)
    {
        string text = Spaces.Replace(body, " ").Trim();
        return text.Length > MaxErrorBodyLength ? text[..MaxErrorBodyLength] : text;
    }

    /// <summary>메시지용 URL: 사용자 정보·query·fragment 제외</summary>
    internal static string Display(Uri uri)
        => uri.GetComponents(UriComponents.Scheme | UriComponents.Host | UriComponents.Port | UriComponents.Path, UriFormat.UriEscaped);

    internal static bool SameOrigin(Uri a, Uri b)
        => Uri.Compare(a, b, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;

    /// <summary>끝 <c>/</c>를 보정해 상대 경로를 붙인다. <paramref name="parent"/>에는 query·fragment가 없다(<see cref="GetResourceAsync"/>가 보장).</summary>
    internal static Uri Child(Uri parent, string relative)
        => new(parent.AbsoluteUri.EndsWith('/') ? parent : new Uri(parent.AbsoluteUri + "/"), relative);

    public void Dispose() => _http.Dispose();
}
