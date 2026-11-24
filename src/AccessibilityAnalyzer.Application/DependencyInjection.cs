using AccessibilityAnalyzer.Application.Services;
using AccessibilityAnalyzer.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibilityAnalyzer.Application;

public static class DependencyInjection
{
    /// <summary>Domain engines and the application service. The ports (browser, SARIF) come from <c>AddInfrastructure</c>.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<DynamicHtmlAnalyzer>();
        services.AddSingleton<AccessibilityRuleEngine>();
        services.AddSingleton<SafeFixEngine>();
        services.AddSingleton<RuntimeAccessibilityReviewAnalyzer>();
        services.AddSingleton<AccessibilityAnalysisService>();
        return services;
    }
}
