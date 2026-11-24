using AccessibilityAnalyzer.Application.Contracts;
namespace AccessibilityAnalyzer.Application.Ports;
public interface IRuntimeDomProvider { Task<RuntimeDomSnapshot> CaptureAsync(string url, CancellationToken cancellationToken = default); }
