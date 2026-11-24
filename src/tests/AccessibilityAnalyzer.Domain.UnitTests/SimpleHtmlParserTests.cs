using AccessibilityAnalyzer.Domain;

namespace AccessibilityAnalyzer.Domain.UnitTests;

public sealed class SimpleHtmlParserTests
{
    [Fact]
    public void ParsesNonVoidElementWithClosingTag()
    {
        const string html = "<div class=\"card\">Hello <span>world</span></div>";

        var elements = new SimpleHtmlParser().Parse(html);

        Assert.True(elements.Count >= 1);
        var div = elements[0];
        Assert.Equal("div", div.TagName);
        Assert.Equal("Hello world", div.InnerText);
        Assert.Equal(1, div.Line);
        Assert.Equal(1, div.Column);
    }
}
