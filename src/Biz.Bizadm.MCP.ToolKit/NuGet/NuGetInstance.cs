namespace Biz.Bizadm.MCP.ToolKit.NuGet;

/// <summary>NuGet Server API v3 피드 한 개의 접속 정보</summary>
public sealed class NuGetInstance
{
    /// <summary>인스턴스 이름. Tool의 <c>instance</c> 인자 값이며 대소문자를 구분하지 않는다.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>service index 주소(절대 http/https URI, 예: <c>https://api.nuget.org/v3/index.json</c>). 결과·오류 메시지에 노출되므로 비밀(키)을 넣지 않는다.</summary>
    public string ServiceIndex { get; set; } = string.Empty;

    /// <summary>용도 설명(선택). <c>nuget_list_instances</c>에 노출된다.</summary>
    public string? Description { get; set; }

    /// <summary>쓰기 Tool(push·unlist·relist)에 쓸 API Key(선택). PackagePublish 요청에만 <c>X-NuGet-ApiKey</c> 헤더로 보낸다.</summary>
    public string? ApiKey { get; set; }

    /// <summary>읽기에도 인증이 필요한 피드용 Basic 사용자(선택). <see cref="Password"/>와 함께 설정한다.</summary>
    public string? User { get; set; }

    /// <summary>Basic 비밀번호 또는 PAT(선택). <see cref="ServiceIndex"/>와 같은 origin 요청에만 보낸다.</summary>
    public string? Password { get; set; }
}
