namespace AccessibilityAnalyzer.Domain;

public sealed record SourceLocation(
    string SourceName,
    int Line,
    int Column,
    int StartIndex,
    int Length);
