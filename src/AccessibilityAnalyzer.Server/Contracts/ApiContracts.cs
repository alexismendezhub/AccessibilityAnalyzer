namespace AccessibilityAnalyzer.Server.Contracts;
public sealed record AnalyzeHtmlRequest(string? Html, string? SourceName);
public sealed record AnalyzeAndFixHtmlRequest(string? Html, string? SourceName, string? PreferredLanguage);
public sealed record SourceInputRequest(string? Name, string? Content);
public sealed record AnalyzeSourcesRequest(IReadOnlyList<SourceInputRequest>? Sources);
public sealed record SafeFixRequest(string? Html, string? PreferredLanguage);
public sealed record RuntimeRequest(string? Url);
public sealed record ApiError(string Error);
