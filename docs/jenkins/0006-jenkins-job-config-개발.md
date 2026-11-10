---
status: done
category: jenkins
feature: jenkins-job-config
phase: 개발
---

# jenkins-job-config 개발

선행: `0004-jenkins-job-config-방안.md`, `0005-jenkins-job-config-설계.md`. 이슈 #3.

단위 순서: U1 → U2 → U3 → U4. 단위마다 구현 후 해당 테스트를 통과시킨 뒤 다음으로 간다.

## U1. `JenkinsRestClient`

**대상**: 새 파일 `src/Biz.Bizadm.MCP.ToolKit/Jenkins/JenkinsRestClient.cs`, 테스트 `tests/.../Jenkins/JenkinsRestClientTests.cs`

**코드 초안**:

```csharp
internal sealed class JenkinsRestClient : IDisposable
{
    internal const string McpPath = "/mcp-server/mcp";
    private const int MaxErrorBodyLength = 200;
    private readonly JenkinsInstance[] _ordered;
    private readonly Dictionary<string, (JenkinsInstance Instance, Uri? Root)> _instances;
    private readonly HttpClient _http;

    public JenkinsRestClient(JenkinsOptions options, HttpMessageHandler? handler = null)
    {
        _ordered = [.. options.Instances];
        _instances = _ordered.ToDictionary(i => i.Name, i => (i, RootOf(i.Endpoint)), StringComparer.OrdinalIgnoreCase);
        _http = new HttpClient(handler ?? new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            AllowAutoRedirect = false,
        }, disposeHandler: handler is null);
    }

    /// Endpoint 경로가 /mcp-server/mcp(끝 / 허용, 대소문자 무시)로 끝나면 그 앞 + "/"
    internal static Uri? RootOf(string endpoint)
    {
        Uri uri = new(endpoint);
        string path = uri.AbsolutePath.TrimEnd('/');
        if (!path.EndsWith(McpPath, StringComparison.OrdinalIgnoreCase))
            return null;
        return new Uri(uri.GetLeftPart(UriPartial.Authority) + path[..^McpPath.Length] + "/");
    }

    /// folder/sub/job → job/folder/job/sub/job/job (세그먼트 인코딩, 빈·.·.. 거부)
    internal static (string Name, string Path) JobPath(string job)
    {
        string[] segments = job.Trim().Trim('/').Split('/');
        if (segments.Any(s => s.Length == 0 || s is "." or ".." || s.Contains('\\')))
            throw new McpException("job must be a full name like folder/job without empty, '.', '..' or '\\' segments.");
        return (string.Join('/', segments), string.Concat(segments.Select(s => "job/" + Uri.EscapeDataString(s) + "/")));
    }

    /// 상태 판정 없이 (상태, Location, 본문)
    public async Task<(int Status, Uri? Location, string Body)> SendRawAsync(JenkinsInstance instance, HttpMethod method,
        string jobPath, string action, HttpContent? content, CancellationToken cancellationToken)
    {
        Uri root = Root(instance);           // null이면 McpException
        string path = jobPath + action;      // 예: job/a/job/b/config.xml
        using HttpRequestMessage request = new(method, new Uri(root, path)) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{instance.User}:{instance.ApiToken}")));
        request.Headers.UserAgent.ParseAdd(UserAgent);
        // ... 전송 예외·취소 처리는 GitHubRestClient와 같다
        Uri? location = response.Headers.Location is { } l ? new Uri(request.RequestUri!, l) : null;
        return ((int)response.StatusCode, location, text);
    }

    /// 2xx만 성공
    public async Task<string> SendAsync(JenkinsInstance instance, HttpMethod method, string jobPath, string action,
        HttpContent? content, CancellationToken cancellationToken)
    {
        (int code, Uri? location, string body) = await SendRawAsync(instance, method, jobPath, action, content, cancellationToken).ConfigureAwait(false);
        return code is >= 200 and < 300 ? body : throw Failure(instance, method, jobPath + action, code, location, body);
    }

    /// 3xx면 "redirected to {Location}", 아니면 DescribeError + 403 crumb 안내
    internal static McpException Failure(JenkinsInstance instance, HttpMethod method, string path, int code, Uri? location, string body)
    {
        string message = code is >= 300 and < 400
            ? $"redirected to {location?.ToString() ?? "(none)"}"
            : DescribeError(body) + (code == 403 && body.Contains("crumb", StringComparison.OrdinalIgnoreCase)
                ? " (ApiToken must be a Jenkins API token, not a password)" : string.Empty);
        return new McpException($"Jenkins '{instance.Name}' {method} {path} failed (HTTP {code}): {message}");
    }

    /// <title> 내용 → 없으면 태그 제거 텍스트 → 본문. HTML 엔티티 디코딩, 공백 축약, 앞 200자
    private static readonly Regex Title = new("<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex Tag = new("<[^>]+>", RegexOptions.Singleline | RegexOptions.CultureInvariant);

    internal static string DescribeError(string body, int status)
    {
        bool html = body.Contains("<html", StringComparison.OrdinalIgnoreCase) || body.Contains("<title", StringComparison.OrdinalIgnoreCase);
        string text = body;
        if (html)
        {
            string stripped = Tag.Replace(Title.Replace(body, " "), " ");
            Match title = Title.Match(body);
            text = !title.Success ? stripped : status >= 500 ? title.Groups[1].Value + " - " + stripped : title.Groups[1].Value;
            text = WebUtility.HtmlDecode(text);
        }
        text = Regex.Replace(text, "\\s+", " ").Trim();
        return text.Length <= MaxErrorBodyLength ? text : text[..MaxErrorBodyLength];
    }
}
```

`Resolve(name)`는 `GitHubRestClient.Resolve`와 같은 형식(`Unknown Jenkins instance '{name}'. Registered instances: ...`)이며, 이름을 찾은 뒤 **루트도 함께 검사**한다. 루트가 없으면 `Jenkins '{name}' Endpoint does not end with /mcp-server/mcp; REST tools are unavailable.` 그래서 Tool은 `Resolve` 단계에서 실패하고 요청 본문(`HttpContent`)을 만들기 전에 끝난다. `SendRawAsync`는 해석된 인스턴스의 루트를 그대로 쓴다.

**검증**: `JenkinsRestClientTests` — `RootOf`(기본·컨텍스트 경로·끝 `/`·대소문자·불일치 null·`/foo-mcp-server/mcp` 불일치), `JobPath`(폴더·인코딩·`%2F` → `%252F`·앞뒤 `/`·빈/`.`/`..`/`\` 거부), `SendAsync`(Basic 헤더, URL, 302 실패와 메시지의 Location, 401 메시지 형식, HTML title(여러 줄·대문자 태그·`&amp;` 등 엔티티)·태그 제거 요약, 403 crumb 안내, 전송 예외, 메시지에 토큰 없음), `SendRawAsync`(상대 Location 해석, 상태 그대로), `Resolve` 오류. URL 비교는 `AbsoluteUri`로 한다(`Uri.ToString()`은 `%252F`를 풀어 보여 준다).

테스트 핸들러: 기존 `GitHubRecordingHandler.Enqueue`에 응답 헤더(`Location`) 설정용 선택 인자 `Action<HttpResponseMessage>? configure = null`을 추가한다(기존 호출 영향 없음). 이 파일은 `allowed_paths`에 추가한다. 핸들러는 응답 큐가 비면 200을 돌려주므로, "요청 없음" 테스트는 반드시 `Requests.Count == 0`을 단언한다.

**선행 단위**: 없음

## U2. `PipelineScript.Replace`

**대상**: 새 파일 `src/Biz.Bizadm.MCP.ToolKit/Jenkins/PipelineScript.cs`, 테스트 `tests/.../Jenkins/PipelineScriptTests.cs`

**코드 초안**:

```csharp
internal static class PipelineScript
{
    internal const string CpsDefinition = "org.jenkinsci.plugins.workflow.cps.CpsFlowDefinition";
    private static readonly Regex Declaration = new("^\\s*<\\?xml[^?]*\\?>", RegexOptions.CultureInvariant);

    /// <returns>Xml이 null이면 인라인 Pipeline Job이 아니며 DefinitionClass(없으면 null)를 보고한다</returns>
    /// <exception cref="XmlException">XML 아님</exception>
    /// <exception cref="ArgumentException">script에 XML 1.0에서 허용하지 않는 문자</exception>
    internal static (string? Xml, int PreviousLength, string? DefinitionClass) Replace(string configXml, string script)
    {
        configXml = configXml.TrimStart((char)0xFEFF);   // BOM은 선언 유무와 무관하게 먼저 뗀다
        Match m = Declaration.Match(configXml);
        string declaration = m.Success ? m.Value : string.Empty;
        XmlReaderSettings readerSettings = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using XmlReader reader = XmlReader.Create(new StringReader(configXml[m.Length..]), readerSettings);
        XDocument doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace);   // DTD 금지(XXE·엔티티 확장 차단)

        XElement? definition = doc.Root is { Name.LocalName: "flow-definition" } root
            ? root.Element("definition") : null;
        string? cls = (string?)definition?.Attribute("class");
        XElement? element = cls == CpsDefinition ? definition!.Element("script") : null;
        if (element is null)
            return (null, 0, cls);

        int previous = NormalizedLength(element.Value);   // 이전 저장의 &#xD;가 \r로 남을 수 있어 정규화
        element.ReplaceNodes(new XText(script));
        return (declaration + Serialize(doc), previous, cls);   // 제어 문자면 ArgumentException
    }

    /// \r\n·\r을 \n으로 바꾼 뒤 길이
    internal static int NormalizedLength(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Length;

    private static string Serialize(XDocument doc)
    {
        StringBuilder sb = new();
        XmlWriterSettings settings = new() { OmitXmlDeclaration = true, Indent = false, NewLineHandling = NewLineHandling.Entitize };
        using (XmlWriter writer = XmlWriter.Create(sb, settings))
            doc.Save(writer);      // 선언 뒤 공백·주석 등 루트 앞 노드도 함께 쓴다
        return sb.ToString();
    }
}
```

**검증**: `PipelineScriptTests` — XML 1.1 선언 그대로 보존, `<script>` 교체 후 `<script>` 밖 내용이 의미상 같음(재파싱 후 `script` 텍스트만 원래 값으로 되돌려 `XNode.DeepEquals`로 원문과 비교, `sandbox` 등), `<`·`&`·`]]>` 이스케이프 후 재파싱 시 원문 일치, CRLF 스크립트 재파싱 시 원문 일치, `NormalizedLength`(CRLF·CR), 기존 `<script>`에 `&#xD;`가 있는 config의 `PreviousLength`, DOCTYPE이 있는 XML은 `XmlException`, 선언 없이 BOM만 있는 XML, 제어 문자 스크립트는 `ArgumentException`, 선언 없는 XML, SCM(`CpsScmFlowDefinition`)·Freestyle(`project` 루트)·`script` 없음은 `Xml` null과 `DefinitionClass` 보고, 잘못된 XML은 `XmlException`.

**선행 단위**: 없음

## U3. `JenkinsJobTools`와 등록

**대상**: 새 파일 `src/Biz.Bizadm.MCP.ToolKit/Tools/Jenkins/JenkinsJobTools.cs`, `JenkinsMcpServerBuilderExtensions.WithJenkinsTools`, 테스트 `tests/.../Jenkins/JenkinsJobToolsTests.cs`, `JenkinsRegistrationTests`(Tool 수 3 → 7, 주석, 비표준 Endpoint 등록 성공)

**코드 초안**:

```csharp
// WithJenkinsTools
builder.Services.AddSingleton(sp => new JenkinsRestClient(sp.GetRequiredService<JenkinsOptions>()));
builder.WithTools<JenkinsTools>();
builder.WithTools<JenkinsJobTools>();
```

```csharp
[McpServerToolType]
public sealed class JenkinsJobTools
{
    private readonly JenkinsRestClient _client;

    // JenkinsRestClient가 internal이므로 기존 JenkinsTools·GitHubRefTools처럼 생성자를 둘로 나눈다
    public JenkinsJobTools(IServiceProvider services)
        : this(services.GetRequiredService<JenkinsRestClient>())
    {
    }

    internal JenkinsJobTools(JenkinsRestClient client) => _client = client;

    [McpServerTool(Name = "jenkins_update_pipeline_script", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
    public async Task<string> UpdatePipelineScript(string? instance = null, string? job = null, string? script = null, CancellationToken cancellationToken = default)
    {
        (JenkinsInstance target, string name, string path) = Target(instance, job);
        if (string.IsNullOrEmpty(script))
            throw new McpException("script is required.");

        string current = await _client.SendAsync(target, HttpMethod.Get, path, "config.xml", null, cancellationToken).ConfigureAwait(false);
        (string xml, int previous) = Replace(target, name, current, script);   // Xml null·XmlException·ArgumentException → McpException, POST 없음
        await _client.SendAsync(target, HttpMethod.Post, path, "config.xml", Xml(xml), cancellationToken).ConfigureAwait(false);
        return new JsonObject { ["updated"] = name, ["previousScriptLength"] = previous, ["scriptLength"] = PipelineScript.NormalizedLength(script) }.ToJsonString();
        // NormalizedLength: \r\n·\r → \n 뒤 길이. previousScriptLength(파서 정규화 뒤)와 같은 기준
    }

    [McpServerTool(Name = "jenkins_delete_job", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
    public async Task<string> DeleteJob(string? instance = null, string? job = null, CancellationToken cancellationToken = default)
    {
        (JenkinsInstance target, string name, string path) = Target(instance, job);
        (int code, Uri? location, string body) = await _client.SendRawAsync(target, HttpMethod.Post, path, "doDelete", null, cancellationToken).ConfigureAwait(false);
        if (code is < 200 or >= 400)
            throw JenkinsRestClient.Failure(target, HttpMethod.Post, path + "doDelete", code, location, body);

        // 리다이렉트를 따라가지 않으므로 GET config.xml 404로 삭제를 확인한다
        string check;
        try
        {
            (int status, _, _) = await _client.SendRawAsync(target, HttpMethod.Get, path, "config.xml", null, cancellationToken).ConfigureAwait(false);
            if (status == 404)
                return new JsonObject { ["deleted"] = name }.ToJsonString();
            check = $"HTTP {status}";
        }
        catch (McpException ex)   // 전송 오류. 취소는 그대로 전파
        {
            check = ex.Message;
        }
        throw new McpException($"Jenkins '{target.Name}' POST {path}doDelete returned HTTP {code}{(location is null ? "" : $" ({location})")}; job '{name}' was not deleted or could not be verified (GET config.xml: {check}).");
        return new JsonObject { ["deleted"] = name }.ToJsonString();
    }

    private static StringContent Xml(string xml) => new(xml, Encoding.UTF8, "application/xml");
}
```

- `jenkins_get_job_config`: `GET config.xml` 본문 그대로 반환. `ReadOnly = true, Idempotent = true, Destructive = false`
- `jenkins_update_job_config`: `configXml` 필수(`IsNullOrEmpty`), `POST config.xml`, `{"updated"}`. `Destructive = true, Idempotent = true`
- `jenkins_delete_job`: 위 초안. 결과 `{"deleted"}`
- `Target`: `_client.Resolve(instance)` → `JenkinsRestClient.JobPath(Required(job))`. 필수 인자 오류는 요청 전 `McpException`.
- Tool 설명: `job`은 "Jenkins 전체 이름(예: folder/sub/job). 표시 이름(display name)이 아닌 Job 이름이다. job/a/job/b나 URL은 받지 않는다", get은 "암호화된 secret이 포함될 수 있는 원문. Job ExtendedRead 또는 Configure 권한 필요", update는 "받은 XML을 UTF-8로 그대로 보낸다", delete는 "폴더·Multibranch Job이면 그 안의 모든 Job과 빌드 기록까지 지운다. 이미 없으면 404 오류".

**검증**: `JenkinsJobToolsTests`(가짜 핸들러는 기존 `GitHubRecordingHandler` 재사용) — Tool마다 메서드·URL·본문·`Content-Type: application/xml; charset=utf-8`, 스크립트 교체 시 GET→POST 2회와 POST 본문의 새 스크립트, 비인라인 Job은 POST 없음, 입력 누락·미등록 인스턴스·비표준 Endpoint는 요청 없음, 삭제 302·200 + 확인 GET 404 성공, 삭제 302(`/login`) + 확인 GET 200 실패, 확인 GET 403 실패, 삭제 403은 확인 GET 없이 실패(요청 1회), 삭제 실패 메시지 전체 문자열(Location 있음·없음), `jenkins_delete_job` 설명에 하위 항목 삭제·404 문구. `JenkinsRegistrationTests` 갱신. 전체 `dotnet test` 통과.

**선행 단위**: U1, U2

## U4. ReadMe

**대상**: `README.md` — 「Jenkins Tool 등록」 절 소개 문장, Tool 수 표의 「Jenkins MCP 중계」 행(Jenkins `3` → `7`, 타입에 `JenkinsJobTools` 추가), 「Jenkins MCP 중계 — `Tools.Jenkins.JenkinsTools`」 상세 절 뒤에 `JenkinsJobTools` 4개(인자 표·결과 형식·오류·필요 권한) 추가. get 결과에 암호화된 secret이 포함될 수 있음, delete는 폴더면 하위 Job·빌드 기록까지 지우고 이미 없으면 404, update는 UTF-8로 그대로 보냄, ApiToken은 비밀번호가 아니라 API Token이어야 함을 적는다.

**코드 초안**:

```markdown
| `jenkins_get_job_config` | Job config.xml 조회 (`GET /job/<경로>/config.xml`) |
| `jenkins_update_job_config` | config.xml 전체 교체 (`POST .../config.xml`) |
| `jenkins_update_pipeline_script` | 인라인 Pipeline `<script>`만 교체 |
| `jenkins_delete_job` | Job 삭제 (`POST .../doDelete`) |

REST 주소는 `Endpoint`에서 `/mcp-server/mcp`를 뗀 Jenkins 루트다. 필요 권한: Job Read·Configure·Delete.
```

**검증**: 문서 표기와 실제 Tool 이름·인자·주석·Tool 수 대조.

**선행 단위**: U3
