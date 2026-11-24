namespace AccessibilityAnalyzer.Domain;

public sealed record AnalysisFinding(
    string RuleId,
    string Category,
    AnalysisSeverity Severity,
    double Confidence,
    string Message,
    string Evidence,
    string Recommendation,
    int Occurrences = 1,
    IReadOnlyList<SourceLocation>? Locations = null);
