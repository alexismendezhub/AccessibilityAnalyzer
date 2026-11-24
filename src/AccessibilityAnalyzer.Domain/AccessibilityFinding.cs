namespace AccessibilityAnalyzer.Domain;

public sealed record AccessibilityFinding(
    string RuleId,
    string Category,
    FindingStatus Status,
    AnalysisSeverity Severity,
    double Confidence,
    string Message,
    string Evidence,
    string Recommendation,
    string WcagCriterion,
    string WcagLevel,
    string Section508Applicability,
    SourceLocation Location,
    int Occurrences = 1,
    string? GroupKey = null,
    bool SafeFixAvailable = false,
    string? SafeFixDescription = null,
    IReadOnlyList<SourceLocation>? Locations = null);
