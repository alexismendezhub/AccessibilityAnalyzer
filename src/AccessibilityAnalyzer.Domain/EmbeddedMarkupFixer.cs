namespace AccessibilityAnalyzer.Domain;

/// <summary>
/// Applies the safe fixes to the markup that lives inside script files, and only there:
/// <list type="bullet">
/// <item>component templates in a <c>template:</c> string (Angular, Vue, AngularJS) in .ts/.js files;</item>
/// <item>JSX elements in React files (.jsx, .tsx, and .js/.mjs/.cjs).</item>
/// </list>
/// Everything else — code, other strings, comments, regular expressions, and the JavaScript inside <c>{…}</c> JSX
/// expressions — is kept byte for byte. A region the scanner cannot read with certainty is left unchanged.
/// </summary>
public sealed class EmbeddedMarkupFixer(SafeFixEngine engine)
{
    private const char PlaceholderStart = '';
    private const char PlaceholderEnd = '';
    private const int MaxExpressionDepth = 32;

    private static readonly HashSet<string> TemplateHosts = new(StringComparer.OrdinalIgnoreCase) { ".ts", ".js", ".mjs", ".cjs", ".jsx", ".tsx" };
    private static readonly HashSet<string> JsxHosts = new(StringComparer.OrdinalIgnoreCase) { ".js", ".mjs", ".cjs", ".jsx", ".tsx" };

    // After these keywords an expression starts, so "<" opens JSX and "/" opens a regular expression.
    private static readonly HashSet<string> ExpressionKeywords = new(StringComparer.Ordinal)
    {
        "return", "yield", "await", "typeof", "case", "do", "else", "in", "of", "new", "delete", "void", "throw", "default"
    };

    public static bool HostsEmbeddedMarkup(string path) => TemplateHosts.Contains(Path.GetExtension(path));

    public SafeFixResult Apply(string source, string path)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(path);
        var extension = Path.GetExtension(path);
        if (!TemplateHosts.Contains(extension)) return new SafeFixResult(source, Array.Empty<string>());
        if (source.Contains(PlaceholderStart, StringComparison.Ordinal)) return new SafeFixResult(source, Array.Empty<string>());
        var applied = new List<string>();
        var output = FixScript(source, JsxHosts.Contains(extension), applied, 0);
        return new SafeFixResult(output, applied);
    }

    private string FixScript(string text, bool jsx, List<string> applied, int depth)
    {
        if (depth > MaxExpressionDepth) return text;
        var scanner = new Scanner(text);
        var edits = new List<(int Start, int Length, string Replacement)>();
        char prev = '\0';
        string? lastWord = null;
        var prevIsWord = false;
        string? wordBeforeColon = null;
        var i = 0;

        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (c == '/' && Peek(text, i + 1) == '/') { i = scanner.SkipLineComment(i); continue; }
            if (c == '/' && Peek(text, i + 1) == '*') { i = scanner.SkipBlockComment(i); continue; }

            if (c is '\'' or '"' or '`')
            {
                var end = c == '`' ? scanner.SkipTemplateLiteral(i) : scanner.SkipString(i);
                if (end < 0) return text; // unreadable: change nothing in this script
                var isTemplate = prev == ':' && wordBeforeColon == "template";
                if (isTemplate)
                {
                    var content = text.Substring(i + 1, end - i - 2);
                    // A template with ${…} interpolations is code as much as markup: left unchanged.
                    if (!(c == '`' && content.Contains("${", StringComparison.Ordinal)))
                    {
                        var fixedContent = engine.Apply(content, new SafeFixOptions(AttributeQuote: c == '"' ? '\'' : '"'));
                        if (fixedContent.AppliedFixes.Count > 0)
                        {
                            edits.Add((i + 1, content.Length, fixedContent.Html));
                            applied.AddRange(fixedContent.AppliedFixes);
                        }
                    }
                }
                i = end; prev = c; prevIsWord = false;
                continue;
            }

            if (IsIdentifierStart(c))
            {
                var start = i;
                while (i < text.Length && IsIdentifierPart(text[i])) i++;
                lastWord = text[start..i];
                prevIsWord = true;
                prev = text[i - 1];
                continue;
            }

            var expressionPosition = prev == '\0' || "(,=:[!&|?{};+-*%<>~^".Contains(prev, StringComparison.Ordinal) ||
                                     (prevIsWord && lastWord is not null && ExpressionKeywords.Contains(lastWord));

            if (c == '/' && expressionPosition)
            {
                var end = scanner.SkipRegex(i);
                if (end < 0) return text;
                i = end; prev = '/'; prevIsWord = false;
                continue;
            }

            if (c == '<' && jsx && expressionPosition && (char.IsLetter(Peek(text, i + 1)) || Peek(text, i + 1) == '>'))
            {
                var expressions = new List<(int Start, int End)>();
                var end = scanner.ParseJsxElement(i, expressions);
                if (end > 0)
                {
                    var fixedSpan = FixJsxSpan(text, i, end, expressions, applied, depth);
                    if (!ReferenceEquals(fixedSpan, null)) edits.Add((i, end - i, fixedSpan));
                    i = end; prev = '>'; prevIsWord = false;
                    continue;
                }
            }

            if (c == '{' && jsx)
            {
                // Plain code blocks are scanned in place (JSX can appear deeper, e.g. inside a function body).
            }

            if (c == ':') wordBeforeColon = prevIsWord ? lastWord : null;
            prev = c; prevIsWord = false; i++;
        }

        if (edits.Count == 0) return text;
        var builder = new System.Text.StringBuilder(text);
        foreach (var edit in edits.OrderByDescending(e => e.Start))
            builder.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement);
        return builder.ToString();
    }

    // Masks every {…} expression of the element (its JavaScript is fixed recursively, for JSX nested in it), runs the
    // safe fixes on the remaining markup, then puts the expressions back. Returns the new text, or null when unchanged.
    private string? FixJsxSpan(string text, int start, int end, List<(int Start, int End)> expressions, List<string> applied, int depth)
    {
        var builder = new System.Text.StringBuilder();
        var restored = new List<string>();
        var cursor = start;
        var changed = false;
        foreach (var (exprStart, exprEnd) in expressions.OrderBy(e => e.Start))
        {
            builder.Append(text, cursor, exprStart - cursor);
            var inner = text.Substring(exprStart + 1, exprEnd - exprStart - 2);
            var fixedInner = FixScript(inner, jsx: true, applied, depth + 1);
            changed |= !string.Equals(inner, fixedInner, StringComparison.Ordinal);
            builder.Append(PlaceholderStart).Append(restored.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(PlaceholderEnd);
            restored.Add("{" + fixedInner + "}");
            cursor = exprEnd;
        }
        builder.Append(text, cursor, end - cursor);

        var markup = builder.ToString();
        var result = engine.Apply(markup, new SafeFixOptions());
        if (result.AppliedFixes.Count == 0 && !changed) return null;
        applied.AddRange(result.AppliedFixes);

        var output = new System.Text.StringBuilder(result.Html.Length);
        for (var i = 0; i < result.Html.Length; i++)
        {
            if (result.Html[i] != PlaceholderStart) { output.Append(result.Html[i]); continue; }
            var close = result.Html.IndexOf(PlaceholderEnd, i);
            var index = int.Parse(result.Html.AsSpan(i + 1, close - i - 1), System.Globalization.CultureInfo.InvariantCulture);
            output.Append(restored[index]);
            i = close;
        }
        return output.ToString();
    }

    private static char Peek(string text, int index) => index < text.Length ? text[index] : '\0';
    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c is '_' or '$';
    private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c is '_' or '$';

    /// <summary>Reads JavaScript/TypeScript/JSX lexical units. Every method returns the index after the unit, or -1.</summary>
    private sealed class Scanner(string text)
    {
        public int SkipLineComment(int i)
        {
            var end = text.IndexOf('\n', i);
            return end < 0 ? text.Length : end + 1;
        }

        public int SkipBlockComment(int i)
        {
            var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
            return end < 0 ? text.Length : end + 2;
        }

        public int SkipString(int i)
        {
            var quote = text[i];
            for (var j = i + 1; j < text.Length; j++)
            {
                if (text[j] == '\\') { j++; continue; }
                if (text[j] == quote) return j + 1;
                if (text[j] == '\n') return -1;
            }
            return -1;
        }

        public int SkipTemplateLiteral(int i)
        {
            for (var j = i + 1; j < text.Length; j++)
            {
                if (text[j] == '\\') { j++; continue; }
                if (text[j] == '`') return j + 1;
                if (text[j] == '$' && j + 1 < text.Length && text[j + 1] == '{')
                {
                    var end = SkipBraces(j + 1);
                    if (end < 0) return -1;
                    j = end - 1;
                }
            }
            return -1;
        }

        public int SkipRegex(int i)
        {
            var inClass = false;
            for (var j = i + 1; j < text.Length; j++)
            {
                var c = text[j];
                if (c == '\\') { j++; continue; }
                if (c == '\n') return -1;
                if (c == '[') inClass = true;
                else if (c == ']') inClass = false;
                else if (c == '/' && !inClass)
                {
                    j++;
                    while (j < text.Length && char.IsLetter(text[j])) j++;
                    return j;
                }
            }
            return -1;
        }

        /// <summary>From "{" to after its matching "}", skipping strings, template literals, comments and nested JSX text.</summary>
        public int SkipBraces(int i)
        {
            var depth = 0;
            for (var j = i; j < text.Length; j++)
            {
                var c = text[j];
                if (c is '\'' or '"') { var end = SkipString(j); if (end < 0) return -1; j = end - 1; continue; }
                if (c == '`') { var end = SkipTemplateLiteral(j); if (end < 0) return -1; j = end - 1; continue; }
                if (c == '/' && j + 1 < text.Length && text[j + 1] == '/') { j = SkipLineComment(j) - 1; continue; }
                if (c == '/' && j + 1 < text.Length && text[j + 1] == '*') { j = SkipBlockComment(j) - 1; continue; }
                if (c == '<' && j + 1 < text.Length && (char.IsLetter(text[j + 1]) || text[j + 1] == '>') && PreviousSignificantOpensExpression(j))
                {
                    // JSX inside an expression ({cond && <img …/>}): skip it whole, its quotes are not JavaScript strings.
                    var end = ParseJsxElement(j, null);
                    if (end > 0) { j = end - 1; continue; }
                }
                if (c == '{') depth++;
                else if (c == '}') { depth--; if (depth == 0) return j + 1; }
            }
            return -1;
        }

        private bool PreviousSignificantOpensExpression(int index)
        {
            var j = index - 1;
            while (j >= 0 && char.IsWhiteSpace(text[j])) j--;
            if (j < 0) return true;
            if ("(,=:[!&|?{};>".Contains(text[j], StringComparison.Ordinal)) return true;
            var end = j + 1;
            while (j >= 0 && IsIdentifierPart(text[j])) j--;
            return ExpressionKeywords.Contains(text[(j + 1)..end]);
        }

        /// <summary>
        /// Parses one JSX element (or fragment) starting at "&lt;". Collects the top-level {…} expressions when
        /// <paramref name="expressions"/> is given. Returns the index after the element, or -1 when it is not JSX.
        /// </summary>
        public int ParseJsxElement(int i, List<(int Start, int End)>? expressions)
        {
            var j = i + 1;
            var nameStart = j;
            while (j < text.Length && (char.IsLetterOrDigit(text[j]) || text[j] is '.' or '-' or ':' or '_')) j++;
            var name = text[nameStart..j];

            // Attributes
            while (true)
            {
                while (j < text.Length && char.IsWhiteSpace(text[j])) j++;
                if (j >= text.Length) return -1;
                if (text[j] == '/' && j + 1 < text.Length && text[j + 1] == '>') return name.Length == 0 ? -1 : j + 2;
                if (text[j] == '>') { j++; break; }
                if (text[j] == '{')
                {
                    var end = SkipBraces(j);
                    if (end < 0) return -1;
                    expressions?.Add((j, end));
                    j = end;
                    continue;
                }
                if (!(char.IsLetter(text[j]) || text[j] is '_' or '$')) return -1;
                while (j < text.Length && (char.IsLetterOrDigit(text[j]) || text[j] is '-' or ':' or '_' or '$' or '.')) j++;
                while (j < text.Length && char.IsWhiteSpace(text[j])) j++;
                if (j < text.Length && text[j] == '=')
                {
                    j++;
                    while (j < text.Length && char.IsWhiteSpace(text[j])) j++;
                    if (j >= text.Length) return -1;
                    if (text[j] is '"' or '\'')
                    {
                        var close = text.IndexOf(text[j], j + 1);
                        if (close < 0) return -1;
                        j = close + 1;
                    }
                    else if (text[j] == '{')
                    {
                        var end = SkipBraces(j);
                        if (end < 0) return -1;
                        expressions?.Add((j, end));
                        j = end;
                    }
                    else return -1;
                }
            }

            // Children until the matching closing tag
            while (j < text.Length)
            {
                if (text[j] == '<' && j + 1 < text.Length && text[j + 1] == '/')
                {
                    var close = text.IndexOf('>', j);
                    if (close < 0) return -1;
                    var closingName = text[(j + 2)..close].Trim();
                    return string.Equals(closingName, name, StringComparison.Ordinal) ? close + 1 : -1;
                }
                if (text[j] == '<')
                {
                    var end = ParseJsxElement(j, expressions);
                    if (end < 0) return -1;
                    j = end;
                    continue;
                }
                if (text[j] == '{')
                {
                    var end = SkipBraces(j);
                    if (end < 0) return -1;
                    expressions?.Add((j, end));
                    j = end;
                    continue;
                }
                j++;
            }
            return -1;
        }
    }
}
