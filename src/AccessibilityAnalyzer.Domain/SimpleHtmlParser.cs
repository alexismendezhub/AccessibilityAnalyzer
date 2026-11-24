using System.Net;
using System.Text.RegularExpressions;

namespace AccessibilityAnalyzer.Domain;

public sealed partial class SimpleHtmlParser
{
    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr"
    };

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Injectable service: the instance API is kept for dependency injection and test doubles.")]
    public IReadOnlyList<HtmlElementInfo> Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var list = new List<HtmlElementInfo>();
        foreach (Match match in StartTagRegex().Matches(content))
        {
            var tag = match.Groups["tag"].Value.ToLowerInvariant();
            var attrsText = match.Groups["attrs"].Value;
            var attrs = ParseAttributes(attrsText);
            var inner = string.Empty;
            var length = match.Length;
            if (!VoidElements.Contains(tag) && !match.Value.EndsWith("/>", StringComparison.Ordinal))
            {
                var close = Regex.Match(content[(match.Index + match.Length)..], $@"(?is)</\s*{Regex.Escape(tag)}\s*>");
                if (close.Success)
                {
                    var innerStart = match.Index + match.Length;
                    inner = content.Substring(innerStart, close.Index);
                    length = match.Length + close.Index + close.Length;
                }
            }

            var (line, column) = GetLineColumn(content, match.Index);
            list.Add(new HtmlElementInfo(tag, attrs, match.Value, StripMarkup(inner), match.Index, length, line, column));
        }
        return list;
    }

    private static Dictionary<string, string?> ParseAttributes(string text)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in AttributeRegex().Matches(text))
        {
            var name = match.Groups["name"].Value;
            if (name.Length == 0 || name.StartsWith('@')) continue;
            string? value = null;
            if (match.Groups["dq"].Success) value = match.Groups["dq"].Value;
            else if (match.Groups["sq"].Success) value = match.Groups["sq"].Value;
            else if (match.Groups["bare"].Success) value = match.Groups["bare"].Value;
            result[name] = value;
        }
        return result;
    }

    private static string StripMarkup(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var noTags = Regex.Replace(value, "(?is)<[^>]+>", " ");
        return WebUtility.HtmlDecode(Regex.Replace(noTags, @"\s+", " ")).Trim();
    }

    public static (int Line, int Column) GetLineColumn(string content, int index)
    {
        var line = 1;
        var lastBreak = -1;
        for (var i = 0; i < index && i < content.Length; i++)
        {
            if (content[i] == '\n') { line++; lastBreak = i; }
        }
        return (line, index - lastBreak);
    }

    [GeneratedRegex(@"(?is)<\s*(?!/|!|\?)(?<tag>[a-zA-Z][\w:-]*)(?<attrs>[^<>]*?)\s*/?>")]
    private static partial Regex StartTagRegex();

    [GeneratedRegex("""(?is)(?<name>[:@a-zA-Z_][\w:.-]*)(?:\s*=\s*(?:"(?<dq>[^"]*)"|'(?<sq>[^']*)'|(?<bare>[^\s>]+)))?""")]
    private static partial Regex AttributeRegex();
}
