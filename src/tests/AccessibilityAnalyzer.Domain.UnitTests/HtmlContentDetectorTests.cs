using AccessibilityAnalyzer.Domain;

namespace AccessibilityAnalyzer.Domain.UnitTests;

public sealed class HtmlContentDetectorTests
{
    [Fact]
    public void ContainsHtml_WithTableMarkup_ReturnsTrue()
    {
        Assert.True(HtmlContentDetector.ContainsHtml("<table><tr><td>Value</td></tr></table>"));
    }

    [Fact]
    public void ContainsHtml_WithPlainCSharp_ReturnsFalse()
    {
        Assert.False(HtmlContentDetector.ContainsHtml("public sealed class Example { }"));
    }

    [Fact]
    public void Classify_RazorFile_ReturnsRazorTemplate()
    {
        Assert.Equal(SourceArtifactKind.RazorTemplate, HtmlContentDetector.Classify("Views/Home/Index.cshtml"));
    }

    [Theory]
    [InlineData("src/app/home/home.component.html")]
    [InlineData("Views/Home/Index.cshtml")]
    [InlineData("Pages/Counter.razor")]
    [InlineData("Default.aspx")]
    [InlineData("components/Card.vue")]
    [InlineData("routes/+page.svelte")]
    public void IsHtmlComponent_MarkupFile_ReturnsTrue(string path)
    {
        Assert.True(HtmlContentDetector.IsHtmlComponent(path));
    }

    [Theory]
    [InlineData("src/app/home/home.component.ts")]
    [InlineData("src/App.tsx")]
    [InlineData("wwwroot/js/site.js")]
    [InlineData("Controllers/HomeController.cs")]
    [InlineData("README.md")]
    [InlineData("appsettings.json")]
    public void IsHtmlComponent_CodeOrOtherFile_ReturnsFalse(string path)
    {
        Assert.False(HtmlContentDetector.IsHtmlComponent(path));
    }
}
