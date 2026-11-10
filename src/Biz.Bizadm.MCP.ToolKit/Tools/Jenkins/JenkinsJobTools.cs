using System.ComponentModel;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml;

using Biz.Bizadm.MCP.ToolKit.Jenkins;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Biz.Bizadm.MCP.ToolKit.Tools.Jenkins;

/// <summary>Jenkins REST API로 Job 설정(config.xml)을 읽고, 바꾸고, Job을 지우는 Tool. Jenkins MCP Server가 노출하지 않는 기능이다.</summary>
[McpServerToolType]
public sealed class JenkinsJobTools
{
    private const string InstanceDescription = "Jenkins 인스턴스 이름(필수). jenkins_list_instances로 확인. Endpoint가 /mcp-server/mcp로 끝나야 한다";
    private const string JobDescription = "Jenkins Job 전체 이름(필수, 예: folder/sub/job). 표시 이름(display name)이 아닌 Job 이름이다. job/a/job/b 형식이나 URL은 받지 않는다";

    private readonly JenkinsRestClient _client;

    /// <summary>DI에 등록한 Jenkins REST client로 Tool을 만든다. <see cref="Microsoft.Extensions.DependencyInjection.JenkinsMcpServerBuilderExtensions.WithJenkinsTools"/>로 등록해야 한다.</summary>
    /// <param name="services">서비스 공급자</param>
    public JenkinsJobTools(IServiceProvider services)
        : this(services.GetRequiredService<JenkinsRestClient>())
    {
    }

    internal JenkinsJobTools(JenkinsRestClient client)
    {
        _client = client;
    }

    /// <summary>Job config.xml을 조회한다.</summary>
    [McpServerTool(Name = "jenkins_get_job_config", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("지정한 Jenkins 인스턴스에서 Job 설정(config.xml, Pipeline 스크립트 포함) 원문을 조회한다. Jenkins가 암호화해 저장한 secret이 들어 있을 수 있다. Job ExtendedRead 또는 Configure 권한이 필요하다.")]
    public async Task<string> GetJobConfig(
        [Description(InstanceDescription)] string? instance = null,
        [Description(JobDescription)] string? job = null,
        CancellationToken cancellationToken = default)
    {
        (JenkinsInstance target, _, string path) = Target(instance, job);
        return await _client.SendAsync(target, HttpMethod.Get, path, "config.xml", null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Job config.xml 전체를 바꾼다.</summary>
    [McpServerTool(Name = "jenkins_update_job_config", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
    [Description("지정한 Jenkins 인스턴스의 Job 설정(config.xml) 전체를 바꾸고 {updated}를 반환한다. 받은 XML을 UTF-8로 그대로 보낸다. 재시작 없이 반영되며 빌드 기록은 유지된다. 스크립트만 바꿀 때는 jenkins_update_pipeline_script를 쓴다. Job Configure 권한이 필요하다.")]
    public async Task<string> UpdateJobConfig(
        [Description(InstanceDescription)] string? instance = null,
        [Description(JobDescription)] string? job = null,
        [Description("새 config.xml 전체(필수). jenkins_get_job_config 결과를 고쳐서 넣는다")] string? configXml = null,
        CancellationToken cancellationToken = default)
    {
        (JenkinsInstance target, string name, string path) = Target(instance, job);
        if (string.IsNullOrEmpty(configXml))
            throw new McpException("configXml is required.");
        await _client.SendAsync(target, HttpMethod.Post, path, "config.xml", Xml(configXml), cancellationToken).ConfigureAwait(false);
        return new JsonObject { ["updated"] = name }.ToJsonString();
    }

    /// <summary>인라인 Pipeline Job의 스크립트만 바꾼다.</summary>
    [McpServerTool(Name = "jenkins_update_pipeline_script", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
    [Description("지정한 Jenkins 인스턴스의 인라인 Pipeline Job에서 config.xml을 읽어 <script>만 바꾼 뒤 다시 올리고 {updated, previousScriptLength, scriptLength}(개행 정규화 뒤 길이)를 반환한다. SCM Jenkinsfile Job·Freestyle Job은 거부한다. Job ExtendedRead(또는 Configure)와 Configure 권한이 필요하다.")]
    public async Task<string> UpdatePipelineScript(
        [Description(InstanceDescription)] string? instance = null,
        [Description(JobDescription)] string? job = null,
        [Description("새 Pipeline 스크립트 전체(필수)")] string? script = null,
        CancellationToken cancellationToken = default)
    {
        (JenkinsInstance target, string name, string path) = Target(instance, job);
        if (string.IsNullOrEmpty(script))
            throw new McpException("script is required.");

        string current = await _client.SendAsync(target, HttpMethod.Get, path, "config.xml", null, cancellationToken).ConfigureAwait(false);
        (string? xml, int previous, string? definitionClass) result;
        try
        {
            result = PipelineScript.Replace(current, script);
        }
        catch (XmlException ex)
        {
            throw new McpException($"Jenkins '{target.Name}' job '{name}' config.xml is not valid XML 1.0: {ex.Message}", ex);
        }
        catch (ArgumentException ex)
        {
            throw new McpException($"Jenkins '{target.Name}' job '{name}' script contains characters not allowed in XML 1.0.", ex);
        }
        if (result.xml is null)
            throw new McpException($"Jenkins '{target.Name}' job '{name}' is not an inline Pipeline script job (definition class: {result.definitionClass ?? "none"}).");

        await _client.SendAsync(target, HttpMethod.Post, path, "config.xml", Xml(result.xml), cancellationToken).ConfigureAwait(false);
        return new JsonObject
        {
            ["updated"] = name,
            ["previousScriptLength"] = result.previous,
            ["scriptLength"] = PipelineScript.NormalizedLength(script),
        }.ToJsonString();
    }

    /// <summary>Job을 지운다.</summary>
    [McpServerTool(Name = "jenkins_delete_job", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = true)]
    [Description("지정한 Jenkins 인스턴스에서 Job을 지우고 {deleted}를 반환한다. 폴더·Multibranch Job이면 그 안의 모든 Job과 빌드 기록까지 지운다. 이미 없으면 404 오류다. 삭제 후 config.xml이 404인지 확인한다. Job Delete 권한이 필요하다.")]
    public async Task<string> DeleteJob(
        [Description(InstanceDescription)] string? instance = null,
        [Description(JobDescription)] string? job = null,
        CancellationToken cancellationToken = default)
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
        catch (McpException ex)
        {
            check = ex.Message; // 전송 오류. 호출자 취소는 그대로 전파된다
        }
        string redirect = location is null ? string.Empty : $" ({location})";
        throw new McpException($"Jenkins '{target.Name}' POST {path}doDelete returned HTTP {code}{redirect}; job '{name}' was not deleted or could not be verified (GET config.xml: {check}).");
    }

    private (JenkinsInstance Target, string Name, string Path) Target(string? instance, string? job)
    {
        JenkinsInstance target = _client.Resolve(instance);
        (string name, string path) = JenkinsRestClient.JobPath(Required(job, nameof(job)));
        return (target, name, path);
    }

    private static StringContent Xml(string xml) => new(xml, Encoding.UTF8, "application/xml");

    private static string Required(string? value, string name)
        => string.IsNullOrWhiteSpace(value) ? throw new McpException($"{name} is required.") : value.Trim();
}
