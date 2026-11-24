using System.Net;
using System.Text.RegularExpressions;

namespace AccessibilityAnalyzer.Domain;

/// <param name="AttributeQuote">Quote of the attributes this engine inserts. Markup embedded in a double-quoted script string uses '.</param>
public sealed record SafeFixOptions(string? PreferredLanguage = null, bool CopyButtonTitleToAriaLabel = true, char AttributeQuote = '"');
public sealed record SafeFixResult(string Html, IReadOnlyList<string> AppliedFixes);

public sealed class SafeFixEngine
{
    private static readonly char[] TagNameTerminators = [' ', '\t', '\r', '\n', '>', '/'];

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Injectable service: the instance API is kept for dependency injection and test doubles.")]
    public SafeFixResult Apply(string html, SafeFixOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        options ??= new SafeFixOptions();
        var (masked, protectedBlocks) = MaskScriptAndStyleContent(html);
        var output = masked;
        var applied = new List<string>();

        if (!string.IsNullOrWhiteSpace(options.PreferredLanguage) && Regex.IsMatch(output, "(?is)<html(?![^>]*\\blang\\s*=)[^>]*>"))
        {
            var language = options.PreferredLanguage.Trim();
            if (!Regex.IsMatch(language, @"^[A-Za-z]{2,8}(?:-[A-Za-z0-9]{1,8})*$")) throw new ArgumentException("PreferredLanguage must be a valid BCP 47-style language tag.", nameof(options));
            output = new Regex("<html(?<attrs>[^>]*)>", RegexOptions.IgnoreCase | RegexOptions.Singleline).Replace(output, m => $"<html{m.Groups["attrs"].Value} lang=\"{language}\">", 1);
            applied.Add($"DOC-LANG-001: added lang=\"{language}\" from the user-supplied language option.");
        }

        output = AddTitleFromSingleHeading(output, applied);
        var q = options.AttributeQuote == '\'' ? '\'' : '"';
        output = CopyImageTitleToAlt(output, applied, q);
        output = CopyImageInputNameToAlt(output, applied, q);
        output = CopyFrameAriaLabelToTitle(output, applied, q);

        if (options.CopyButtonTitleToAriaLabel)
        {
            output = Regex.Replace(output, "(?is)<button(?<before>[^>]*?)\\stitle\\s*=\\s*(?<q>[\"'])(?<title>[^\"']+)\\k<q>(?<after>[^>]*)>(?<body>\\s*(?:<[^>]+>\\s*)*)</button>", m =>
            {
                var whole = m.Value;
                // Icon-only content such as <i class="icon-save"></i> gives no name; visible text or an inner alt/aria name does.
                var body = m.Groups["body"].Value;
                if (Regex.IsMatch(whole, "(?is)\\baria-(?:label|labelledby)\\s*=") || Regex.IsMatch(Regex.Replace(body, "<[^>]*>", string.Empty), @"[A-Za-z0-9]") || Regex.IsMatch(body, "(?is)\\b(?:alt|aria-label|aria-labelledby)\\s*=\\s*[\"'][^\"']*[A-Za-z0-9]")) return whole;
                applied.Add("BTN-NAME-001: copied the existing button title into aria-label for an otherwise unnamed button.");
                return whole.Replace("<button", $"<button aria-label={q}{WebUtility.HtmlEncode(m.Groups["title"].Value)}{q}", StringComparison.Ordinal);
            });
        }

        return new SafeFixResult(RestoreProtectedContent(output, protectedBlocks), applied);
    }

    private const char MaskStart = '';
    private const char MaskEnd = '';

    // Script and style bodies are code, not markup: a string such as "<img title='x'>" inside JavaScript must never be rewritten.
    private static (string Html, IReadOnlyList<string> Blocks) MaskScriptAndStyleContent(string html)
    {
        if (html.Contains(MaskStart, StringComparison.Ordinal)) return (html, Array.Empty<string>());
        var blocks = new List<string>();
        var masked = Regex.Replace(html, "(?is)(?<open><(?<tag>script|style)\\b[^>]*>)(?<body>.+?)(?<close></\\k<tag>\\s*>)", m =>
        {
            blocks.Add(m.Groups["body"].Value);
            return $"{m.Groups["open"].Value}{MaskStart}{blocks.Count - 1}{MaskEnd}{m.Groups["close"].Value}";
        });
        return (masked, blocks);
    }

    private static string RestoreProtectedContent(string html, IReadOnlyList<string> blocks) =>
        blocks.Count == 0 ? html : Regex.Replace(html, $"{MaskStart}(?<index>\\d+){MaskEnd}", m => blocks[int.Parse(m.Groups["index"].Value, System.Globalization.CultureInfo.InvariantCulture)]);

    private static string AddTitleFromSingleHeading(string html, List<string> applied)
    {
        if (!Regex.IsMatch(html, "(?is)<html(?:\\s|>)")) return html;
        if (Regex.IsMatch(html, "(?is)<title(?:\\s[^>]*)?>\\s*[^<\\s][\\s\\S]*?</title>")) return html;

        var headings = Regex.Matches(html, "(?is)<h1(?:\\s[^>]*)?>(?<text>[\\s\\S]*?)</h1>")
            .Cast<Match>()
            .Select(m => NormalizeText(m.Groups["text"].Value))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (headings.Length != 1) return html;

        var encoded = WebUtility.HtmlEncode(headings[0]);
        if (Regex.IsMatch(html, "(?is)<title(?:\\s[^>]*)?>\\s*</title>"))
        {
            applied.Add("DOC-TITLE-001: populated the empty document title from the single non-empty h1.");
            return new Regex("<title(?<attrs>\\s[^>]*)?>\\s*</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline).Replace(html, m => $"<title{m.Groups["attrs"].Value}>{encoded}</title>", 1);
        }
        if (Regex.IsMatch(html, "(?is)<head(?:\\s[^>]*)?>"))
        {
            applied.Add("DOC-TITLE-001: added a document title from the single non-empty h1.");
            return new Regex("<head(?<attrs>\\s[^>]*)?>", RegexOptions.IgnoreCase | RegexOptions.Singleline).Replace(html, m => $"<head{m.Groups["attrs"].Value}><title>{encoded}</title>", 1);
        }

        applied.Add("DOC-TITLE-001: created a head/title from the single non-empty h1.");
        return new Regex("<html(?<attrs>[^>]*)>", RegexOptions.IgnoreCase | RegexOptions.Singleline).Replace(html, m => $"<html{m.Groups["attrs"].Value}><head><title>{encoded}</title></head>", 1);
    }

    private static string CopyImageTitleToAlt(string html, List<string> applied, char quote) =>
        Regex.Replace(html, "(?is)<img\\b[^>]*>", m =>
        {
            var tag = m.Value;
            if (HasAttribute(tag, "alt")) return tag;
            var title = AttributeValue(tag, "title");
            if (string.IsNullOrWhiteSpace(title)) return tag;
            applied.Add("IMG-ALT-001: copied an existing image title into alt because no alt attribute was present.");
            return InsertAttribute(tag, "alt", title, quote);
        });

    private static string CopyImageInputNameToAlt(string html, List<string> applied, char quote) =>
        Regex.Replace(html, "(?is)<input\\b[^>]*>", m =>
        {
            var tag = m.Value;
            if (!string.Equals(AttributeValue(tag, "type"), "image", StringComparison.OrdinalIgnoreCase) || HasAttribute(tag, "alt")) return tag;
            var candidate = AttributeValue(tag, "aria-label") ?? AttributeValue(tag, "title") ?? AttributeValue(tag, "value");
            if (string.IsNullOrWhiteSpace(candidate)) return tag;
            applied.Add("INPUT-IMAGE-ALT-001: copied an existing authored accessible-name value into alt for the image input.");
            return InsertAttribute(tag, "alt", candidate, quote);
        });

    private static string CopyFrameAriaLabelToTitle(string html, List<string> applied, char quote) =>
        Regex.Replace(html, "(?is)<(?:iframe|frame)\\b[^>]*>", m =>
        {
            var tag = m.Value;
            if (HasAttribute(tag, "title")) return tag;
            var ariaLabel = AttributeValue(tag, "aria-label");
            if (string.IsNullOrWhiteSpace(ariaLabel)) return tag;
            applied.Add("IFRAME-TITLE-001: copied the existing aria-label into title for the frame.");
            return InsertAttribute(tag, "title", ariaLabel, quote);
        });

    private static bool HasAttribute(string tag, string name) =>
        Regex.IsMatch(tag, $@"(?is)\s{Regex.Escape(name)}\s*=", RegexOptions.CultureInvariant);

    private static string? AttributeValue(string tag, string name)
    {
        var match = Regex.Match(tag, $"(?is)\\s{Regex.Escape(name)}\\s*=\\s*(?<q>[\"'])(?<value>.*?)\\k<q>", RegexOptions.CultureInvariant);
        return match.Success ? WebUtility.HtmlDecode(match.Groups["value"].Value.Trim()) : null;
    }

    private static string InsertAttribute(string tag, string name, string value, char quote)
    {
        var encoded = WebUtility.HtmlEncode(value.Trim());
        var index = tag.IndexOfAny(TagNameTerminators, 1);
        if (index < 0) return tag;
        return tag.Insert(index, $" {name}={quote}{encoded}{quote}");
    }

    private static string NormalizeText(string value)
    {
        var withoutTags = Regex.Replace(value, "(?is)<[^>]+>", " ");
        return Regex.Replace(WebUtility.HtmlDecode(withoutTags), @"\s+", " ").Trim();
    }
}
