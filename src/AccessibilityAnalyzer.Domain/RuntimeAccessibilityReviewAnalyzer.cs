namespace AccessibilityAnalyzer.Domain;

public sealed class RuntimeAccessibilityReviewAnalyzer
{
    private readonly SimpleHtmlParser _parser = new();

    public IReadOnlyList<AccessibilityFinding> Analyze(string dom, string source)
    {
        ArgumentNullException.ThrowIfNull(dom);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var elements = _parser.Parse(dom);
        var findings = new List<AccessibilityFinding>();
        var complex = elements.FirstOrDefault(static e =>
            e.TagName == "dialog" || e.Attribute("role") is "dialog" or "alertdialog" or "grid" or "tree" or "treegrid" or "menu" or "tablist" or "combobox");
        if (complex is not null)
        {
            var rule = AccessibilityRuleCatalog.All.First(static r => r.RuleId == "RUNTIME-KEYBOARD-001");
            findings.Add(Create(rule, complex, source,
                "Complex interactive widget detected in the rendered DOM; keyboard behavior cannot be proven from markup alone.",
                "Test the widget with keyboard only, including entry/exit, expected arrow-key model, Escape behavior, and absence of keyboard traps.",
                "runtime-keyboard"));
        }

        var modal = elements.FirstOrDefault(static e =>
            e.TagName == "dialog" || string.Equals(e.Attribute("aria-modal"), "true", StringComparison.OrdinalIgnoreCase) || e.Attribute("role") is "dialog" or "alertdialog");
        if (modal is not null)
        {
            var rule = AccessibilityRuleCatalog.All.First(static r => r.RuleId == "RUNTIME-FOCUS-001");
            findings.Add(Create(rule, modal, source,
                "Dialog/modal detected in the rendered DOM; focus entry, containment, restoration, and visible focus require runtime verification.",
                "Open and close the dialog using keyboard and assistive technology; verify initial focus, focus containment where appropriate, Escape/close behavior, and focus restoration.",
                "runtime-focus"));
        }

        return findings;
    }

    private static AccessibilityFinding Create(AccessibilityRuleDefinition rule, HtmlElementInfo element, string source, string message, string recommendation, string groupKey)
    {
        var location = new SourceLocation(source, element.Line, element.Column, element.StartIndex, element.Length);
        return new AccessibilityFinding(rule.RuleId, rule.Category, FindingStatus.ManualReview, AnalysisSeverity.Review, .99, message, element.RawStartTag, recommendation,
            rule.WcagCriterion, rule.WcagLevel, rule.Section508Applicability, location, GroupKey: groupKey, Locations: new[] { location });
    }
}
