using Biz.Bizadm.MCP.ToolKit.Redmine;
using Biz.Bizadm.MCP.ToolKit.Tools.Redmine;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tests.Redmine;

[TestClass]
public sealed class TextGuideToolsTests
{
    [TestMethod]
    [DataRow("issue")]
    [DataRow("comment")]
    [DataRow("wiki")]
    public void GetTextGuide_IncludesSharedReservedWords(string target)
    {
        string guide = new TextGuideTools().GetTextGuide(target);

        string[] reserved =
        [
            "*굵게*",
            "_기울임_",
            "+밑줄+",
            "-취소선-",
            "@코드@",
            "??인용??",
            "h1.",
            "h3.",
            "bq.",
            "|표|셀|",
            "<pre><code class=\"ruby\">",
            "c, cpp, css, delphi, groovy, html, java, javascript, json, php, python, rhtml, ruby, scheme, sql, xml, yaml",
            "#124",
            "#124-6",
            "#124#note-6",
            "[[페이지]]",
            "[[페이지#앵커|문구]]",
            "[[프로젝트:페이지]]",
            "document#17",
            "document:제목",
            "version#3",
            "version:1.0.0",
            "attachment:file.zip",
            "r758",
            "commit:해시",
            "source:경로@52#L120",
            "export:경로",
            "message#1218",
            "project#3",
            "project:식별자",
            "!#124",
            "{{toc}}",
            "{{>toc}}",
            "{{include(페이지)}}",
            "{{child_pages}}",
            "{{collapse(제목)",
            "{{thumbnail(파일)}}",
            "{{macro_list}}",
            "{{hello_world}}"
        ];
        foreach (string token in reserved)
        {
            StringAssert.Contains(guide, token, StringComparison.Ordinal);
        }

        Assert.IsFalse(guide.Contains("{{collapse}}", StringComparison.Ordinal));
    }

    [TestMethod]
    public void GetTextGuide_IssueTemplateUsesPlainSubject()
    {
        string guide = new TextGuideTools().GetTextGuide("issue");

        StringAssert.Contains(guide, "subject는 일반 텍스트", StringComparison.Ordinal);
        StringAssert.Contains(guide, "h2. 배경", StringComparison.Ordinal);
        StringAssert.Contains(guide, "h2. 재현 절차", StringComparison.Ordinal);
        StringAssert.Contains(guide, "h2. 기대 결과", StringComparison.Ordinal);
        StringAssert.Contains(guide, "h2. 실제 결과", StringComparison.Ordinal);
    }

    [TestMethod]
    public void GetTextGuide_CommentTemplateRejectsEditingExistingNotes()
    {
        string guide = new TextGuideTools().GetTextGuide("comment");

        StringAssert.Contains(guide, "update_issue의 notes", StringComparison.Ordinal);
        StringAssert.Contains(guide, "기존 댓글은 이 Redmine 2.2에 수정 REST가 없어 고칠 수 없다.", StringComparison.Ordinal);
        StringAssert.Contains(guide, "bq. 인용할 내용", StringComparison.Ordinal);
        StringAssert.Contains(guide, "h3. 확인", StringComparison.Ordinal);
    }

    [TestMethod]
    public void GetTextGuide_WikiTemplateUsesWikiMacros()
    {
        string guide = new TextGuideTools().GetTextGuide("wiki");

        StringAssert.Contains(guide, "put_wiki_page의 text", StringComparison.Ordinal);
        StringAssert.Contains(guide, "{{include(페이지)}}와 {{child_pages}}는 위키 페이지에서 쓴다.", StringComparison.Ordinal);
        StringAssert.Contains(guide, "{{>toc}}", StringComparison.Ordinal);
        StringAssert.Contains(guide, "h2. 개요", StringComparison.Ordinal);
        StringAssert.Contains(guide, "h2. 관련 일감", StringComparison.Ordinal);
    }

    [TestMethod]
    public void GetTextGuide_RejectsUnknownTarget()
    {
        McpException exception = Assert.ThrowsExactly<McpException>(() => new TextGuideTools().GetTextGuide("search"));

        StringAssert.Contains(exception.Message, "issue, comment, wiki", StringComparison.Ordinal);
    }
}
