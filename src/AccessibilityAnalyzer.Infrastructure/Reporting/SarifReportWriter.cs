using AccessibilityAnalyzer.Domain;
using System.Text.Json;

namespace AccessibilityAnalyzer.Infrastructure.Reporting;

public sealed class SarifReportWriter : AccessibilityAnalyzer.Application.Ports.ISarifReportWriter
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };
    private sealed record Entry(string RuleId, string Title, string Help, string Level, string Message, string Source, int Line, int Column);

    public string Write(IEnumerable<AccessibilityReport> reports)
    {
        ArgumentNullException.ThrowIfNull(reports);
        var entries = new List<Entry>();
        foreach (var report in reports)
        {
            foreach (var finding in report.Findings)
            {
                var rule = AccessibilityRuleCatalog.All.FirstOrDefault(r => r.RuleId == finding.RuleId);
                var locations = finding.Locations is { Count: > 0 } ? finding.Locations : new[] { finding.Location };
                foreach (var location in locations)
                    entries.Add(new Entry(finding.RuleId, rule?.Title ?? finding.RuleId, $"WCAG {finding.WcagCriterion} Level {finding.WcagLevel}. {finding.Section508Applicability}", SeverityLevel(finding.Severity), finding.Message, location.SourceName, Math.Max(1, location.Line), Math.Max(1, location.Column)));
            }
            foreach (var finding in report.DynamicFindings)
            {
                var locations = finding.Locations is { Count: > 0 } ? finding.Locations : new[] { new SourceLocation(report.SourceName, 1, 1, 0, 0) };
                foreach (var location in locations)
                    entries.Add(new Entry(finding.RuleId, finding.Category, "Dynamic/generated-markup heuristic. Runtime/manual verification may be required.", "note", finding.Message, location.SourceName, Math.Max(1, location.Line), Math.Max(1, location.Column)));
            }
        }

        var rules = entries.GroupBy(static x => x.RuleId, StringComparer.Ordinal).Select(static g => new { id = g.Key, name = g.First().Title.Replace(" ", string.Empty, StringComparison.Ordinal), shortDescription = new { text = g.First().Title }, help = new { text = g.First().Help } }).ToArray();
        var results = entries.Select(static x => new { ruleId = x.RuleId, level = x.Level, message = new { text = x.Message }, locations = new[] { new { physicalLocation = new { artifactLocation = new { uri = x.Source.Replace('\\', '/') }, region = new { startLine = x.Line, startColumn = x.Column } } } } }).ToArray();
        var run = new { tool = new { driver = new { name = "Accessibility Analyzer", informationUri = "https://www.section508.gov/", rules } }, results };
        var sarif = new Dictionary<string, object?>
        {
            ["version"] = "2.1.0",
            ["$schema"] = "https://json.schemastore.org/sarif-2.1.0.json",
            ["runs"] = new[] { run }
        };
        return JsonSerializer.Serialize(sarif, IndentedJson);
    }

    private static string SeverityLevel(AnalysisSeverity severity) => severity switch { AnalysisSeverity.Error => "error", AnalysisSeverity.Warning => "warning", _ => "note" };
}
