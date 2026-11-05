using System.Net;
using System.Text;

using Biz.Bizadm.MCP.ToolKit.Redmine;

namespace Biz.Bizadm.MCP.ToolKit.Tests.Redmine.V7;

/// <summary>요청을 기록하고 정해진 본문을 돌려주는 테스트용 핸들러</summary>
internal sealed class V7RecordingHandler : HttpMessageHandler
{
    public const string Endpoint = "https://redmine.example/";

    private readonly Queue<string> _responses = new();

    public List<(HttpMethod Method, Uri Uri, string? Body)> Requests { get; } = [];

    public (HttpMethod Method, Uri Uri, string? Body) Last => Requests[^1];

    public void Enqueue(string body) => _responses.Enqueue(body);

    public RedmineClient CreateClient()
    {
        HttpClient httpClient = new(this, disposeHandler: false);
        RedmineClient.Configure(httpClient, new RedmineOptions { ApiEndpoint = Endpoint, ApiKey = "secret-key" });
        return new RedmineClient(httpClient);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.Method, request.RequestUri!, body));
        string response = _responses.Count > 0 ? _responses.Dequeue() : "{}";
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response, Encoding.UTF8, "application/json")
        };
    }
}
