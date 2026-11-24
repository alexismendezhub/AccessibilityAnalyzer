using AccessibilityAnalyzer.Application.Contracts;
using AccessibilityAnalyzer.Application.Ports;
using AccessibilityAnalyzer.Application.Services;
using AccessibilityAnalyzer.Domain;

namespace AccessibilityAnalyzer.Application.UnitTests;

public sealed class AccessibilityAnalysisServiceTests
{
    [Fact]
    public void SourceAnalysisPreservesBlazorClassification()
    {
        var result = Create().AnalyzeSources(new[] { new SourceInput("component.razor", "<button></button>") });
        Assert.Equal(SourceArtifactKind.BlazorComponent, result.Single().Artifact.Kind);
    }

    [Fact]
    public void BrowserSuppliedSourceContentIsAnalyzedWithoutAFileSystemPath()
    {
        var result = Create().AnalyzeSources(new[] { new SourceInput("Views/index.html", "<img src='x'>") });
        Assert.Contains(result.Single().Report.Findings, f => f.RuleId == "IMG-ALT-001");
        Assert.Equal("<img src='x'>", result.Single().CorrectedContent);
    }

    [Fact]
    public void SafeCorrectionStillProducesValidationWarningAtOriginalLine()
    {
        var result = Create().AnalyzeSources(new[] { new SourceInput("sample.html", "<p>before</p>\n<button title='Save'></button>") }).Single();
        Assert.True(result.CorrectedContent.Contains("aria-label=\"Save\"", StringComparison.Ordinal));
        var warning = result.Report.Findings.Single(f => f.RuleId == "BTN-NAME-001");
        Assert.Equal(FindingStatus.PotentialIssue, warning.Status);
        Assert.Equal(2, warning.Location.Line);
        Assert.True(warning.Message.Contains("Automatic correction applied", StringComparison.Ordinal));
    }

    [Fact]
    public void AnalyzeSources_PlainNonMarkupText_IsRetainedUnchangedWithoutInventedFindings()
    {
        const string css = "button:focus { outline: 2px solid currentColor; }";
        var result = Create().AnalyzeSources(new[] { new SourceInput("styles/site.css", css) }).Single();

        Assert.Equal(SourceArtifactKind.Unknown, result.Artifact.Kind);
        Assert.False(result.Artifact.ContainsHtml);
        Assert.Equal(css, result.CorrectedContent);
        Assert.Empty(result.AppliedFixes);
        Assert.Empty(result.Report.Findings);
        Assert.Empty(result.Report.DynamicFindings);
    }

    [Fact]
    public void AnalyzeSources_UnknownTextContainingHtml_IsStillAnalyzed()
    {
        var result = Create().AnalyzeSources(new[] { new SourceInput("notes/accessibility.md", "<img src='example.png'>") }).Single();

        Assert.True(result.Artifact.ContainsHtml);
        Assert.Contains(result.Report.Findings, finding => finding.RuleId == "IMG-ALT-001");
    }

    [Theory]
    [InlineData("wwwroot/js/site.js", "el.innerHTML = \"<button title='Save'></button>\";")]
    [InlineData("src/app/strings.ts", "export const html = `<button title='Save'></button>`;")]
    [InlineData("Services/Mailer.cs", "var html = \"<button title='Save'></button>\";")]
    [InlineData("docs/usage.md", "<button title='Save'></button>")]
    public void AnalyzeSources_MarkupOutsideTemplatesAndJsx_IsReportedButNeverModified(string path, string content)
    {
        var result = Create().AnalyzeSources(new[] { new SourceInput(path, content) }).Single();

        Assert.Equal(content, result.CorrectedContent);
        Assert.Empty(result.AppliedFixes);
        Assert.False(result.Artifact.HtmlComponent);
        Assert.Contains(result.Report.Findings, finding => finding.RuleId == "BTN-NAME-001");
    }

    [Theory]
    [InlineData("src/app/toolbar.component.ts", "@Component({ selector: 'app-toolbar', template: `<button title='Save'></button>` })\nexport class Toolbar {}", "@Component({ selector: 'app-toolbar', template: `<button aria-label=\"Save\" title='Save'></button>` })\nexport class Toolbar {}")]
    [InlineData("src/Toolbar.tsx", "export const Toolbar = (p: Props) => <button title='Save' onClick={() => p.save(\"<img title='x'>\")}></button>;", "export const Toolbar = (p: Props) => <button aria-label=\"Save\" title='Save' onClick={() => p.save(\"<img title='x'>\")}></button>;")]
    [InlineData("src/Logo.jsx", "export default function Logo() {\n  return <img src={logo} title=\"Company logo\" />;\n}", "export default function Logo() {\n  return <img alt=\"Company logo\" src={logo} title=\"Company logo\" />;\n}")]
    [InlineData("src/App.js", "const App = () => (\n  <div>{ready && <iframe src=\"/map\" aria-label=\"Store map\"></iframe>}</div>\n);", "const App = () => (\n  <div>{ready && <iframe title=\"Store map\" src=\"/map\" aria-label=\"Store map\"></iframe>}</div>\n);")]
    public void AnalyzeSources_TemplatesAndJsxInScripts_AreCorrectedLeavingTheCodeUnchanged(string path, string content, string expected)
    {
        var result = Create().AnalyzeSources(new[] { new SourceInput(path, content) }).Single();

        Assert.Equal(expected, result.CorrectedContent);
        Assert.Single(result.AppliedFixes);
        Assert.False(result.Artifact.HtmlComponent);
    }

    [Theory]
    [InlineData("src/app/toolbar.component.html")]
    [InlineData("Views/Shared/_Toolbar.cshtml")]
    [InlineData("Components/Toolbar.razor")]
    public void AnalyzeSources_HtmlComponent_IsCorrected(string path)
    {
        var result = Create().AnalyzeSources(new[] { new SourceInput(path, "<button title='Save'></button>") }).Single();

        Assert.True(result.Artifact.HtmlComponent);
        Assert.Contains("aria-label=\"Save\"", result.CorrectedContent);
        Assert.Single(result.AppliedFixes);
    }

    [Fact]
    public void AnalyzeSources_VueComponent_CorrectsTemplateButNotScript()
    {
        const string script = "<script>export default { data: () => ({ tpl: \"<img title='Inline'>\" }) }</script>";
        var vue = $"<template><img src='logo.png' title='Logo'></template>\n{script}";

        var result = Create().AnalyzeSources(new[] { new SourceInput("components/Logo.vue", vue) }).Single();

        Assert.Contains("<img alt=\"Logo\" src='logo.png' title='Logo'>", result.CorrectedContent);
        Assert.Contains(script, result.CorrectedContent);
    }

    [Fact]
    public void AnalyzeHtmlWithSafeFixes_PastedHtml_IsAlwaysCorrected()
    {
        var result = Create().AnalyzeHtmlWithSafeFixes("<button title='Save'></button>");

        Assert.Contains("aria-label=\"Save\"", result.CorrectedContent);
        Assert.True(result.Artifact.HtmlComponent);
    }

    private static AccessibilityAnalysisService Create() => new(
        new FakeRuntime(),
        new FakeSarif(),
        new AccessibilityRuleEngine(),
        new SafeFixEngine(),
        new RuntimeAccessibilityReviewAnalyzer());

    private sealed class FakeRuntime : IRuntimeDomProvider
    {
        public Task<RuntimeDomSnapshot> CaptureAsync(string url, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RuntimeDomSnapshot(url, "fake", DateTimeOffset.UtcNow, "<html lang='en'><head><title>X</title></head><body></body></html>"));
    }

    private sealed class FakeSarif : ISarifReportWriter
    {
        public string Write(IEnumerable<AccessibilityReport> reports) => "sarif";
    }
}
