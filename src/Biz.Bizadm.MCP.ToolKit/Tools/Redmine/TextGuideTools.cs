using ModelContextProtocol.Server;
using System.ComponentModel;

using Biz.Bizadm.MCP.ToolKit.Redmine;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tools.Redmine;

/// <summary>Redmine Textile 작성 가이드 Tool</summary>
[McpServerToolType]
public sealed class TextGuideTools
{
    private const string Shared = """
        이 서버의 본문 문법은 Textile이다. Markdown은 쓰지 않는다.

        글자
        *굵게*
        _기울임_
        +밑줄+
        -취소선-
        @코드@
        ??인용??

        구조
        h1. 제목
        h2. 절
        h3. 소절
        * 목록
        # 번호 목록
        bq. 인용 문단
        |표|셀|
        |/2. 두 줄|한 칸|

        코드
        <pre><code class="ruby">
        코드
        </code></pre>
        class에 넣을 수 있는 언어: c, cpp, css, delphi, groovy, html, java, javascript, json, php, python, rhtml, ruby, scheme, sql, xml, yaml

        링크 예약어
        #124
        #124-6
        #124#note-6
        [[페이지]]
        [[페이지#앵커|문구]]
        [[프로젝트:페이지]]
        document#17
        document:제목
        version#3
        version:1.0.0
        attachment:file.zip
        r758
        commit:해시
        source:경로
        source:경로@52#L120
        export:경로
        message#1218
        project#3
        project:식별자
        다른 프로젝트의 문서, 버전, 저장소, 위키는 식별자를 앞에 붙인다. 예: sandbox:document:제목, sandbox:r758, sandbox:source:경로
        자동 링크를 막으려면 앞에 !를 붙인다. 예: !#124

        매크로
        {{toc}}
        {{>toc}}
        {{include(페이지)}}
        {{child_pages}}
        {{collapse(제목)
        접을 내용
        }}
        {{thumbnail(파일)}}
        {{macro_list}}
        {{hello_world}}
        """;

    /// <summary>Redmine 2.2 Textile 예약어와 일감, 댓글, 위키 본문 템플릿을 반환한다. 설명, 댓글, 위키 본문을 쓰기 전에 호출한다.</summary>
    [McpServerTool(Name = "get_text_guide", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Redmine 2.2 Textile 예약어와 일감, 댓글, 위키 본문 템플릿을 반환한다. 설명, 댓글, 위키 본문을 쓰기 전에 호출한다.")]
    public string GetTextGuide(
        [Description("issue, comment, wiki 중 하나")] string target)
    {
        if (string.IsNullOrWhiteSpace(target))
            throw new McpException("target is required.");
        string guide = target.Trim() switch
        {
            "issue" => IssueGuide,
            "comment" => CommentGuide,
            "wiki" => WikiGuide,
            _ => throw new McpException("target은 issue, comment, wiki 중 하나여야 합니다.")
        };
        return guide + "\n" + Shared;
    }

    private const string IssueGuide = """
        대상: 일감
        subject는 일반 텍스트다. Textile 기호를 제목에 넣지 않는다.
        description만 Textile이다. create_issue와 update_issue의 description에 붙인다.

        템플릿
        h2. 배경

        h2. 재현 절차
        # 첫 단계
        # 다음 단계

        h2. 기대 결과

        h2. 실제 결과
        """;

    private const string CommentGuide = """
        대상: 새 댓글
        update_issue의 notes에 붙인다. notes만 Textile이다.
        기존 댓글은 이 Redmine 2.2에 수정 REST가 없어 고칠 수 없다.

        템플릿
        bq. 인용할 내용

        h3. 확인
        * 확인한 사실
        """;

    private const string WikiGuide = """
        대상: 위키
        put_wiki_page의 text에 붙인다. text는 Textile이다.
        {{include(페이지)}}와 {{child_pages}}는 위키 페이지에서 쓴다.

        템플릿
        {{>toc}}

        h1. 제목

        h2. 개요

        h2. 관련 일감
        * #123
        """;
}
