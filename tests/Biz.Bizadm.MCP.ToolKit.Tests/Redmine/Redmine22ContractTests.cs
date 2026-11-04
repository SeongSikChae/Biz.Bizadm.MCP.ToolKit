using System.Net;
using System.Reflection;
using System.Text;

using Biz.Bizadm.MCP.ToolKit.Redmine;
using Biz.Bizadm.MCP.ToolKit.Tools.Redmine;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tests.Redmine;

[TestClass]
public sealed class Redmine22ContractTests
{
    [TestMethod]
    public async Task ListTimeEntries_OmitsUserIdAndSendsSupportedFilters()
    {
        using RecordingHandler handler = new();
        using HttpClient httpClient = new(handler, disposeHandler: false);
        RedmineClient.Configure(httpClient, new RedmineOptions { ApiEndpoint = "https://redmine.example/", ApiKey = "secret-key" });
        TimeEntryTools tools = new(new RedmineClient(httpClient));

        string body = await tools.ListTimeEntries(
            projectId: "demo",
            issueId: 12,
            from: "2026-01-01",
            to: "2026-01-31",
            cancellationToken: CancellationToken.None);

        Assert.AreEqual("{}", body);
        Assert.IsNotNull(handler.RequestUri);
        string query = handler.RequestUri.Query;
        StringAssert.Contains(query, "project_id=demo", StringComparison.Ordinal);
        StringAssert.Contains(query, "issue_id=12", StringComparison.Ordinal);
        StringAssert.Contains(query, "from=2026-01-01", StringComparison.Ordinal);
        StringAssert.Contains(query, "to=2026-01-31", StringComparison.Ordinal);
        Assert.IsFalse(query.Contains("user_id", StringComparison.Ordinal));
        Assert.IsFalse(
            typeof(TimeEntryTools).GetMethod(nameof(TimeEntryTools.ListTimeEntries))!.GetParameters()
                .Any(parameter => parameter.Name == "userId"));
    }

    [TestMethod]
    public void ToolDescriptions_StateRedmine22Behavior()
    {
        StringAssert.Contains(Description(typeof(TimeEntryTools), nameof(TimeEntryTools.ListTimeEntries)), "사용자로는 거르지 못한다", StringComparison.Ordinal);
        StringAssert.Contains(Description(typeof(TimeEntryTools), nameof(TimeEntryTools.ListTimeEntries)), "issueId가 있으면 projectId는 적용되지 않는다", StringComparison.Ordinal);
        StringAssert.Contains(Description(typeof(TimeEntryTools), nameof(TimeEntryTools.UpdateTimeEntry)), "성공 시 본문이 비어 있다", StringComparison.Ordinal);

        StringAssert.Contains(ParameterDescription(typeof(IssueTools), nameof(IssueTools.ListIssues), "statusId"), "비우면 열린 일감만 반환한다", StringComparison.Ordinal);
        StringAssert.Contains(ParameterDescription(typeof(IssueTools), nameof(IssueTools.ListIssues), "statusId"), "전체는 *이다", StringComparison.Ordinal);
        string include = ParameterDescription(typeof(IssueTools), nameof(IssueTools.GetIssue), "include");
        StringAssert.Contains(include, "journals", StringComparison.Ordinal);
        StringAssert.Contains(include, "children", StringComparison.Ordinal);
        StringAssert.Contains(include, "attachments", StringComparison.Ordinal);
        StringAssert.Contains(include, "relations", StringComparison.Ordinal);
        StringAssert.Contains(include, "changesets", StringComparison.Ordinal);
        StringAssert.Contains(include, "watchers는 2.2 응답에 없다", StringComparison.Ordinal);
        StringAssert.Contains(Description(typeof(IssueTools), nameof(IssueTools.UpdateIssue)), "성공 시 본문이 비어 있다", StringComparison.Ordinal);

        StringAssert.Contains(Description(typeof(UserTools), nameof(UserTools.ListUsers)), "관리자 권한이 필요하다", StringComparison.Ordinal);
        StringAssert.Contains(Description(typeof(UserTools), nameof(UserTools.ListUsers)), "활성 사용자만 반환한다", StringComparison.Ordinal);
        string getUser = Description(typeof(UserTools), nameof(UserTools.GetUser));
        StringAssert.Contains(getUser, "활성 사용자가 아니거나", StringComparison.Ordinal);
        StringAssert.Contains(getUser, "보이는 멤버십과 활동이 없으면 404", StringComparison.Ordinal);
        StringAssert.Contains(Description(typeof(UserTools), nameof(UserTools.GetCurrentUser)), "일반 API 키", StringComparison.Ordinal);

        string wiki = Description(typeof(WikiTools), nameof(WikiTools.PutWikiPage));
        StringAssert.Contains(wiki, "성공 본문이 비어 있다", StringComparison.Ordinal);
        StringAssert.Contains(wiki, "페이지 JSON이 온다", StringComparison.Ordinal);

        string activities = Description(typeof(MetadataTools), nameof(MetadataTools.ListTimeEntryActivities));
        StringAssert.Contains(activities, "시스템 공통 활동만 반환한다", StringComparison.Ordinal);
        StringAssert.Contains(activities, "프로젝트에서 재정의한 활동 ID는 목록에 없다", StringComparison.Ordinal);

        string download = Description(typeof(AttachmentTools), nameof(AttachmentTools.DownloadAttachment));
        StringAssert.Contains(download, "5MiB", StringComparison.Ordinal);
        StringAssert.Contains(download, "outputPath는 MCP 서버 호스트", StringComparison.Ordinal);

        string upload = Description(typeof(AttachmentTools), nameof(AttachmentTools.UploadFile));
        StringAssert.Contains(upload, "5MiB", StringComparison.Ordinal);
        StringAssert.Contains(upload, "sourcePath는 MCP 서버 호스트", StringComparison.Ordinal);
        StringAssert.Contains(upload, "uploads로 넘겨야 첨부가 된다", StringComparison.Ordinal);
        StringAssert.Contains(upload, "100초", StringComparison.Ordinal);
        StringAssert.Contains(wiki, "get_wiki_page의 현재 text", StringComparison.Ordinal);
        StringAssert.Contains(ParameterDescription(typeof(RelationTools), nameof(RelationTools.CreateIssueRelation), "relationType"), "copied_to는 2.2에 없다", StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task CreateIssueRelation_RejectsCopiedTo()
    {
        using RecordingHandler handler = new();
        using HttpClient httpClient = new(handler, disposeHandler: false);
        RedmineClient.Configure(httpClient, new RedmineOptions { ApiEndpoint = "https://redmine.example/", ApiKey = "secret-key" });
        RelationTools tools = new(new RedmineClient(httpClient));

        await Assert.ThrowsExactlyAsync<McpException>(() =>
            tools.CreateIssueRelation(1, 2, "copied_to", cancellationToken: CancellationToken.None));

        Assert.IsNull(handler.RequestUri);
        CollectionAssert.DoesNotContain(RelationTools.RelationTypes, "copied_to");
    }

    private static string Description(Type type, string methodName)
    {
        return type.GetMethod(methodName)!.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()!.Description;
    }

    private static string ParameterDescription(Type type, string methodName, string parameterName)
    {
        ParameterInfo parameter = type.GetMethod(methodName)!.GetParameters().Single(item => item.Name == parameterName);
        return parameter.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()!.Description;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }
}
