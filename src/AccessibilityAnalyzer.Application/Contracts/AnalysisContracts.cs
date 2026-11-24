using AccessibilityAnalyzer.Domain;

namespace AccessibilityAnalyzer.Application.Contracts;

public sealed record SourceArtifact(string Path, SourceArtifactKind Kind, bool ContainsHtml, IReadOnlyList<AnalysisFinding> DynamicFindings, bool HtmlComponent = false);
public sealed record SourceAnalysisResult(SourceArtifact Artifact, AccessibilityReport Report, string CorrectedContent, IReadOnlyList<string> AppliedFixes);
public sealed record RuntimeDomSnapshot(string Url, string BrowserExecutable, DateTimeOffset CapturedAt, string Dom);
public sealed record RuntimeAnalysisResult(string Url, string BrowserExecutable, DateTimeOffset CapturedAt, AccessibilityReport Report, string OriginalDom, string CorrectedDom, IReadOnlyList<string> AppliedFixes);
public sealed record SourceInput(string Name, string Content);
