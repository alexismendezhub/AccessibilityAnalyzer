using System.Text.RegularExpressions;

namespace AccessibilityAnalyzer.Domain;

public sealed partial class DynamicHtmlAnalyzer
{
    private sealed record PatternRule(string RuleId, string Category, AnalysisSeverity Severity, double Confidence, Regex Pattern, string Message, string Recommendation);

    private static readonly PatternRule[] PatternRules =
    {
        new("DYN-EXPLICIT-001", "DynamicHtml", AnalysisSeverity.Review, 0.95, InnerHtmlRegex(), "Assignment to innerHTML can generate or replace runtime markup.", "Inspect the generated DOM and verify accessible names, semantics, focus behavior, and safe encoding."),
        new("DYN-EXPLICIT-002", "DynamicHtml", AnalysisSeverity.Review, 0.95, OuterHtmlRegex(), "Assignment to outerHTML can replace runtime markup.", "Analyze the final DOM after replacement and verify focus is not lost unexpectedly."),
        new("DYN-EXPLICIT-003", "DynamicHtml", AnalysisSeverity.Review, 0.95, InsertAdjacentHtmlRegex(), "insertAdjacentHTML generates runtime markup.", "Run runtime accessibility analysis on the resulting DOM and review keyboard/focus behavior."),
        new("DYN-EXPLICIT-004", "DynamicHtml", AnalysisSeverity.Review, 0.95, DocumentWriteRegex(), "document.write can generate HTML dynamically.", "Prefer structured DOM/component rendering and analyze the resulting DOM."),
        new("DYN-EXPLICIT-005", "DynamicHtml", AnalysisSeverity.Review, 0.85, CreateElementRegex(), "DOM nodes are being generated programmatically.", "Trace created elements and verify names, roles, states, relationships, and keyboard behavior."),
        new("DYN-FRAMEWORK-001", "DynamicHtml", AnalysisSeverity.Review, 0.98, DangerouslySetInnerHtmlRegex(), "React dangerouslySetInnerHTML injects HTML at runtime.", "Analyze the rendered DOM; ensure content sanitization and accessibility semantics are preserved."),
        new("DYN-FRAMEWORK-002", "DynamicHtml", AnalysisSeverity.Review, 0.98, AngularInnerHtmlRegex(), "Angular innerHTML binding injects HTML at runtime.", "Analyze the rendered DOM and verify semantics that cannot be proven from the template alone."),
        new("DYN-FRAMEWORK-003", "DynamicHtml", AnalysisSeverity.Review, 0.98, VueHtmlRegex(), "Vue v-html injects HTML at runtime.", "Analyze the rendered DOM and verify semantics and accessible naming."),
        new("DYN-FRAMEWORK-004", "DynamicHtml", AnalysisSeverity.Review, 0.95, HtmlRawRegex(), "Razor Html.Raw can emit markup dynamically.", "Inspect the emitted DOM and verify both encoding safety and accessibility semantics."),
        new("DYN-FRAMEWORK-005", "DynamicHtml", AnalysisSeverity.Review, 0.95, JQueryHtmlRegex(), "jQuery html() can replace or generate markup.", "Analyze the resulting DOM and validate focus/keyboard behavior for interactive content."),
        new("DYN-GENERATOR-001", "GeneratedMarkup", AnalysisSeverity.Review, 0.99, RazorLoopRegex(), "Razor/Blazor loop generates repeated markup from data.", "Evaluate the template as a component-level source and group repeated accessibility findings by root cause."),
        new("DYN-GENERATOR-002", "GeneratedMarkup", AnalysisSeverity.Review, 0.90, JsMapRegex(), "JavaScript/React map() may generate repeated markup/components.", "Inspect the callback output and group accessibility findings at the generator/component level."),
        new("DYN-GENERATOR-003", "GeneratedMarkup", AnalysisSeverity.Review, 0.99, AngularLoopRegex(), "Angular loop syntax generates repeated markup from data.", "Analyze the repeated template and verify generated IDs, names, headers, focus, and keyboard behavior."),
        new("DYN-GENERATOR-004", "GeneratedMarkup", AnalysisSeverity.Review, 0.99, VueLoopRegex(), "Vue v-for generates repeated markup/components.", "Analyze the repeated component template and group findings by template root cause."),
        new("DYN-GENERATOR-005", "GeneratedMarkup", AnalysisSeverity.Review, 0.99, SvelteLoopRegex(), "Svelte each block generates repeated markup/components.", "Analyze the repeated block and group findings by template root cause."),
        new("DYN-GENERATOR-006", "GeneratedMarkup", AnalysisSeverity.Review, 0.95, DynamicComponentRegex(), "A dynamic component factory was detected.", "Resolve the possible runtime component types and analyze representative rendered states."),
        new("DYN-VIRTUAL-001", "VirtualizedComponent", AnalysisSeverity.Review, 0.88, VirtualizationRegex(), "Virtualized/virtual-scroll markup pattern detected.", "Verify aria-rowcount/aria-rowindex where applicable, screen-reader navigation, offscreen content behavior, and focus persistence while rows are recycled."),
        new("DYN-LIBRARY-001", "ComponentLibrary", AnalysisSeverity.Review, 0.82, KnownGridLibraryRegex(), "A known data-grid/component-library marker was detected.", "Identify the component and verify its accessibility configuration, keyboard model, virtualized-row behavior, headers, sorting state, and focus management."),
    };

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Injectable service: the instance API is kept for dependency injection and test doubles.")]
    public IReadOnlyList<AnalysisFinding> Analyze(string content, string sourceName = "source")
    {
        ArgumentNullException.ThrowIfNull(content);
        var findings = new List<AnalysisFinding>();
        foreach (var rule in PatternRules)
        {
            var matches = rule.Pattern.Matches(content);
            if (matches.Count == 0) continue;
            var locations = matches.Cast<Match>().Select(match => Location(content, sourceName, match.Index, match.Length)).ToArray();
            findings.Add(new AnalysisFinding(rule.RuleId, rule.Category, rule.Severity, rule.Confidence, rule.Message, TrimEvidence(matches[0].Value), rule.Recommendation, matches.Count, locations));
        }
        findings.AddRange(AnalyzeRepeatedMarkup(content, sourceName));
        findings.AddRange(AnalyzeSequentialIds(content, sourceName));
        return findings;
    }

    private static IEnumerable<AnalysisFinding> AnalyzeRepeatedMarkup(string content, string sourceName)
    {
        var signatures = new Dictionary<string, (int Count, string Evidence, string Classification, List<SourceLocation> Locations)>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in StartTagRegex().Matches(content))
        {
            var tag = match.Groups["tag"].Value.ToLowerInvariant();
            if (!IsRepeatCandidateTag(tag)) continue;
            var attrs = match.Groups["attrs"].Value;
            var classMatch = ClassRegex().Match(attrs);
            var classes = classMatch.Success
                ? classMatch.Groups["classes"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(static value => value.Length <= 80).OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).Take(4).ToArray()
                : Array.Empty<string>();
            var signature = classes.Length == 0 ? tag : $"{tag}.{string.Join(".", classes)}";
            var classification = InferRepeatedComponent(tag, classes);
            var location = Location(content, sourceName, match.Index, match.Length);
            if (signatures.TryGetValue(signature, out var existing))
            {
                existing.Locations.Add(location);
                signatures[signature] = (existing.Count + 1, existing.Evidence, existing.Classification, existing.Locations);
            }
            else signatures[signature] = (1, TrimEvidence(match.Value), classification, new List<SourceLocation> { location });
        }

        foreach (var entry in signatures.Where(static pair => pair.Value.Count >= 3).OrderByDescending(static pair => pair.Value.Count).Take(16))
        {
            var count = entry.Value.Count;
            var semanticBoost = entry.Value.Classification == "repeated markup" ? 0.0 : 0.08;
            var confidence = Math.Min(0.94, 0.52 + (Math.Min(count, 20) * 0.02) + semanticBoost);
            yield return new AnalysisFinding("DYN-REPEAT-001", "RepeatedMarkup", AnalysisSeverity.Review, confidence, $"Repeated structural signature '{entry.Key}' appears {count} times and may be generated by a table/grid/component template ({entry.Value.Classification}).", entry.Value.Evidence, "Review the generator/template once, group duplicated findings by component root cause, and verify generated IDs, accessible names, table/grid semantics, focus, and keyboard behavior.", count, entry.Value.Locations);
        }
    }

    private static IEnumerable<AnalysisFinding> AnalyzeSequentialIds(string content, string sourceName)
    {
        var groups = new Dictionary<string, (int Count, string Evidence, List<SourceLocation> Locations)>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in IdRegex().Matches(content))
        {
            var id = match.Groups["id"].Value;
            var normalized = Regex.Replace(id, @"\d+$", "#");
            if (normalized == id) continue;
            var location = Location(content, sourceName, match.Index, match.Length);
            if (groups.TryGetValue(normalized, out var existing))
            {
                existing.Locations.Add(location);
                groups[normalized] = (existing.Count + 1, existing.Evidence, existing.Locations);
            }
            else groups[normalized] = (1, TrimEvidence(match.Value), new List<SourceLocation> { location });
        }
        foreach (var item in groups.Where(static x => x.Value.Count >= 3).Take(8))
            yield return new AnalysisFinding("DYN-SEQUENTIAL-ID-001", "GeneratedMarkup", AnalysisSeverity.Review, .78, $"Sequential/repeated ID pattern '{item.Key}' appears {item.Value.Count} times and may be generated by a repeated component.", item.Value.Evidence, "Verify generated IDs remain unique and that label/ARIA/header references point to the correct repeated instance.", item.Value.Count, item.Value.Locations);
    }

    private static SourceLocation Location(string content, string sourceName, int index, int length)
    {
        var (line, column) = SimpleHtmlParser.GetLineColumn(content, index);
        return new SourceLocation(sourceName, line, column, index, length);
    }

    private static bool IsRepeatCandidateTag(string tag) => tag is "tr" or "td" or "th" or "li" or "div" or "section" or "article" or "button" or "a";
    private static string InferRepeatedComponent(string tag, IReadOnlyCollection<string> classes)
    {
        var text = string.Join(" ", classes).ToLowerInvariant();
        if (tag == "tr" || text.Contains("grid", StringComparison.Ordinal) || text.Contains("row", StringComparison.Ordinal)) return "table/grid candidate";
        if (text.Contains("card", StringComparison.Ordinal)) return "card component candidate";
        if (text.Contains("accordion", StringComparison.Ordinal)) return "accordion component candidate";
        if (text.Contains("tab", StringComparison.Ordinal)) return "tabs component candidate";
        if (text.Contains("menu", StringComparison.Ordinal) || text.Contains("dropdown", StringComparison.Ordinal)) return "menu/dropdown component candidate";
        if (text.Contains("modal", StringComparison.Ordinal) || text.Contains("dialog", StringComparison.Ordinal)) return "modal/dialog component candidate";
        return "repeated markup";
    }

    private static string TrimEvidence(string value)
    {
        const int maxLength = 180;
        var normalized = Regex.Replace(value, @"\s+", " ").Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength] + "...";
    }

    [GeneratedRegex(@"\.innerHTML\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex InnerHtmlRegex();
    [GeneratedRegex(@"\.outerHTML\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex OuterHtmlRegex();
    [GeneratedRegex(@"\.insertAdjacentHTML\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex InsertAdjacentHtmlRegex();
    [GeneratedRegex(@"document\.write(?:ln)?\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex DocumentWriteRegex();
    [GeneratedRegex(@"document\.createElement\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex CreateElementRegex();
    [GeneratedRegex(@"dangerouslySetInnerHTML\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex DangerouslySetInnerHtmlRegex();
    [GeneratedRegex(@"\[innerHTML\]\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex AngularInnerHtmlRegex();
    [GeneratedRegex(@"\bv-html\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex VueHtmlRegex();
    [GeneratedRegex(@"@Html\.Raw\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex HtmlRawRegex();
    [GeneratedRegex(@"(?:\$|jQuery)\s*\([^\)]*\)\.html\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex JQueryHtmlRegex();
    [GeneratedRegex(@"@(?:foreach|for)\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex RazorLoopRegex();
    [GeneratedRegex(@"\.map\s*\(\s*(?:\([^\)]*\)|[A-Za-z_$][\w$]*)\s*=>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex JsMapRegex();
    [GeneratedRegex(@"(?:\*ngFor\s*=|@for\s*\()", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex AngularLoopRegex();
    [GeneratedRegex(@"\bv-for\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex VueLoopRegex();
    [GeneratedRegex(@"\{#each\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex SvelteLoopRegex();
    [GeneratedRegex(@"\bDynamicComponent\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex DynamicComponentRegex();
    [GeneratedRegex(@"(?:virtual[-_ ]?scroll|virtualized|cdk-virtual-scroll|translateY\s*\(|aria-rowindex|aria-rowcount)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex VirtualizationRegex();
    [GeneratedRegex(@"(?:\bag-(?:grid|row|cell|header)|\bk-grid\b|\bdx-datagrid\b|\be-grid\b|\bsf-grid\b|\bp-datatable\b|\bp-table\b|\bmud-table\b|\brz-data-grid\b|\bdataTable\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex KnownGridLibraryRegex();
    [GeneratedRegex(@"<(?<tag>[A-Za-z][A-Za-z0-9:-]*)(?<attrs>[^<>]*?)>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex StartTagRegex();
    [GeneratedRegex("""\bclass\s*=\s*["'](?<classes>[^"']+)["']""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex ClassRegex();
    [GeneratedRegex("""\bid\s*=\s*["'](?<id>[A-Za-z_][\w:.-]*\d+)["']""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex IdRegex();
}
