using AccessibilityAnalyzer.Application.Contracts;
using AccessibilityAnalyzer.Application.Errors;
using AccessibilityAnalyzer.Application.Ports;
using AccessibilityAnalyzer.Domain;

namespace AccessibilityAnalyzer.Application.Services;

public sealed class AccessibilityAnalysisService
{
    private readonly IRuntimeDomProvider _runtimeDomProvider;
    private readonly ISarifReportWriter _sarif;
    private readonly AccessibilityRuleEngine _engine;
    private readonly SafeFixEngine _safeFix;
    private readonly RuntimeAccessibilityReviewAnalyzer _runtimeReview;
    private readonly EmbeddedMarkupFixer _embeddedMarkup;

    public AccessibilityAnalysisService(
        IRuntimeDomProvider runtimeDomProvider,
        ISarifReportWriter sarif,
        AccessibilityRuleEngine engine,
        SafeFixEngine safeFix,
        RuntimeAccessibilityReviewAnalyzer runtimeReview)
    {
        _runtimeDomProvider = runtimeDomProvider;
        _sarif = sarif;
        _engine = engine;
        _safeFix = safeFix;
        _runtimeReview = runtimeReview;
        _embeddedMarkup = new EmbeddedMarkupFixer(safeFix);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Application service API used through dependency injection.")]
    public IReadOnlyList<AccessibilityRuleDefinition> GetRules() => AccessibilityRuleCatalog.All;

    public AccessibilityReport AnalyzeHtml(string html, string? sourceName = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        return _engine.Analyze(html, NormalizeName(sourceName));
    }

    // Pasted text is HTML by definition, so it is corrected whatever name the caller gives it.
    public SourceAnalysisResult AnalyzeHtmlWithSafeFixes(string html, string? sourceName = null, string? preferredLanguage = null) =>
        AnalyzeSource(NormalizeName(sourceName), html ?? string.Empty, preferredLanguage, applyFixes: true);

    public IReadOnlyList<SourceAnalysisResult> AnalyzeSources(IEnumerable<SourceInput> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        return sources.Select(source => AnalyzeSource(source.Name, source.Content)).ToArray();
    }

    public SafeFixResult ApplySafeFixes(string html, string? preferredLanguage = null) =>
        _safeFix.Apply(html ?? string.Empty, new SafeFixOptions(preferredLanguage));

    public async Task<RuntimeAnalysisResult> AnalyzeRuntimeAsync(string url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url)) throw new AnalysisRequestException("Enter an absolute http or https URL.");
        var snapshot = await _runtimeDomProvider.CaptureAsync(url.Trim(), cancellationToken).ConfigureAwait(false);
        var originalReport = _engine.Analyze(snapshot.Dom, snapshot.Url);
        var correction = _safeFix.Apply(snapshot.Dom);
        var report = _engine.Analyze(correction.Html, snapshot.Url);
        report = AddCorrectionReviewWarnings(report, originalReport, correction.AppliedFixes);

        var runtimeFindings = _runtimeReview.Analyze(correction.Html, snapshot.Url);
        if (runtimeFindings.Count > 0)
            report = report with { Findings = report.Findings.Concat(runtimeFindings).OrderBy(static finding => finding.Location.Line).ToArray() };

        return new RuntimeAnalysisResult(snapshot.Url, snapshot.BrowserExecutable, snapshot.CapturedAt, report, snapshot.Dom, correction.Html, correction.AppliedFixes);
    }

    public string WriteSarifForSources(IEnumerable<SourceInput> sources) =>
        _sarif.Write(AnalyzeSources(sources).Select(static result => result.Report));

    private SourceAnalysisResult AnalyzeSource(string name, string content, string? preferredLanguage = null, bool? applyFixes = null)
    {
        var normalized = NormalizeName(name);
        var original = content ?? string.Empty;
        var kind = HtmlContentDetector.Classify(normalized);
        var containsHtml = HtmlContentDetector.ContainsHtml(original);
        var htmlComponent = applyFixes ?? HtmlContentDetector.IsHtmlComponent(normalized);

        if (!HtmlContentDetector.IsCandidateFile(normalized) && !containsHtml && !htmlComponent)
        {
            var emptyReport = new AccessibilityReport(normalized, DateTimeOffset.UtcNow, Array.Empty<AccessibilityFinding>(), Array.Empty<AnalysisFinding>());
            var plainArtifact = new SourceArtifact(normalized, kind, false, emptyReport.DynamicFindings, htmlComponent);
            return new SourceAnalysisResult(plainArtifact, emptyReport, original, Array.Empty<string>());
        }

        var originalReport = _engine.Analyze(original, normalized);
        var markupHost = !htmlComponent && EmbeddedMarkupFixer.HostsEmbeddedMarkup(normalized);
        if (!htmlComponent && !markupHost)
        {
            // C#, documentation and other text files are reported only; their bytes are never rewritten.
            var reportOnly = new SourceArtifact(normalized, kind, containsHtml, originalReport.DynamicFindings, false);
            return new SourceAnalysisResult(reportOnly, originalReport, original, Array.Empty<string>());
        }

        // Script files (.ts, .js, .jsx, .tsx): only their component templates and JSX markup are corrected; the code around them is unchanged.
        var correction = htmlComponent
            ? _safeFix.Apply(original, new SafeFixOptions(preferredLanguage))
            : _embeddedMarkup.Apply(original, normalized);
        if (correction.AppliedFixes.Count == 0)
        {
            var unchanged = new SourceArtifact(normalized, kind, containsHtml, originalReport.DynamicFindings, htmlComponent);
            return new SourceAnalysisResult(unchanged, originalReport, original, Array.Empty<string>());
        }
        var report = _engine.Analyze(correction.Html, normalized);
        report = AddCorrectionReviewWarnings(report, originalReport, correction.AppliedFixes);
        var artifact = new SourceArtifact(normalized, kind, containsHtml, report.DynamicFindings, htmlComponent);
        return new SourceAnalysisResult(artifact, report, correction.Html, correction.AppliedFixes);
    }

    private static AccessibilityReport AddCorrectionReviewWarnings(
        AccessibilityReport correctedReport,
        AccessibilityReport originalReport,
        IReadOnlyList<string> appliedFixes)
    {
        if (appliedFixes.Count == 0) return correctedReport;

        var fixedRuleIds = appliedFixes
            .Select(static fix => fix.Split(':', 2, StringSplitOptions.TrimEntries)[0])
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);

        var warnings = new List<AccessibilityFinding>();
        foreach (var original in originalReport.Findings.Where(finding => fixedRuleIds.Contains(finding.RuleId)))
        {
            var remainingLines = correctedReport.Findings
                .Where(finding => string.Equals(finding.RuleId, original.RuleId, StringComparison.Ordinal))
                .SelectMany(LocationsOf)
                .Select(static location => location.Line)
                .ToHashSet();

            var fixedLocations = LocationsOf(original)
                .Where(location => !remainingLines.Contains(location.Line))
                .Distinct()
                .OrderBy(static location => location.Line)
                .ThenBy(static location => location.Column)
                .ToArray();
            if (fixedLocations.Length == 0) continue;

            var fixDetails = appliedFixes.Where(fix => fix.StartsWith(original.RuleId + ":", StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).ToArray();
            var detail = fixDetails.Length == 0 ? "A deterministic structural correction was applied." : string.Join(" ", fixDetails);
            warnings.Add(original with
            {
                Status = FindingStatus.PotentialIssue,
                Severity = AnalysisSeverity.Warning,
                Confidence = Math.Min(original.Confidence, .90),
                Message = "Automatic correction applied; the reused or inferred semantic value still requires validation.",
                Recommendation = $"{detail} Verify that the corrected value accurately communicates the element's real purpose/content. Keep the correction if it is accurate; otherwise edit the corrected source.",
                Location = fixedLocations[0],
                Locations = fixedLocations,
                Occurrences = fixedLocations.Length,
                GroupKey = $"autofix-review:{original.RuleId}",
                SafeFixAvailable = false,
                SafeFixDescription = detail
            });
        }

        if (warnings.Count == 0) return correctedReport;
        return correctedReport with
        {
            Findings = correctedReport.Findings
                .Concat(warnings)
                .OrderByDescending(static finding => finding.Severity)
                .ThenBy(static finding => finding.Location.Line)
                .ToArray()
        };
    }

    private static IReadOnlyList<SourceLocation> LocationsOf(AccessibilityFinding finding) =>
        finding.Locations is { Count: > 0 } ? finding.Locations : new[] { finding.Location };

    private static string NormalizeName(string? name) => string.IsNullOrWhiteSpace(name) ? "pasted-html" : name.Trim();
}
