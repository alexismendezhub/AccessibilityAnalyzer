using AccessibilityAnalyzer.Domain;

namespace AccessibilityAnalyzer.Domain.UnitTests;

public sealed class DynamicHtmlAnalyzerTests
{
    [Fact] public void Analyze_RazorForeach_FindsGeneratedMarkup(){const string source="@foreach (var user in Model.Users) { <tr><td>@user.Name</td><td><button>Edit</button></td></tr> }";var findings=new DynamicHtmlAnalyzer().Analyze(source);Assert.Contains(findings, static f=>f.RuleId=="DYN-GENERATOR-001");}
    [Fact] public void Analyze_RepeatedGridRows_FindsRepeatedMarkup(){const string source="<div class='grid-row'><div class='grid-cell'>A</div></div><div class='grid-row'><div class='grid-cell'>B</div></div><div class='grid-row'><div class='grid-cell'>C</div></div>";var repeated=new DynamicHtmlAnalyzer().Analyze(source).Single(static f=>f.RuleId=="DYN-REPEAT-001"&&f.Message.Contains("grid-row",StringComparison.Ordinal));Assert.True(repeated.Confidence>=0.60);Assert.Equal(3, repeated.Occurrences);}
    [Fact] public void Analyze_InnerHtml_FindsExplicitDynamicHtml(){var findings=new DynamicHtmlAnalyzer().Analyze("target.innerHTML = `<button>${label}</button>`;");Assert.Contains(findings, static f=>f.RuleId=="DYN-EXPLICIT-001");}
    [Fact] public void Analyze_DynamicPattern_ReportsAllSourceLines(){const string source="target.innerHTML = 'a';\nconst x = 1;\ntarget.innerHTML = 'b';";var finding=new DynamicHtmlAnalyzer().Analyze(source,"script.ts").Single(static f=>f.RuleId=="DYN-EXPLICIT-001");Assert.Equal(2, finding.Locations?.Count);Assert.Equal(new[]{1,3}, finding.Locations!.Select(x=>x.Line).ToArray());}
    [Fact] public void Analyze_VirtualScroll_FindsVirtualization(){var findings=new DynamicHtmlAnalyzer().Analyze("<cdk-virtual-scroll-viewport><div aria-rowindex='5'>x</div></cdk-virtual-scroll-viewport>");Assert.Contains(findings, static f=>f.RuleId=="DYN-VIRTUAL-001");}
    [Fact] public void Analyze_KnownGridLibrary_FindsComponentMarker(){var findings=new DynamicHtmlAnalyzer().Analyze("<div class='ag-grid ag-row'>x</div>");Assert.Contains(findings, static f=>f.RuleId=="DYN-LIBRARY-001");}
    [Fact] public void Analyze_SequentialIds_FindsGeneratedPattern(){var findings=new DynamicHtmlAnalyzer().Analyze("<button id='edit1'></button><button id='edit2'></button><button id='edit3'></button>");Assert.Contains(findings, static f=>f.RuleId=="DYN-SEQUENTIAL-ID-001");}
}
