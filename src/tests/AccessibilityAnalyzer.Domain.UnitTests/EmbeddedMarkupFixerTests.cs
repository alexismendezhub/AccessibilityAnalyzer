using AccessibilityAnalyzer.Domain;

namespace AccessibilityAnalyzer.Domain.UnitTests;

public sealed class EmbeddedMarkupFixerTests
{
    private static SafeFixResult Fix(string source, string path) => new EmbeddedMarkupFixer(new SafeFixEngine()).Apply(source, path);

    [Theory]
    [InlineData("a.ts", true)]
    [InlineData("a.tsx", true)]
    [InlineData("a.jsx", true)]
    [InlineData("a.js", true)]
    [InlineData("a.mjs", true)]
    [InlineData("a.cjs", true)]
    [InlineData("a.cs", false)]
    [InlineData("a.md", false)]
    [InlineData("a.html", false)]
    public void HostsEmbeddedMarkup_ScriptFilesOnly(string path, bool expected) =>
        Assert.Equal(expected, EmbeddedMarkupFixer.HostsEmbeddedMarkup(path));

    [Fact]
    public void AngularInlineTemplate_IsCorrected_RestOfFileUnchanged()
    {
        const string source = "import { Component } from '@angular/core';\n@Component({\n  selector: 'app-a',\n  template: `\n    <button title=\"Delete\"><i class=\"bi bi-trash\"></i></button>\n    <img src=\"a.png\" title=\"Chart\">\n  `,\n})\nexport class A { label = '<img title=\"code string\">'; }";
        var result = Fix(source, "src/app/a.component.ts");

        Assert.Equal(2, result.AppliedFixes.Count);
        Assert.Equal(source
            .Replace("<button title=\"Delete\">", "<button aria-label=\"Delete\" title=\"Delete\">", StringComparison.Ordinal)
            .Replace("<img src=\"a.png\" title=\"Chart\">", "<img alt=\"Chart\" src=\"a.png\" title=\"Chart\">", StringComparison.Ordinal), result.Html);
    }

    [Fact]
    public void DoubleQuotedTemplate_GetsSingleQuotedAttributes()
    {
        var result = Fix("const c = { template: \"<img src='a.png' title='Chart'>\" };", "legacy.js");
        Assert.Equal("const c = { template: \"<img alt='Chart' src='a.png' title='Chart'>\" };", result.Html);
    }

    [Fact]
    public void TemplateWithInterpolation_IsLeftUnchanged()
    {
        const string source = "@Component({ template: `<img src=\"${base}/a.png\" title=\"Chart\">` }) class A {}";
        Assert.Equal(source, Fix(source, "a.ts").Html);
    }

    [Fact]
    public void TemplateUrlAndOtherStrings_AreNotTemplates()
    {
        const string source = "@Component({ templateUrl: './a.html', styles: [`img { }`] }) class A { html = `<img title=\"x\">`; }";
        Assert.Equal(source, Fix(source, "a.ts").Html);
    }

    [Fact]
    public void TypeScriptGenericsAndAssertions_AreNotJsx()
    {
        const string source = "const a = <any>value; const b: Array<string> = []; if (x < y && y > z) { run(); }";
        Assert.Equal(source, Fix(source, "a.ts").Html);
    }

    [Fact]
    public void Jsx_ExpressionValuesAreNeverCopied_AndCodeInsideIsUntouched()
    {
        const string source = "export const B = () => <button title={t('save')} onClick={() => go(\"<img title='x'>\")}><Icon /></button>;";
        var result = Fix(source, "B.tsx");
        Assert.Equal(source, result.Html);
        Assert.Empty(result.AppliedFixes);
    }

    [Fact]
    public void Jsx_NestedInExpressionsAndMapCallbacks_IsCorrected()
    {
        const string source = "export function List({ items }) {\n  return (\n    <ul>\n      {items.map(i => <li key={i.id}><img src={i.src} title=\"Avatar\" /></li>)}\n    </ul>\n  );\n}";
        var result = Fix(source, "List.jsx");
        Assert.Equal(source.Replace("<img src={i.src}", "<img alt=\"Avatar\" src={i.src}", StringComparison.Ordinal), result.Html);
        Assert.Single(result.AppliedFixes);
    }

    [Fact]
    public void Jsx_FragmentsTextWithApostrophesAndComments_AreHandled()
    {
        const string source = "// <img title=\"comment\">\n/* <img title=\"block\"> */\nconst re = /<img title=\"x\">/;\nconst A = () => <><p>Don't panic</p><img src=\"a.png\" title=\"Logo\" /></>;";
        var result = Fix(source, "A.jsx");
        Assert.Equal(source.Replace("<img src=\"a.png\"", "<img alt=\"Logo\" src=\"a.png\"", StringComparison.Ordinal), result.Html);
        Assert.Single(result.AppliedFixes);
    }

    [Fact]
    public void ReactInPlainJs_IsCorrected()
    {
        const string source = "function Toolbar() { return <button title=\"Print\"><svg aria-hidden=\"true\" /></button>; }";
        var result = Fix(source, "toolbar.js");
        Assert.Contains("<button aria-label=\"Print\" title=\"Print\">", result.Html);
    }

    [Fact]
    public void UnreadableSource_IsLeftUnchanged()
    {
        const string source = "const s = \"unterminated\nconst A = () => <img title=\"x\" />;";
        Assert.Equal(source, Fix(source, "A.jsx").Html);
    }

    [Fact]
    public void TsxGenericArrowFunction_IsNotJsx()
    {
        const string source = "const id = <T,>(v: T) => v;\nconst A = () => <img src=\"a.png\" title=\"Logo\" />;";
        var result = Fix(source, "A.tsx");
        Assert.Contains("const id = <T,>(v: T) => v;", result.Html);
        Assert.Contains("<img alt=\"Logo\" src=\"a.png\" title=\"Logo\" />", result.Html);
    }
}
