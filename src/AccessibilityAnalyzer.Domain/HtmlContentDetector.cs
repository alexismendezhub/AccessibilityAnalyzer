using System.Text.RegularExpressions;

namespace AccessibilityAnalyzer.Domain;

public static partial class HtmlContentDetector
{
    // Files whose primary content is HTML markup. Only these are ever rewritten by safe fixes;
    // every other candidate (JavaScript, TypeScript, JSX, C#) is analyzed and reported but left byte-for-byte unchanged.
    private static readonly HashSet<string> HtmlComponentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".htm", ".xhtml", ".cshtml", ".vbhtml", ".razor", ".aspx", ".ascx", ".master", ".vue", ".svelte"
    };

    private static readonly HashSet<string> CandidateExtensions = new(HtmlComponentExtensions, StringComparer.OrdinalIgnoreCase)
    {
        ".js", ".mjs", ".cjs", ".ts", ".jsx", ".tsx", ".cs"
    };

    public static bool IsCandidateFile(string path) => CandidateExtensions.Contains(Path.GetExtension(path));

    public static bool IsHtmlComponent(string path) => HtmlComponentExtensions.Contains(Path.GetExtension(path));

    public static bool ContainsHtml(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return HtmlTagRegex().IsMatch(content) || FrameworkMarkupRegex().IsMatch(content);
    }

    public static SourceArtifactKind Classify(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" or ".htm" or ".xhtml" => SourceArtifactKind.HtmlDocument,
            ".cshtml" or ".vbhtml" => SourceArtifactKind.RazorTemplate,
            ".razor" => SourceArtifactKind.BlazorComponent,
            ".aspx" or ".ascx" or ".master" => SourceArtifactKind.AspNetWebForms,
            ".js" or ".mjs" or ".cjs" => SourceArtifactKind.JavaScript,
            ".ts" => SourceArtifactKind.TypeScript,
            ".jsx" or ".tsx" => SourceArtifactKind.ReactJsx,
            ".vue" => SourceArtifactKind.VueComponent,
            ".svelte" => SourceArtifactKind.SvelteComponent,
            ".cs" => SourceArtifactKind.CSharpSource,
            _ => SourceArtifactKind.Unknown
        };
    }

    [GeneratedRegex(@"<\s*(?:html|body|main|header|footer|nav|section|article|div|span|table|thead|tbody|tfoot|tr|th|td|form|label|input|select|textarea|button|a|img|ul|ol|li|template|dialog)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex("""(?:dangerouslySetInnerHTML|\[innerHTML\]|v-html\s*=|@Html\.Raw\s*\(|<template\b|<script\b[^>]*type\s*=\s*["']text/html["'])""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FrameworkMarkupRegex();
}
