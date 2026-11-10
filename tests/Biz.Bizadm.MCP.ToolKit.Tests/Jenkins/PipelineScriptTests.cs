using System.Xml;
using System.Xml.Linq;

using Biz.Bizadm.MCP.ToolKit.Jenkins;

namespace Biz.Bizadm.MCP.ToolKit.Tests.Jenkins;

[TestClass]
public sealed class PipelineScriptTests
{
    internal const string Config = """
        <?xml version='1.1' encoding='UTF-8'?>
        <flow-definition plugin="workflow-job@1400">
          <description>say &quot;hi&quot;</description>
          <keepDependencies>false</keepDependencies>
          <properties/>
          <definition class="org.jenkinsci.plugins.workflow.cps.CpsFlowDefinition" plugin="workflow-cps@3900">
            <script>pipeline { agent any }</script>
            <sandbox>true</sandbox>
          </definition>
          <triggers/>
          <disabled>false</disabled>
        </flow-definition>
        """;

    private static XDocument Parse(string xml)
    {
        int end = xml.IndexOf("?>", StringComparison.Ordinal);
        return XDocument.Parse(xml.StartsWith("<?xml", StringComparison.Ordinal) ? xml[(end + 2)..] : xml);
    }

    private static XElement Script(XDocument doc) => doc.Root!.Element("definition")!.Element("script")!;

    [TestMethod]
    public void Replace_KeepsDeclarationAndEverythingOutsideScript()
    {
        (string? xml, int previous, string? cls) = PipelineScript.Replace(Config, "node { echo 'new' }");

        Assert.IsNotNull(xml);
        Assert.StartsWith("<?xml version='1.1' encoding='UTF-8'?>", xml);
        Assert.AreEqual("pipeline { agent any }".Length, previous);
        Assert.AreEqual(PipelineScript.CpsDefinition, cls);

        XDocument updated = Parse(xml);
        Assert.AreEqual("node { echo 'new' }", Script(updated).Value);
        Script(updated).Value = "pipeline { agent any }";
        Assert.IsTrue(XNode.DeepEquals(Parse(Config), updated));
    }

    [TestMethod]
    [DataRow("echo \"a < b && c > d\" // ]]> <script>")]
    [DataRow("line1\r\nline2\rline3\nend")]
    [DataRow("  leading and trailing  ")]
    public void Replace_RoundTripsScriptText(string script)
    {
        (string? xml, _, _) = PipelineScript.Replace(Config, script);

        Assert.AreEqual(script, Script(Parse(xml!)).Value);
    }

    [TestMethod]
    public void Replace_PreviousLengthIsNormalized()
    {
        string config = Config.Replace("pipeline { agent any }", "a&#xD;\nb&#xD;c");

        (_, int previous, _) = PipelineScript.Replace(config, "x");

        Assert.AreEqual("a\nb\nc".Length, previous);
        Assert.AreEqual(5, PipelineScript.NormalizedLength("a\r\nb\rc"));
    }

    [TestMethod]
    public void Replace_WithoutDeclarationOrWithBom()
    {
        string body = Config[(Config.IndexOf("?>", StringComparison.Ordinal) + 2)..].TrimStart();

        (string? plain, _, _) = PipelineScript.Replace(body, "x");
        (string? bom, _, _) = PipelineScript.Replace("﻿" + body, "x");
        (string? bomDeclared, _, _) = PipelineScript.Replace("﻿" + Config, "x");

        Assert.StartsWith("<flow-definition", plain!);
        Assert.StartsWith("<flow-definition", bom!);
        Assert.StartsWith("<?xml version='1.1'", bomDeclared!);
    }

    [TestMethod]
    public void Replace_ControlCharacterInScript_Throws()
        => Assert.Throws<ArgumentException>(() => PipelineScript.Replace(Config, "a\u0001b"));

    [TestMethod]
    [DataRow("<flow-definition><definition class=\"org.jenkinsci.plugins.workflow.cps.CpsScmFlowDefinition\"><scriptPath>Jenkinsfile</scriptPath></definition></flow-definition>", "org.jenkinsci.plugins.workflow.cps.CpsScmFlowDefinition")]
    [DataRow("<project><builders/></project>", null)]
    [DataRow("<flow-definition><definition class=\"org.jenkinsci.plugins.workflow.cps.CpsFlowDefinition\"/></flow-definition>", "org.jenkinsci.plugins.workflow.cps.CpsFlowDefinition")]
    public void Replace_NotInlinePipeline_ReturnsNullXml(string config, string? expectedClass)
    {
        (string? xml, _, string? cls) = PipelineScript.Replace(config, "x");

        Assert.IsNull(xml);
        Assert.AreEqual(expectedClass, cls);
    }

    [TestMethod]
    [DataRow("<flow-definition>")]
    [DataRow("<!DOCTYPE x [<!ENTITY e \"boom\">]><flow-definition>&e;</flow-definition>")]
    [DataRow("<?xml version='1.1'?><flow-definition>&#1;</flow-definition>")]
    public void Replace_InvalidXml_ThrowsXmlException(string config)
        => Assert.ThrowsExactly<XmlException>(() => PipelineScript.Replace(config, "x"));
}
