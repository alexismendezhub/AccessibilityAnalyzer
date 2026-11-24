using AccessibilityAnalyzer.Domain;

namespace AccessibilityAnalyzer.Domain.UnitTests;

public sealed class AccessibilityRuleEngineTests
{
    private readonly AccessibilityRuleEngine _engine = new();
    [Fact] public void MissingDocumentLanguageIsError() => Has("<html><head><title>X</title></head><body></body></html>", "DOC-LANG-001", FindingStatus.Error);
    [Fact] public void MissingDocumentTitleIsError() => Has("<html lang='en'><body></body></html>", "DOC-TITLE-001", FindingStatus.Error);
    [Fact] public void MissingImageAltIsError() => Has("<img src='x'>", "IMG-ALT-001", FindingStatus.Error);
    [Fact] public void FilenameAltRequiresReview() => Has("<img src='x.png' alt='x.png'>", "IMG-ALT-QUALITY-001", FindingStatus.ManualReview);
    [Fact] public void ButtonWithoutNameIsError() => Has("<button></button>", "BTN-NAME-001", FindingStatus.Error);
    [Fact] public void ImageInsideLinkCanNameLink(){var r=_engine.Analyze("<a href='/'><img alt='Home' src='h.svg'></a>");Assert.DoesNotContain(r.Findings, x=>x.RuleId=="LINK-NAME-001");}
    [Fact] public void DuplicateIdsAreGrouped(){var r=_engine.Analyze("<div id='x'></div><span id='x'></span>");var f=r.Findings.Single(x=>x.RuleId=="ID-DUP-001");Assert.Equal(2, f.Occurrences);}
    [Fact] public void LabelForMakesInputNamed(){var r=_engine.Analyze("<label for='n'>Name</label><input id='n'>");Assert.DoesNotContain(r.Findings, x=>x.RuleId=="FORM-LABEL-001");}
    [Fact] public void WrappedLabelMakesInputNamed(){var r=_engine.Analyze("<label>Name <input></label>");Assert.DoesNotContain(r.Findings, x=>x.RuleId=="FORM-LABEL-001");}
    [Fact] public void BrokenLabelTargetIsError() => Has("<label for='missing'>Name</label><input id='real'>", "LABEL-FOR-001", FindingStatus.Error);
    [Fact] public void BrokenAriaReferenceIsError() => Has("<button aria-labelledby='missing'></button>", "REF-MISSING-001", FindingStatus.Error);
    [Fact] public void HeadingJumpIsPotentialIssue() => Has("<h1>A</h1><h3>B</h3>", "HEADING-ORDER-001", FindingStatus.PotentialIssue);
    [Fact] public void PositiveTabIndexIsPotentialIssue() => Has("<div tabindex='3'>x</div>", "TABINDEX-POSITIVE-001", FindingStatus.PotentialIssue);
    [Fact] public void ClickableDivRequiresReview() => Has("<div onclick='go()'>Go</div>", "CLICK-SEMANTICS-001", FindingStatus.PotentialIssue);
    [Fact] public void AriaHiddenButtonIsError() => Has("<button aria-hidden='true'>X</button>", "ARIA-HIDDEN-FOCUS-001", FindingStatus.Error);
    [Fact] public void TableWithoutHeadersIsPotentialIssue() => Has("<table><tr><td>A</td></tr></table>", "TABLE-HEADER-001", FindingStatus.PotentialIssue);
    [Fact] public void FrameWithoutTitleIsError() => Has("<iframe src='x'></iframe>", "IFRAME-TITLE-001", FindingStatus.Error);
    [Fact] public void RepeatedUnnamedButtonsAreGrouped(){var r=_engine.Analyze("<button class='edit'></button><button class='edit'></button><button class='edit'></button>");var f=r.Findings.Single(x=>x.RuleId=="BTN-NAME-001");Assert.Equal(3, f.Occurrences);}
    [Fact] public void GroupedFindingsExposeEveryWarningLine(){var r=_engine.Analyze("<button class='edit'></button>\n<p>x</p>\n<button class='edit'></button>\n<button class='edit'></button>", "sample.html");var f=r.Findings.Single(x=>x.RuleId=="BTN-NAME-001");Assert.Equal(3, f.Locations?.Count);Assert.Equal(new[]{1,3,4}, f.Locations!.Select(x=>x.Line).ToArray());}
    [Fact] public void GenericLinkTextIsPotentialIssue() => Has("<a href='/details'>Click here</a>", "LINK-GENERIC-001", FindingStatus.PotentialIssue);
    [Fact] public void VideoWithoutTrackRequiresManualReview() => Has("<video src='x.mp4'></video>", "MEDIA-CAPTIONS-001", FindingStatus.ManualReview);
    private void Has(string html,string id,FindingStatus status){var r=_engine.Analyze(html);Assert.Contains(r.Findings, x=>x.RuleId==id&&x.Status==status);}
}
