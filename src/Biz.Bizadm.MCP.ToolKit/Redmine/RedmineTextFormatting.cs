namespace Biz.Bizadm.MCP.ToolKit.Redmine;

/// <summary>Redmine 서버 텍스트 서식(관리 → 설정 → 일반 → 텍스트 서식)</summary>
public enum RedmineTextFormatting
{
    /// <summary>Textile</summary>
    Textile,

    /// <summary>CommonMark Markdown(GitHub Flavored). Redmine 7.0에서만 지정할 수 있다.</summary>
    CommonMark
}
