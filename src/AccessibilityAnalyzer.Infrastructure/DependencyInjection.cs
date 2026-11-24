using AccessibilityAnalyzer.Application.Ports;
using AccessibilityAnalyzer.Infrastructure.Browser;
using AccessibilityAnalyzer.Infrastructure.Reporting;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibilityAnalyzer.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Implementations of the application ports: headless browser DOM capture and SARIF export.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IRuntimeDomProvider>(_ => new ChromiumRuntimeDomProvider());
        services.AddSingleton<ISarifReportWriter, SarifReportWriter>();
        return services;
    }
}
