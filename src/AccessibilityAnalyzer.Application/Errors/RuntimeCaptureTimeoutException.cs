namespace AccessibilityAnalyzer.Application.Errors;

public sealed class RuntimeCaptureTimeoutException : Exception
{
    public RuntimeCaptureTimeoutException(string message) : base(message) { }
}
