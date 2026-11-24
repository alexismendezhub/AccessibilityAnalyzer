using AccessibilityAnalyzer.Domain;
namespace AccessibilityAnalyzer.Domain.UnitTests;

public sealed class SourceClassificationTests { [Fact] public void RazorComponentIsBlazorComponent()=>Assert.Equal(SourceArtifactKind.BlazorComponent, HtmlContentDetector.Classify("component.razor")); }
