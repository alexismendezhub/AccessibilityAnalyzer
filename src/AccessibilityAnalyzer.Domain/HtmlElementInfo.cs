namespace AccessibilityAnalyzer.Domain;

public sealed record HtmlElementInfo(
    string TagName,
    IReadOnlyDictionary<string, string?> Attributes,
    string RawStartTag,
    string InnerText,
    int StartIndex,
    int Length,
    int Line,
    int Column)
{
    public bool HasAttribute(string name) => Attributes.ContainsKey(name);
    public string? Attribute(string name) => Attributes.TryGetValue(name, out var value) ? value : null;
}
