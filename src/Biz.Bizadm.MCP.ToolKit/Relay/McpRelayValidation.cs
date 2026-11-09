namespace Biz.Bizadm.MCP.ToolKit.Relay;

/// <summary>중계 인스턴스 등록의 공통 검증. 실패하면 <see cref="ArgumentException"/>이며 메시지에 자격 증명을 넣지 않는다.</summary>
internal static class McpRelayValidation
{
    private const string ParamName = "options";

    /// <summary>인스턴스가 1개 이상인지 검사한다.</summary>
    public static void RequireInstances<T>(string label, IList<T>? instances) where T : class
    {
        if (instances is null || instances.Count == 0)
            throw new ArgumentException($"At least one {label} instance is required", ParamName);
    }

    /// <summary>항목이 null이 아닌지 검사한다. 항목 순서대로 다른 검사와 함께 호출한다.</summary>
    public static T NotNull<T>(string label, T? instance) where T : class
        => instance ?? throw new ArgumentException($"{label} instance must not be null", ParamName);

    /// <summary>이름(trim·공백·중복)과 Endpoint(절대 http/https·사용자 정보 없음)를 검사하고 정규화한 값을 반환한다.</summary>
    /// <param name="label">메시지에 쓰는 서비스 이름</param>
    /// <param name="names">이미 등록한 이름(대소문자 무시). 검사한 이름을 추가한다.</param>
    /// <param name="name">인스턴스 이름</param>
    /// <param name="endpoint">MCP 주소</param>
    /// <param name="credentialHint">사용자 정보 대신 쓸 속성 안내(예: <c>User and ApiToken</c>)</param>
    public static (string Name, string Endpoint) NameAndEndpoint(string label, HashSet<string> names, string? name, string? endpoint, string credentialHint)
    {
        string trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            throw new ArgumentException($"{label} instance Name is required", ParamName);
        if (!names.Add(trimmed))
            throw new ArgumentException($"Duplicate {label} instance name: {trimmed}", ParamName);
        if (!Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out Uri? uri)
            || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException($"{label} instance '{trimmed}' Endpoint must be an absolute http or https URI", ParamName);
        // 사용자 정보가 있으면 list_instances로 자격 증명이 노출된다
        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException($"{label} instance '{trimmed}' Endpoint must not contain user info. Use {credentialHint}", ParamName);
        return (trimmed, uri.AbsoluteUri);
    }
}
