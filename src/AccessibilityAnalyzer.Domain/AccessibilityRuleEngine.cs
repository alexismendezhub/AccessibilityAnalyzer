using System.Globalization;
using System.Text.RegularExpressions;

namespace AccessibilityAnalyzer.Domain;

public sealed class AccessibilityRuleEngine
{
    private readonly SimpleHtmlParser _parser = new();
    private readonly DynamicHtmlAnalyzer _dynamic = new();

    public AccessibilityReport Analyze(string content, string sourceName = "pasted-html")
    {
        ArgumentNullException.ThrowIfNull(content);
        var elements = _parser.Parse(content);
        var findings = new List<AccessibilityFinding>();
        CheckDocument(sourceName, elements, findings);
        CheckImages(sourceName, elements, findings);
        CheckForms(sourceName, elements, findings);
        CheckInteractive(sourceName, elements, findings);
        CheckHeadings(sourceName, elements, findings);
        CheckDuplicateIds(sourceName, elements, findings);
        CheckReferences(sourceName, elements, findings);
        CheckTables(content, sourceName, elements, findings);
        CheckFrames(sourceName, elements, findings);
        CheckKeyboard(sourceName, elements, findings);
        CheckAria(sourceName, elements, findings);
        CheckMetaRefresh(sourceName, elements, findings);
        CheckSvg(sourceName, elements, findings);
        CheckMedia(content, sourceName, elements, findings);
        CheckBypass(sourceName, elements, findings);
        return new AccessibilityReport(sourceName, DateTimeOffset.UtcNow, Consolidate(findings), _dynamic.Analyze(content, sourceName));
    }

    private static void CheckDocument(string source, IReadOnlyList<HtmlElementInfo> elements, List<AccessibilityFinding> findings)
    {
        var html = elements.FirstOrDefault(static e => e.TagName == "html");
        if (html is null) return;
        var lang = html.Attribute("lang");
        if (string.IsNullOrWhiteSpace(lang)) Add(findings, "DOC-LANG-001", FindingStatus.Error, AnalysisSeverity.Error, .99, "The <html> element does not declare lang.", html.RawStartTag, "Set lang to the actual primary language of the page (for example lang=\"en\" or lang=\"es\").", source, html);
        else if (!Regex.IsMatch(lang, @"^[A-Za-z]{2,8}(?:-[A-Za-z0-9]{1,8})*$")) Add(findings, "DOC-LANG-001", FindingStatus.PotentialIssue, AnalysisSeverity.Warning, .85, $"The lang value '{lang}' is not a recognizable BCP 47-style language tag.", html.RawStartTag, "Use a valid language tag that reflects the page language.", source, html);

        var title = elements.FirstOrDefault(static e => e.TagName == "title");
        if (title is null || string.IsNullOrWhiteSpace(title.InnerText))
            Add(findings, "DOC-TITLE-001", FindingStatus.Error, AnalysisSeverity.Error, .98, "The HTML document has no non-empty <title>.", title?.RawStartTag ?? html.RawStartTag, "Provide a concise page title that identifies the current page or task.", source, title ?? html);
    }

    private static void CheckImages(string source, IReadOnlyList<HtmlElementInfo> elements, List<AccessibilityFinding> findings)
    {
        foreach (var img in elements.Where(static e => e.TagName == "img"))
        {
            if (!img.HasAttribute("alt"))
            {
                Add(findings, "IMG-ALT-001", FindingStatus.Error, AnalysisSeverity.Error, .99, "Image is missing the alt attribute.", img.RawStartTag, "Add meaningful alt text for informative images or alt=\"\" for images confirmed to be decorative.", source, img);
                continue;
            }
            var alt = img.Attribute("alt") ?? string.Empty;
            if (LooksLikePlaceholderAlternative(alt))
                Add(findings, "IMG-ALT-QUALITY-001", FindingStatus.ManualReview, AnalysisSeverity.Review, .82, $"Image alt text '{alt}' looks filename-like or placeholder-like.", img.RawStartTag, "Confirm that the alternative communicates the same purpose/information as the image; use alt=\"\" only when the image is truly decorative.", source, img);
        }
        foreach (var input in elements.Where(static e => e.TagName == "input" && string.Equals(e.Attribute("type"), "image", StringComparison.OrdinalIgnoreCase)))
            if (string.IsNullOrWhiteSpace(input.Attribute("alt"))) Add(findings, "INPUT-IMAGE-ALT-001", FindingStatus.Error, AnalysisSeverity.Error, .99, "Image input is missing alt text.", input.RawStartTag, "Add alt text that describes the action performed by the image button.", source, input);
    }

    private static void CheckForms(string source, IReadOnlyList<HtmlElementInfo> elements, List<AccessibilityFinding> findings)
    {
        var ids = elements.Select(static e => e.Attribute("id")).Where(static x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.Ordinal);
        var labels = elements.Where(static e => e.TagName == "label").ToList();
        foreach (var label in labels)
        {
            var target = label.Attribute("for");
            if (!string.IsNullOrWhiteSpace(target) && !ids.Contains(target))
                Add(findings, "LABEL-FOR-001", FindingStatus.Error, AnalysisSeverity.Error, .99, $"Label references missing id '{target}'.", label.RawStartTag, "Set for to the unique id of the form control this label describes.", source, label);
        }

        foreach (var control in elements.Where(static e => e.TagName is "input" or "select" or "textarea"))
        {
            var type = control.Attribute("type")?.ToLowerInvariant();
            if (type is "hidden" or "submit" or "reset" or "button" or "image") continue;
            var id = control.Attribute("id");
            var explicitLabel = !string.IsNullOrWhiteSpace(id) && labels.Any(l => string.Equals(l.Attribute("for"), id, StringComparison.Ordinal));
            var wrappedLabel = labels.Any(l => control.StartIndex > l.StartIndex && control.StartIndex < l.StartIndex + l.Length && !string.IsNullOrWhiteSpace(l.InnerText));
            var named = explicitLabel || wrappedLabel || !string.IsNullOrWhiteSpace(control.Attribute("aria-label")) || !string.IsNullOrWhiteSpace(control.Attribute("aria-labelledby"));
            if (!named)
                Add(findings, "FORM-LABEL-001", FindingStatus.Error, AnalysisSeverity.Error, .96, "Form control has no programmatically associated label or ARIA name.", control.RawStartTag, "Associate a visible <label> with the control whenever possible; do not rely on placeholder text as the label.", source, control);
        }
    }

    private static void CheckInteractive(string source, IReadOnlyList<HtmlElementInfo> elements, List<AccessibilityFinding> findings)
    {
        foreach (var button in elements.Where(static e => e.TagName == "button"))
        {
            if (HasPrimaryAccessibleName(button, elements)) continue;
            var title = button.Attribute("title");
            Add(findings, "BTN-NAME-001", string.IsNullOrWhiteSpace(title) ? FindingStatus.Error : FindingStatus.PotentialIssue, string.IsNullOrWhiteSpace(title) ? AnalysisSeverity.Error : AnalysisSeverity.Warning, .98, string.IsNullOrWhiteSpace(title) ? "Button has no accessible name." : "Button relies only on title for its accessible name.", button.RawStartTag, "Provide visible button text, aria-label, or aria-labelledby. Visible text is preferred when practical.", source, button, safeFix: !string.IsNullOrWhiteSpace(title), fixDescription: !string.IsNullOrWhiteSpace(title) ? "Copy the existing title value into aria-label." : null);
        }
        foreach (var link in elements.Where(static e => e.TagName == "a" && e.HasAttribute("href")))
        {
            var name = AccessibleNameText(link, elements);
            if (string.IsNullOrWhiteSpace(name))
                Add(findings, "LINK-NAME-001", FindingStatus.Error, AnalysisSeverity.Error, .98, "Link has no discernible accessible name.", link.RawStartTag, "Provide meaningful link text or an accessible name that describes the destination/purpose.", source, link);
            else if (Regex.IsMatch(name.Trim(), @"^(click\s+here|here|more|read\s+more|learn\s+more|details)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                Add(findings, "LINK-GENERIC-001", FindingStatus.PotentialIssue, AnalysisSeverity.Warning, .88, $"Link text '{name.Trim()}' may not identify its purpose without surrounding context.", link.RawStartTag, "Use link text that identifies the destination or purpose, or ensure its programmatic context makes the purpose unambiguous.", source, link);
        }
    }

    private static void CheckHeadings(string source, IReadOnlyList<HtmlElementInfo> elements, List<AccessibilityFinding> findings)
    {
        var headings = elements.Where(static e => e.TagName.Length == 2 && e.TagName[0] == 'h' && e.TagName[1] is >= '1' and <= '6').OrderBy(static e => e.StartIndex).ToList();
        var previous = 0;
        foreach (var heading in headings)
        {
            if (string.IsNullOrWhiteSpace(heading.InnerText))
                Add(findings, "HEADING-EMPTY-001", FindingStatus.PotentialIssue, AnalysisSeverity.Warning, .95, "Heading is empty.", heading.RawStartTag, "Remove empty headings or provide text that accurately identifies the following section.", source, heading);
            var current = heading.TagName[1] - '0';
            if (previous > 0 && current > previous + 1)
                Add(findings, "HEADING-ORDER-001", FindingStatus.PotentialIssue, AnalysisSeverity.Warning, .85, $"Heading level jumps from h{previous} to h{current}.", heading.RawStartTag, "Review the document outline and use heading levels that represent hierarchy rather than visual styling.", source, heading);
            previous = current;
        }
    }

    private static void CheckDuplicateIds(string source, IReadOnlyList<HtmlElementInfo> elements, List<AccessibilityFinding> findings)
    {
        foreach (var group in elements.Select(e => (Element: e, Id: e.Attribute("id"))).Where(static x => !string.IsNullOrWhiteSpace(x.Id)).GroupBy(static x => x.Id!, StringComparer.Ordinal).Where(static g => g.Count() > 1))
        {
            var first = group.First().Element;
            Add(findings, "ID-DUP-001", FindingStatus.Error, AnalysisSeverity.Error, .99, $"ID '{group.Key}' occurs {group.Count()} times.", first.RawStartTag, "Make every id unique and update label/ARIA/fragment references accordingly.", source, first, occurrences: group.Count(), groupKey: $"duplicate-id:{group.Key}");
        }
    }

    private static void CheckReferences(string source, IReadOnlyList<HtmlElementInfo> elements, List<AccessibilityFinding> findings)
    {
        var ids = elements.Select(static e => e.Attribute("id")).Where(static x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.Ordinal);
        foreach (var element in elements)
        {
            foreach (var attributeName in new[] { "aria-labelledby", "aria-describedby", "aria-controls", "headers" })
            {
                var raw = element.Attribute(attributeName);
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var missing = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(id => !ids.Contains(id)).Distinct(StringComparer.Ordinal).ToArray();
                if (missing.Length > 0)
                    Add(findings, "REF-MISSING-001", FindingStatus.Error, AnalysisSeverity.Error, .98, $"{attributeName} references missing id(s): {string.Join(", ", missing)}.", element.RawStartTag, "Create the referenced elements/IDs or remove stale references; IDs must be unique and present in the same document.", source, element);
            }
        }
    }

    private static void CheckTables(string content, string source, IReadOnlyList<HtmlElementInfo> elements, List<AccessibilityFinding> findings)
    {
        foreach (var table in elements.Where(static e => e.TagName == "table"))
        {
            if (string.Equals(table.Attribute("role"), "presentation", StringComparison.OrdinalIgnoreCase) || string.Equals(table.Attribute("role"), "none", StringComparison.OrdinalIgnoreCase)) continue;
            var raw = Slice(content, table);
            if (!Regex.IsMatch(raw, "(?is)<\\s*th(?:\\s|>)"))
                Add(findings, "TABLE-HEADER-001", FindingStatus.PotentialIssue, AnalysisSeverity.Warning, .93, "Table contains no <th> header cells.", table.RawStartTag, "If this represents data, identify row/column headers with <th> and explicit relationships where needed. If it is layout-only, avoid table semantics.", source, table);
            if (!Regex.IsMatch(raw, "(?is)<\\s*caption(?:\\s|>)") && !table.HasAttribute("aria-label") && !table.HasAttribute("aria-labelledby"))
                Add(findings, "TABLE-CAPTION-001", FindingStatus.ManualReview, AnalysisSeverity.Review, .65, "Data-table candidate has no caption or obvious accessible name.", table.RawStartTag, "Confirm the table is identified by nearby context; for many data tables, use <caption> or an appropriate programmatic name.", source, table);
        }
        foreach (var th in elements.Where(static e => e.TagName == "th" && !e.HasAttribute("scope") && !e.HasAttribute("headers")))
            Add(findings, "TABLE-SCOPE-001", FindingStatus.PotentialIssue, AnalysisSeverity.Review, .72, "Header cell has no scope or headers relationship.", th.RawStartTag, "For simple tables, add scope=\"col\" or scope=\"row\" as appropriate; complex tables may require id/headers associations.", source, th);
    }

    private static void CheckFrames(string source, IReadOnlyList<HtmlElementInfo> elements, List<AccessibilityFinding> findings)
    {
        foreach (var frame in elements.Where(static e => e.TagName is "iframe" or "frame"))
            if (string.IsNullOrWhiteSpace(frame.Attribute("title"))) Add(findings, "IFRAME-TITLE-001", FindingStatus.Error, AnalysisSeverity.Error, .98, "Frame has no accessible title.", frame.RawStartTag, "Add a concise title that identifies the frame purpose.", source, frame);
    }

    private static void CheckKeyboard(string source, IReadOnlyList<HtmlElementInfo> elements, List<AccessibilityFinding> findings)
    {
        foreach (var element in elements)
        {
            if (int.TryParse(element.Attribute("tabindex"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var tabindex) && tabindex > 0)
                Add(findings, "TABINDEX-POSITIVE-001", FindingStatus.PotentialIssue, AnalysisSeverity.Warning, .95, $"Positive tabindex={tabindex} overrides natural focus order.", element.RawStartTag, "Prefer DOM order and tabindex=\"0\" only when a custom focusable element is genuinely required.", source, element);
            if (element.HasAttribute("onclick") && element.TagName is not ("button" or "a" or "input" or "select" or "textarea" or "summary"))
            {
                var keyboard = element.HasAttribute("onkeydown") || element.HasAttribute("onkeyup") || element.HasAttribute("onkeypress");
                var role = element.Attribute("role");
                if (!keyboard || string.IsNullOrWhiteSpace(role))
                    Add(findings, "CLICK-SEMANTICS-001", FindingStatus.PotentialIssue, AnalysisSeverity.Warning, .90, "Non-interactive element has a click handler without complete keyboard/semantic evidence.", element.RawStartTag, "Prefer a native button/link. If a custom widget is unavoidable, supply the correct role, keyboard interaction, focusability, and state management.", source, element);
            }
        }
    }

    private static void CheckAria(string source, IReadOnlyList<HtmlElementInfo> elements, List<AccessibilityFinding> findings)
    {
        foreach (var element in elements.Where(static e => string.Equals(e.Attribute("aria-hidden"), "true", StringComparison.OrdinalIgnoreCase)))
        {
            var naturallyFocusable = element.TagName is "button" or "input" or "select" or "textarea" || (element.TagName == "a" && element.HasAttribute("href"));
            var explicitlyFocusable = int.TryParse(element.Attribute("tabindex"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var tab) && tab >= 0;
            if (naturallyFocusable || explicitlyFocusable)
                Add(findings, "ARIA-HIDDEN-FOCUS-001", FindingStatus.Error, AnalysisSeverity.Error, .97, "Focusable element is marked aria-hidden=true.", element.RawStartTag, "Do not expose keyboard focus to content hidden from assistive technology. Remove aria-hidden or remove the element from the focus sequence as appropriate.", source, element);
        }
    }

    private static void CheckMetaRefresh(string source, IReadOnlyList<HtmlElementInfo> elements, List<AccessibilityFinding> findings)
    {
        foreach (var meta in elements.Where(static e => e.TagName == "meta" && string.Equals(e.Attribute("http-equiv"), "refresh", StringComparison.OrdinalIgnoreCase)))
            Add(findings, "META-REFRESH-001", FindingStatus.ManualReview, AnalysisSeverity.Review, .98, "Meta refresh/redirect detected.", meta.RawStartTag, "Review timing, user control, redirect behavior, and whether the mechanism interrupts assistive-technology users.", source, meta);
    }

    private static void CheckSvg(string source, IReadOnlyList<HtmlElementInfo> elements, List<AccessibilityFinding> findings)
    {
        foreach (var svg in elements.Where(static e => e.TagName == "svg"))
        {
            var hidden = string.Equals(svg.Attribute("aria-hidden"), "true", StringComparison.OrdinalIgnoreCase);
            var named = !string.IsNullOrWhiteSpace(svg.Attribute("aria-label")) || !string.IsNullOrWhiteSpace(svg.Attribute("aria-labelledby")) || !string.IsNullOrWhiteSpace(svg.InnerText);
            if (!hidden && !named)
                Add(findings, "SVG-NAME-001", FindingStatus.ManualReview, AnalysisSeverity.Review, .70, "SVG has no obvious accessible name and is not explicitly hidden.", svg.RawStartTag, "If informative, provide an accessible name/description. If decorative, hide it from assistive technology and ensure it is not focusable.", source, svg);
        }
    }

    private static void CheckMedia(string content, string source, IReadOnlyList<HtmlElementInfo> elements, List<AccessibilityFinding> findings)
    {
        foreach (var video in elements.Where(static e => e.TagName == "video"))
        {
            var raw = Slice(content, video);
            if (!Regex.IsMatch(raw, "(?is)<\\s*track[^>]+kind\\s*=\\s*[\"']captions[\"']"))
                Add(findings, "MEDIA-CAPTIONS-001", FindingStatus.ManualReview, AnalysisSeverity.Review, .80, "Video element has no explicit <track kind=\"captions\"> in this markup.", video.RawStartTag, "Confirm that prerecorded video with audio provides synchronized captions. Embedded captions or externally supplied tracks require manual verification.", source, video);
            if (video.HasAttribute("autoplay"))
                Add(findings, "AUTOPLAY-AUDIO-001", FindingStatus.ManualReview, AnalysisSeverity.Review, .75, "Autoplaying video detected.", video.RawStartTag, "If audio plays automatically for more than three seconds, provide a mechanism to pause/stop it or control volume independently.", source, video);
        }
        foreach (var audio in elements.Where(static e => e.TagName == "audio"))
        {
            Add(findings, "MEDIA-AUDIO-001", FindingStatus.ManualReview, AnalysisSeverity.Review, .85, "Audio element requires equivalent-alternative review.", audio.RawStartTag, "For prerecorded audio-only content, provide an equivalent transcript or other required alternative and verify its completeness manually.", source, audio);
            if (audio.HasAttribute("autoplay"))
                Add(findings, "AUTOPLAY-AUDIO-001", FindingStatus.ManualReview, AnalysisSeverity.Review, .85, "Autoplaying audio detected.", audio.RawStartTag, "If audio plays automatically for more than three seconds, provide a mechanism to pause/stop it or control volume independently.", source, audio);
        }
    }

    private static void CheckBypass(string source, IReadOnlyList<HtmlElementInfo> elements, List<AccessibilityFinding> findings)
    {
        if (!elements.Any(static e => e.TagName == "html")) return;
        var hasMain = elements.Any(static e => e.TagName == "main" || string.Equals(e.Attribute("role"), "main", StringComparison.OrdinalIgnoreCase));
        var hasSkip = elements.Any(static e => e.TagName == "a" && (e.Attribute("href")?.StartsWith('#') ?? false) && Regex.IsMatch(e.InnerText, @"skip|bypass", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        var hasNavigation = elements.Any(static e => e.TagName == "nav" || string.Equals(e.Attribute("role"), "navigation", StringComparison.OrdinalIgnoreCase));
        if (hasNavigation && !hasMain && !hasSkip)
        {
            var nav = elements.First(static e => e.TagName == "nav" || string.Equals(e.Attribute("role"), "navigation", StringComparison.OrdinalIgnoreCase));
            Add(findings, "BYPASS-BLOCKS-001", FindingStatus.ManualReview, AnalysisSeverity.Review, .68, "Navigation was detected but no obvious main landmark or skip link was found.", nav.RawStartTag, "Verify that keyboard and screen-reader users can bypass repeated navigation, for example with a main landmark and/or skip link.", source, nav);
        }
    }

    private static string Slice(string content, HtmlElementInfo element)
    {
        if (element.StartIndex < 0 || element.StartIndex >= content.Length) return element.RawStartTag;
        return content.Substring(element.StartIndex, Math.Min(element.Length, content.Length - element.StartIndex));
    }

    private static bool LooksLikePlaceholderAlternative(string alt)
    {
        if (string.IsNullOrWhiteSpace(alt)) return false;
        var value = alt.Trim();
        return Regex.IsMatch(value, @"^(image|photo|picture|graphic|icon)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
               Regex.IsMatch(value, @"\.(png|jpe?g|gif|webp|svg|bmp)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string? AccessibleNameText(HtmlElementInfo element, IReadOnlyList<HtmlElementInfo> elements)
    {
        if (!string.IsNullOrWhiteSpace(element.InnerText)) return element.InnerText;
        var aria = element.Attribute("aria-label");
        if (!string.IsNullOrWhiteSpace(aria)) return aria;
        if (!string.IsNullOrWhiteSpace(element.Attribute("aria-labelledby"))) return "aria-labelledby";
        var descendantAlt = elements.Where(e => (e.TagName is "img" or "input") && e.StartIndex > element.StartIndex && e.StartIndex < element.StartIndex + element.Length)
            .Select(static e => e.Attribute("alt")).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        if (!string.IsNullOrWhiteSpace(descendantAlt)) return descendantAlt;
        return element.Attribute("title");
    }

    private static bool HasPrimaryAccessibleName(HtmlElementInfo element, IReadOnlyList<HtmlElementInfo> elements)
    {
        if (!string.IsNullOrWhiteSpace(element.InnerText) || !string.IsNullOrWhiteSpace(element.Attribute("aria-label")) || !string.IsNullOrWhiteSpace(element.Attribute("aria-labelledby"))) return true;
        return elements.Any(e => (e.TagName is "img" or "input") && e.StartIndex > element.StartIndex && e.StartIndex < element.StartIndex + element.Length && !string.IsNullOrWhiteSpace(e.Attribute("alt")));
    }

    private static AccessibilityFinding[] Consolidate(IReadOnlyList<AccessibilityFinding> findings)
    {
        return findings.GroupBy(static f => $"{f.RuleId}\n{NormalizeEvidence(f.Evidence)}", StringComparer.Ordinal)
            .Select(static group =>
            {
                var first = group.First();
                var count = group.Sum(static f => Math.Max(1, f.Occurrences));
                var locations = group
                    .SelectMany(static f => f.Locations is { Count: > 0 } ? f.Locations : new[] { f.Location })
                    .Distinct()
                    .OrderBy(static location => location.Line)
                    .ThenBy(static location => location.Column)
                    .ToArray();
                if (group.Count() == 1) return first with { Locations = locations };
                return first with
                {
                    Occurrences = count,
                    Locations = locations,
                    GroupKey = $"component:{first.RuleId}:{NormalizeEvidence(first.Evidence)}",
                    Message = $"{first.Message} Repeated {count} times with the same markup signature in this source."
                };
            })
            .OrderByDescending(static f => f.Severity)
            .ThenBy(static f => f.Location.Line)
            .ToArray();
    }

    private static string NormalizeEvidence(string value) => Regex.Replace(value.Trim(), @"\s+", " ");

    private static void Add(List<AccessibilityFinding> findings, string ruleId, FindingStatus status, AnalysisSeverity severity, double confidence, string message, string evidence, string recommendation, string source, HtmlElementInfo e, int occurrences = 1, string? groupKey = null, bool safeFix = false, string? fixDescription = null)
    {
        var rule = AccessibilityRuleCatalog.All.First(r => r.RuleId == ruleId);
        var location = new SourceLocation(source, e.Line, e.Column, e.StartIndex, e.Length);
        findings.Add(new AccessibilityFinding(ruleId, rule.Category, status, severity, confidence, message, evidence, recommendation, rule.WcagCriterion, rule.WcagLevel, rule.Section508Applicability, location, occurrences, groupKey ?? ruleId, safeFix, fixDescription, new[] { location }));
    }
}
