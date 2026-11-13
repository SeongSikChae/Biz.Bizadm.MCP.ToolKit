using System.Net;
using System.Text;

namespace Biz.Bizadm.MCP.ToolKit.Tests.NuGet;

/// <summary>요청을 기록하고 정해진 응답을 차례로 돌려주는 테스트용 핸들러. 비동기 응답(게이트·지연)과 동시 호출을 지원한다.</summary>
internal sealed class NuGetRecordingHandler : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly Queue<Func<CancellationToken, Task<HttpResponseMessage>>> _responses = new();
    private readonly List<(HttpMethod Method, Uri Uri, string? Body, Dictionary<string, string> Headers)> _requests = [];

    public IReadOnlyList<(HttpMethod Method, Uri Uri, string? Body, Dictionary<string, string> Headers)> Requests
    {
        get { lock (_gate) return [.. _requests]; }
    }

    public (HttpMethod Method, Uri Uri, string? Body, Dictionary<string, string> Headers) Last => Requests[^1];

    public static HttpResponseMessage Response(HttpStatusCode status, string body = "", string mediaType = "application/json")
        => new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    public void Enqueue(HttpStatusCode status, string body = "", string mediaType = "application/json", Action<HttpResponseMessage>? configure = null)
        => Add(_ =>
        {
            HttpResponseMessage response = Response(status, body, mediaType);
            configure?.Invoke(response);
            return Task.FromResult(response);
        });

    public void Enqueue(Func<HttpResponseMessage> response) => Add(_ => Task.FromResult(response()));

    public void EnqueueException(Exception exception) => Add(_ => Task.FromException<HttpResponseMessage>(exception));

    /// <summary>테스트가 <see cref="TaskCompletionSource{TResult}.SetResult"/>로 완료 시점을 정하는 응답</summary>
    public TaskCompletionSource<HttpResponseMessage> EnqueueGate()
    {
        TaskCompletionSource<HttpResponseMessage> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Add(ct => tcs.Task.WaitAsync(ct));
        return tcs;
    }

    /// <summary>지연 뒤 200 <c>{}</c>. 요청 토큰이 취소되면 <see cref="OperationCanceledException"/></summary>
    public void EnqueueDelay(TimeSpan delay)
        => Add(async ct =>
        {
            await Task.Delay(delay, ct);
            return Response(HttpStatusCode.OK, "{}");
        });

    private void Add(Func<CancellationToken, Task<HttpResponseMessage>> response)
    {
        lock (_gate) _responses.Enqueue(response);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Dictionary<string, string> headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value), StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, IEnumerable<string>> h in request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            headers[h.Key] = string.Join(", ", h.Value);

        Func<CancellationToken, Task<HttpResponseMessage>>? next;
        lock (_gate)
        {
            _requests.Add((request.Method, request.RequestUri!, body, headers));
            _responses.TryDequeue(out next);
        }
        HttpResponseMessage response = next is null ? Response(HttpStatusCode.OK, "{}") : await next(cancellationToken);
        response.RequestMessage ??= request;
        return response;
    }
}
