using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Redmine;

/// <summary>Redmine REST API 호출 클라이언트</summary>
public sealed class RedmineClient
{
    /// <summary>목록 조회 기본 개수</summary>
    public const int DefaultLimit = 25;

    /// <summary>목록 조회 최대 개수</summary>
    public const int MaxLimit = 100;

    internal const string ApiKeyHeaderName = "X-Redmine-API-Key";

    private readonly HttpClient _httpClient;

    /// <summary>옵션으로 HttpClient를 만들어 Redmine에 연결한다.</summary>
    /// <param name="options">Redmine 접속 옵션</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/>가 null이다.</exception>
    public RedmineClient(RedmineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _httpClient = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) });
        Configure(_httpClient, options);
    }

    /// <summary>구성된 HttpClient로 Redmine에 연결한다.</summary>
    /// <param name="httpClient">BaseAddress와 인증 헤더가 설정된 HttpClient</param>
    /// <exception cref="ArgumentNullException"><paramref name="httpClient"/>가 null이다.</exception>
    public RedmineClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
    }

    internal static void Configure(HttpClient client, RedmineOptions options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);

        client.BaseAddress = CreateBaseAddress(options.ApiEndpoint);
        client.DefaultRequestHeaders.Remove(ApiKeyHeaderName);
        if (!client.DefaultRequestHeaders.TryAddWithoutValidation(ApiKeyHeaderName, options.ApiKey))
            throw new InvalidOperationException("Failed to set the Redmine API key header.");

        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    internal static Uri CreateBaseAddress(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("Redmine API endpoint is required.", nameof(endpoint));

        string trimmed = endpoint.Trim();
        if (!trimmed.EndsWith('/'))
            trimmed += "/";

        return new Uri(trimmed, UriKind.Absolute);
    }

    /// <summary>limit을 1~<see cref="MaxLimit"/> 범위로 맞춘다. null이면 <see cref="DefaultLimit"/>이다.</summary>
    /// <param name="limit">요청 개수</param>
    /// <returns>보정한 개수</returns>
    public static int NormalizeLimit(int? limit)
    {
        int value = limit ?? DefaultLimit;
        if (value < 1)
            return 1;
        if (value > MaxLimit)
            return MaxLimit;
        return value;
    }

    /// <summary>offset을 0 이상으로 맞춘다.</summary>
    /// <param name="offset">요청 시작 위치</param>
    /// <returns>보정한 시작 위치</returns>
    public static int NormalizeOffset(int? offset)
    {
        if (offset is null || offset.Value < 0)
            return 0;
        return offset.Value;
    }

    /// <summary>GET 요청을 보내고 응답 본문을 반환한다.</summary>
    /// <param name="path">API 기준 상대 경로</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>응답 본문</returns>
    public Task<string> GetAsync(string path, CancellationToken cancellationToken)
    {
        return SendAsync(HttpMethod.Get, path, query: null, content: null, cancellationToken);
    }

    /// <summary>쿼리와 함께 GET 요청을 보내고 응답 본문을 반환한다. 값이 비어 있는 쿼리는 뺀다.</summary>
    /// <param name="path">API 기준 상대 경로</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <param name="query">쿼리 키·값</param>
    /// <returns>응답 본문</returns>
    public Task<string> GetAsync(string path, CancellationToken cancellationToken, params (string Key, string? Value)[] query)
    {
        return SendAsync(HttpMethod.Get, path, query, content: null, cancellationToken);
    }

    /// <summary>limit·offset을 붙여 목록 GET 요청을 보낸다.</summary>
    /// <param name="path">API 기준 상대 경로</param>
    /// <param name="limit">요청 개수</param>
    /// <param name="offset">시작 위치</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <param name="filters">추가 쿼리 키·값</param>
    /// <returns>응답 본문</returns>
    public Task<string> GetPagedAsync(
        string path,
        int? limit,
        int? offset,
        CancellationToken cancellationToken,
        params (string Key, string? Value)[] filters)
    {
        List<(string Key, string? Value)> query = new(filters);
        query.Add(("limit", NormalizeLimit(limit).ToString(CultureInfo.InvariantCulture)));
        query.Add(("offset", NormalizeOffset(offset).ToString(CultureInfo.InvariantCulture)));
        return SendAsync(HttpMethod.Get, path, query, content: null, cancellationToken);
    }

    /// <summary><c>{wrapper: payload}</c> Body로 POST 요청을 보낸다.</summary>
    /// <param name="path">API 기준 상대 경로</param>
    /// <param name="wrapper">Body 최상위 키</param>
    /// <param name="payload">Body 내용</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>응답 본문</returns>
    public Task<string> PostAsync(string path, string wrapper, JsonObject payload, CancellationToken cancellationToken)
    {
        return SendWrappedAsync(HttpMethod.Post, path, wrapper, payload, cancellationToken);
    }

    /// <summary><c>{wrapper: payload}</c> Body로 PUT 요청을 보낸다.</summary>
    /// <param name="path">API 기준 상대 경로</param>
    /// <param name="wrapper">Body 최상위 키</param>
    /// <param name="payload">Body 내용</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>응답 본문</returns>
    public Task<string> PutAsync(string path, string wrapper, JsonObject payload, CancellationToken cancellationToken)
    {
        return SendWrappedAsync(HttpMethod.Put, path, wrapper, payload, cancellationToken);
    }

    /// <summary>Body 전체를 그대로 PUT 요청으로 보낸다.</summary>
    /// <param name="path">API 기준 상대 경로</param>
    /// <param name="body">Body 전체</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>응답 본문</returns>
    public Task<string> PutBodyAsync(string path, JsonObject body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        return SendAsync(HttpMethod.Put, path, query: null, CreateJsonContent(body), cancellationToken);
    }

    /// <summary>DELETE 요청을 보낸다.</summary>
    /// <param name="path">API 기준 상대 경로</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>응답 본문</returns>
    public Task<string> DeleteAsync(string path, CancellationToken cancellationToken)
    {
        return SendAsync(HttpMethod.Delete, path, query: null, content: null, cancellationToken);
    }

    /// <summary>application/octet-stream Body로 POST 요청을 보낸다.</summary>
    /// <param name="path">API 기준 상대 경로</param>
    /// <param name="body">보낼 내용</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>응답 본문</returns>
    public Task<string> PostOctetStreamAsync(string path, Stream body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        StreamContent content = new(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return SendAsync(HttpMethod.Post, path, query: null, content, cancellationToken);
    }

    /// <summary>첨부 내용을 바이트로 내려받는다. 절대 URL은 API 엔드포인트와 출처가 같아야 한다.</summary>
    /// <param name="url">API 기준 상대 경로 또는 같은 출처 절대 URL</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>응답 바이트</returns>
    /// <exception cref="McpException">출처가 다르다.</exception>
    /// <exception cref="RedmineApiException">응답이 성공 상태가 아니다.</exception>
    public async Task<byte[]> GetBytesAsync(string url, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        Uri requestUri = ResolveDownloadUri(url);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        byte[] bytes = response.Content is null
            ? []
            : await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new RedmineApiException((int)response.StatusCode, Encoding.UTF8.GetString(bytes));

        return bytes;
    }

    private Uri ResolveDownloadUri(string url)
    {
        if (_httpClient.BaseAddress is null)
            throw new McpException("HttpClient BaseAddress is required for attachment download.");

        Uri baseAddress = _httpClient.BaseAddress;
        if (Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? absolute))
        {
            EnsureSameOrigin(baseAddress, absolute);
            return absolute;
        }

        string relative = url.Trim().TrimStart('/');
        return new Uri(baseAddress, relative);
    }

    internal static void EnsureSameOrigin(Uri baseAddress, Uri candidate)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        ArgumentNullException.ThrowIfNull(candidate);

        if (!string.Equals(baseAddress.Scheme, candidate.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(baseAddress.IdnHost, candidate.IdnHost, StringComparison.OrdinalIgnoreCase)
            || baseAddress.Port != candidate.Port)
        {
            throw new McpException(
                $"Attachment URL origin must match the Redmine API endpoint ({baseAddress.GetLeftPart(UriPartial.Authority)}).");
        }
    }

    private Task<string> SendWrappedAsync(
        HttpMethod method,
        string path,
        string wrapper,
        JsonObject payload,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wrapper);
        ArgumentNullException.ThrowIfNull(payload);
        JsonObject body = new()
        {
            [wrapper] = payload
        };
        return SendAsync(method, path, query: null, CreateJsonContent(body), cancellationToken);
    }

    private static StringContent CreateJsonContent(JsonObject body)
    {
        return new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
    }

    private async Task<string> SendAsync(
        HttpMethod method,
        string path,
        IReadOnlyList<(string Key, string? Value)>? query,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(method, BuildRelativeUri(path, query));
        request.Content = content;

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string responseBody = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new RedmineApiException((int)response.StatusCode, responseBody);

        return responseBody;
    }

    internal static string BuildRelativeUri(string path, IReadOnlyList<(string Key, string? Value)>? query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string relative = path.TrimStart('/');
        if (query is null || query.Count == 0)
            return relative;

        List<string> pairs = [];
        foreach ((string key, string? value) in query)
        {
            if (string.IsNullOrEmpty(value))
                continue;

            pairs.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
        }

        if (pairs.Count == 0)
            return relative;

        return relative + "?" + string.Join("&", pairs);
    }
}
