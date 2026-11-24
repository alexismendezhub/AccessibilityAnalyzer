namespace AccessibilityAnalyzer.Application.Errors;

public sealed class AnalysisRequestException : Exception
{
    public AnalysisRequestException(string message) : base(message) { }
}
