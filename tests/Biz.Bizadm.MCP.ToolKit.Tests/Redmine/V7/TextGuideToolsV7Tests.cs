using Biz.Bizadm.MCP.ToolKit.Redmine;
using Biz.Bizadm.MCP.ToolKit.Tools.Redmine.V7;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tests.Redmine.V7;

[TestClass]
public sealed class TextGuideToolsV7Tests
{
    private static TextGuideToolsV7 Create(RedmineTextFormatting format) =>
        new(new RedmineOptions { TextFormatting = format });

    [TestMethod]
    [DataRow("issue")]
    [DataRow("comment")]
    [DataRow("wiki")]
    public void Textile_ReturnsTextileSyntax(string target)
    {
        string guide = Create(RedmineTextFormatting.Textile).GetTextGuide(target);

        StringAssert.Contains(guide, "본문 문법은 Textile", StringComparison.Ordinal);
        StringAssert.Contains(guide, "h2. 절", StringComparison.Ordinal);
        Assert.IsFalse(guide.Contains("**굵게**", StringComparison.Ordinal));
        AssertShared(guide);
    }

    [TestMethod]
    [DataRow("issue")]
    [DataRow("comment")]
    [DataRow("wiki")]
    public void CommonMark_ReturnsMarkdownSyntax(string target)
    {
        string guide = Create(RedmineTextFormatting.CommonMark).GetTextGuide(target);

        StringAssert.Contains(guide, "CommonMark Markdown", StringComparison.Ordinal);
        StringAssert.Contains(guide, "## 절", StringComparison.Ordinal);
        StringAssert.Contains(guide, "```ruby", StringComparison.Ordinal);
        Assert.IsFalse(guide.Contains("h2.", StringComparison.Ordinal));
        AssertShared(guide);
    }

    [TestMethod]
    [DataRow(RedmineTextFormatting.Textile)]
    [DataRow(RedmineTextFormatting.CommonMark)]
    public void CommentGuide_PointsToUpdateJournal(RedmineTextFormatting format)
    {
        string guide = Create(format).GetTextGuide("comment");

        StringAssert.Contains(guide, "update_journal", StringComparison.Ordinal);
        Assert.IsFalse(guide.Contains("고칠 수 없다", StringComparison.Ordinal));
    }

    [TestMethod]
    public void InvalidTarget_Throws()
    {
        TextGuideToolsV7 tools = Create(RedmineTextFormatting.Textile);
        Assert.ThrowsExactly<McpException>(() => tools.GetTextGuide(""));
        Assert.ThrowsExactly<McpException>(() => tools.GetTextGuide("news"));
    }

    private static void AssertShared(string guide)
    {
        StringAssert.Contains(guide, "#124", StringComparison.Ordinal);
        StringAssert.Contains(guide, "{{toc}}", StringComparison.Ordinal);
        StringAssert.Contains(guide, "[[페이지]]", StringComparison.Ordinal);
    }
}
