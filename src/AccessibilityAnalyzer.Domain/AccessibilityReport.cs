namespace AccessibilityAnalyzer.Domain;

public sealed record AccessibilityReport(
    string SourceName,
    DateTimeOffset AnalyzedAt,
    IReadOnlyList<AccessibilityFinding> Findings,
    IReadOnlyList<AnalysisFinding> DynamicFindings)
{
    public int Errors => Findings.Count(static f => f.Status == FindingStatus.Error);
    public int PotentialIssues => Findings.Count(static f => f.Status == FindingStatus.PotentialIssue);
    public int ManualReviews => Findings.Count(static f => f.Status == FindingStatus.ManualReview);
    public int DynamicCount => DynamicFindings.Count;
}
