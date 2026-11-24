using AccessibilityAnalyzer.Domain;
namespace AccessibilityAnalyzer.Application.Ports;
public interface ISarifReportWriter { string Write(IEnumerable<AccessibilityReport> reports); }
