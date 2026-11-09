using System.Net;
using System.Text;

namespace Biz.Bizadm.MCP.ToolKit.Tests.GitHub;

/// <summary>요청을 기록하고 정해진 응답을 차례로 돌려주는 테스트용 핸들러</summary>
internal sealed class GitHubRecordingHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses = new();

    public List<(HttpMethod Method, Uri Uri, string? Body, Dictionary<string, string> Headers)> Requests { get; } = [];

    public (HttpMethod Method, Uri Uri, string? Body, Dictionary<string, string> Headers) Last => Requests[^1];

    public void Enqueue(HttpStatusCode status, string body = "", string mediaType = "application/json")
        => _responses.Enqueue(() => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) });

    public void EnqueueException(Exception exception) => _responses.Enqueue(() => throw exception);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Dictionary<string, string> headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value), StringComparer.OrdinalIgnoreCase);
        Requests.Add((request.Method, request.RequestUri!, body, headers));
        return _responses.Count > 0 ? _responses.Dequeue()() : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
    }
}
