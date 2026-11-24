using AccessibilityAnalyzer.Domain;

namespace AccessibilityAnalyzer.Domain.UnitTests;

public sealed class SafeFixEngineTests
{
    [Fact]
    public void AddsUserSuppliedLanguage()
    {
        var result = new SafeFixEngine().Apply("<html><body>x</body></html>", new SafeFixOptions("es"));
        Assert.Contains("lang=\"es\"", result.Html);
        Assert.Single(result.AppliedFixes);
    }

    [Fact]
    public void DoesNotInventImageAlt()
    {
        var result = new SafeFixEngine().Apply("<img src='x.png'>");
        Assert.Equal("<img src='x.png'>", result.Html);
    }

    [Fact]
    public void CopiesExistingImageTitleToAlt()
    {
        var result = new SafeFixEngine().Apply("<img src='x.png' title='Team photo'>");
        Assert.Contains("alt=\"Team photo\"", result.Html);
    }

    [Fact]
    public void CopiesButtonTitleWhenUnnamed()
    {
        var result = new SafeFixEngine().Apply("<button title='Save'></button>");
        Assert.Contains("aria-label=\"Save\"", result.Html);
    }

    [Fact]
    public void AddsDocumentTitleFromSingleH1()
    {
        var result = new SafeFixEngine().Apply("<html lang='en'><head></head><body><h1>Orders</h1></body></html>");
        Assert.Contains("<title>Orders</title>", result.Html);
    }

    [Fact]
    public void DoesNotInventDocumentTitleWhenMultipleH1ValuesExist()
    {
        var html = "<html lang='en'><head></head><body><h1>Orders</h1><h1>Admin</h1></body></html>";
        var result = new SafeFixEngine().Apply(html);
        Assert.Equal(html, result.Html);
    }

    [Fact]
    public void CopiesButtonTitleWhenContentIsOnlyAnIcon()
    {
        var result = new SafeFixEngine().Apply("<button title=\"Save\"><i class=\"icon-save\"></i></button>");
        Assert.Contains("aria-label=\"Save\"", result.Html);
    }

    [Fact]
    public void LeavesButtonNamedByInnerImageAlt()
    {
        const string html = "<button title=\"Save\"><img src=\"save.png\" alt=\"Print\"></button>";
        Assert.Equal(html, new SafeFixEngine().Apply(html).Html);
    }

    [Fact]
    public void LeavesMarkupInsideScriptAndStyleUntouched()
    {
        const string script = "<script>const tpl = \"<button title='Inline'></button><img title='Inline'>\";</script>";
        const string style = "<style>/* <img title='Styled'> */ img { border: 0; }</style>";
        var html = $"<div>{script}{style}<img src='a.png' title='Logo'></div>";

        var result = new SafeFixEngine().Apply(html);

        Assert.Contains(script, result.Html);
        Assert.Contains(style, result.Html);
        Assert.Contains("<img alt=\"Logo\" src='a.png' title='Logo'>", result.Html);
        Assert.Single(result.AppliedFixes);
    }
}
