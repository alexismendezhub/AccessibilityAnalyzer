using AccessibilityAnalyzer.Server.Validation;
namespace AccessibilityAnalyzer.Server.IntegrationTests;

public sealed class ApiRequestValidatorTests
{
    [Fact]
    public void InvalidRuntimeUrlIsRejected() => Assert.NotNull(ApiRequestValidator.ValidateUrl("file:///tmp/test.html"));

    [Fact]
    public void HttpRuntimeUrlIsAccepted() => Assert.Null(ApiRequestValidator.ValidateUrl("https://example.test/page"));
}
