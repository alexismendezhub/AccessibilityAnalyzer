using AccessibilityAnalyzer.Domain; using AccessibilityAnalyzer.Infrastructure.Reporting;
namespace AccessibilityAnalyzer.Infrastructure.IntegrationTests;

public sealed class SarifReportWriterTests { [Fact] public void WriterEmitsSchemaRuleAndLocation(){var report=new AccessibilityRuleEngine().Analyze("<img src='x'>","sample.html");var sarif=new SarifReportWriter().Write(new[]{report});Assert.Contains("\"$schema\"", sarif);Assert.Contains("IMG-ALT-001", sarif);Assert.Contains("sample.html", sarif);} }
