namespace AccessibilityAnalyzer.Domain;

public enum SourceArtifactKind
{
    Unknown,
    HtmlDocument,
    RazorTemplate,
    BlazorComponent,
    AspNetWebForms,
    JavaScript,
    TypeScript,
    ReactJsx,
    VueComponent,
    SvelteComponent,
    CSharpSource
}
