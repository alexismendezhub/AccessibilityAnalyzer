namespace AccessibilityAnalyzer.Domain;

public sealed record AccessibilityRuleDefinition(
    string RuleId,
    string Title,
    string Category,
    string WcagCriterion,
    string WcagLevel,
    string Section508Applicability,
    FindingStatus DefaultStatus,
    bool Automatic,
    bool RuntimeRecommended,
    string Summary);
