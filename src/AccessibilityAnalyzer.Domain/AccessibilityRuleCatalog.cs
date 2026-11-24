namespace AccessibilityAnalyzer.Domain;

public static class AccessibilityRuleCatalog
{
    public static IReadOnlyList<AccessibilityRuleDefinition> All { get; } = new[]
    {
        R("DOC-LANG-001", "Document language", "Document", "3.1.1", "A", FindingStatus.Error, true, false, "The page must expose its default human language."),
        R("DOC-TITLE-001", "Page title", "Document", "2.4.2", "A", FindingStatus.Error, true, false, "Documents need a descriptive non-empty title."),
        R("IMG-ALT-001", "Image text alternative", "Images", "1.1.1", "A", FindingStatus.Error, true, false, "Images require an alt attribute; semantic quality may still require review."),
        R("IMG-ALT-QUALITY-001", "Image alternative quality", "Images", "1.1.1", "A", FindingStatus.ManualReview, true, false, "Filename-like or placeholder alt text requires human review."),
        R("FORM-LABEL-001", "Form control accessible name", "Forms", "1.3.1 / 3.3.2 / 4.1.2", "A", FindingStatus.Error, true, false, "User-input controls need a programmatically determinable accessible name."),
        R("LABEL-FOR-001", "Label target", "Forms", "1.3.1 / 4.1.2", "A", FindingStatus.Error, true, false, "A label for attribute must reference an existing form-control id."),
        R("BTN-NAME-001", "Button accessible name", "Interactive", "4.1.2", "A", FindingStatus.Error, true, false, "Buttons require an accessible name."),
        R("LINK-NAME-001", "Link accessible name", "Interactive", "2.4.4 / 4.1.2", "A", FindingStatus.Error, true, false, "Links require a discernible accessible name."),
        R("LINK-GENERIC-001", "Ambiguous link text", "Interactive", "2.4.4", "A", FindingStatus.PotentialIssue, true, false, "Generic link text may not identify purpose from its programmatic context."),
        R("HEADING-ORDER-001", "Heading hierarchy", "Structure", "1.3.1 / 2.4.6", "A/AA", FindingStatus.PotentialIssue, true, false, "Heading levels should form a meaningful hierarchy."),
        R("HEADING-EMPTY-001", "Empty heading", "Structure", "1.3.1 / 2.4.6", "A/AA", FindingStatus.PotentialIssue, true, false, "Empty headings add misleading document structure."),
        R("ID-DUP-001", "Duplicate ID", "Parsing", "4.1.1", "A", FindingStatus.Error, true, false, "Duplicate IDs can break programmatic relationships."),
        R("REF-MISSING-001", "Broken ID reference", "ARIA", "1.3.1 / 4.1.2", "A", FindingStatus.Error, true, false, "ARIA and table relationships must reference IDs that exist."),
        R("TABLE-HEADER-001", "Data table headers", "Tables", "1.3.1", "A", FindingStatus.PotentialIssue, true, false, "Tabular data requires programmatic header relationships."),
        R("TABLE-SCOPE-001", "Simple table header scope", "Tables", "1.3.1", "A", FindingStatus.PotentialIssue, true, false, "Simple row/column header relationships should be explicit when ambiguity is possible."),
        R("TABLE-CAPTION-001", "Table identification", "Tables", "1.3.1 / 2.4.6", "A/AA", FindingStatus.ManualReview, true, false, "Data tables should be identifiable in context; a caption is often the clearest mechanism."),
        R("IFRAME-TITLE-001", "Frame title", "Frames", "2.4.1 / 4.1.2", "A", FindingStatus.Error, true, false, "Frames need an accessible title."),
        R("TABINDEX-POSITIVE-001", "Positive tabindex", "Keyboard", "2.1.1 / 2.4.3", "A", FindingStatus.PotentialIssue, true, true, "Positive tabindex can create an unexpected keyboard focus order."),
        R("CLICK-SEMANTICS-001", "Non-semantic click target", "Keyboard", "2.1.1 / 4.1.2", "A", FindingStatus.PotentialIssue, true, true, "Clickable non-interactive elements require keyboard operation and semantics."),
        R("ARIA-HIDDEN-FOCUS-001", "Focusable content hidden from AT", "ARIA", "4.1.2", "A", FindingStatus.Error, true, true, "Focusable controls must not be hidden from assistive technology."),
        R("INPUT-IMAGE-ALT-001", "Image input text alternative", "Forms", "1.1.1 / 4.1.2", "A", FindingStatus.Error, true, false, "Image submit controls require alt text."),
        R("META-REFRESH-001", "Automatic refresh/redirect", "Timing", "2.2.1", "A", FindingStatus.ManualReview, true, true, "Automatic refresh or redirect requires timing and user-control review."),
        R("SVG-NAME-001", "Meaningful SVG accessible name", "Images", "1.1.1 / 4.1.2", "A", FindingStatus.ManualReview, true, false, "Meaningful SVG content may require an accessible name; decorative SVG should be hidden appropriately."),
        R("MEDIA-CAPTIONS-001", "Video captions", "Media", "1.2.2", "A", FindingStatus.ManualReview, true, true, "Prerecorded video with audio requires captions; automation cannot prove caption quality or embedded caption tracks."),
        R("MEDIA-AUDIO-001", "Audio alternative", "Media", "1.2.1", "A", FindingStatus.ManualReview, true, true, "Prerecorded audio-only content requires an equivalent alternative; automation cannot prove transcript equivalence."),
        R("AUTOPLAY-AUDIO-001", "Automatic audio", "Media", "1.4.2", "A", FindingStatus.ManualReview, true, true, "Automatically playing audio longer than three seconds needs a stop/pause or independent volume mechanism."),
        R("BYPASS-BLOCKS-001", "Bypass repeated blocks", "Navigation", "2.4.1", "A", FindingStatus.ManualReview, true, true, "Pages with repeated navigation need a mechanism to bypass repeated blocks."),
        R("RUNTIME-FOCUS-001", "Runtime focus behavior", "Runtime", "2.4.3 / 2.4.7", "A/AA", FindingStatus.ManualReview, false, true, "Dynamic UI focus behavior requires runtime/manual verification."),
        R("RUNTIME-KEYBOARD-001", "Runtime keyboard behavior", "Runtime", "2.1.1 / 2.1.2", "A", FindingStatus.ManualReview, false, true, "Complex widgets require keyboard and keyboard-trap verification."),
    };

    private static AccessibilityRuleDefinition R(string id, string title, string category, string wcag, string level, FindingStatus status, bool automatic, bool runtime, string summary) =>
        new(id, title, category, wcag, level, "Revised Section 508 web baseline incorporates WCAG 2.0 Level A/AA criteria where applicable; manual review remains necessary where automation cannot establish conformance.", status, automatic, runtime, summary);
}
