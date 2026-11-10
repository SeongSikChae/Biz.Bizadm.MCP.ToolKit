using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Biz.Bizadm.MCP.ToolKit.Jenkins;

/// <summary>Jenkins Pipeline Job config.xml에서 인라인 스크립트(<c>&lt;script&gt;</c>)만 바꾼다.</summary>
/// <remarks>
/// Jenkins는 <c>&lt;?xml version='1.1' ...?&gt;</c> 선언을 쓰지만 .NET 파서는 XML 1.0만 읽는다.
/// 그래서 선언을 떼어 DTD를 금지한 리더로 파싱하고, 저장할 때 원래 선언을 그대로 붙인다.
/// <c>&lt;script&gt;</c> 밖 내용은 의미가 같고 문자열은 달라질 수 있다(<c>&lt;a/&gt;</c> → <c>&lt;a /&gt;</c> 등).
/// </remarks>
internal static class PipelineScript
{
    internal const string CpsDefinition = "org.jenkinsci.plugins.workflow.cps.CpsFlowDefinition";

    private static readonly Regex Declaration = new("^\\s*<\\?xml[^?]*\\?>", RegexOptions.CultureInvariant);

    /// <summary>config.xml의 인라인 Pipeline 스크립트를 <paramref name="script"/>로 바꾼다.</summary>
    /// <returns><c>Xml</c>이 null이면 인라인 Pipeline Job이 아니며 <c>DefinitionClass</c>(없으면 null)를 보고한다. <c>PreviousLength</c>는 개행 정규화 뒤 길이다.</returns>
    /// <exception cref="XmlException">XML 1.0으로 읽을 수 없다(DOCTYPE 포함).</exception>
    /// <exception cref="ArgumentException"><paramref name="script"/>에 XML 1.0에서 허용하지 않는 문자가 있다.</exception>
    internal static (string? Xml, int PreviousLength, string? DefinitionClass) Replace(string configXml, string script)
    {
        configXml = configXml.TrimStart((char)0xFEFF); // BOM은 선언 유무와 무관하게 먼저 뗀다
        Match match = Declaration.Match(configXml);
        string declaration = match.Success ? match.Value : string.Empty;

        XmlReaderSettings readerSettings = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        XDocument doc;
        using (XmlReader reader = XmlReader.Create(new StringReader(configXml[match.Length..]), readerSettings))
            doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace);

        XElement? definition = doc.Root is { Name.LocalName: "flow-definition" } root ? root.Element("definition") : null;
        string? definitionClass = (string?)definition?.Attribute("class");
        XElement? element = definitionClass == CpsDefinition ? definition!.Element("script") : null;
        if (element is null)
            return (null, 0, definitionClass);

        int previous = NormalizedLength(element.Value); // 이전 저장의 &#xD;가 \r로 남을 수 있다
        element.ReplaceNodes(new XText(script));
        return (declaration + Serialize(doc), previous, definitionClass);
    }

    /// <summary><c>\r\n</c>·<c>\r</c>을 <c>\n</c>으로 바꾼 뒤 길이</summary>
    internal static int NormalizedLength(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Length;

    private static string Serialize(XDocument doc)
    {
        StringBuilder builder = new();
        XmlWriterSettings settings = new() { OmitXmlDeclaration = true, Indent = false, NewLineHandling = NewLineHandling.Entitize };
        using (XmlWriter writer = XmlWriter.Create(builder, settings))
            doc.Save(writer);
        return builder.ToString();
    }
}
