using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AccessibilityAnalyzer.Application.Contracts;
using AccessibilityAnalyzer.Application.Errors;
using AccessibilityAnalyzer.Application.Ports;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibilityAnalyzer.Server.IntegrationTests;

/// <summary>Real HTTP requests against the minimal API endpoints, with the headless browser replaced by a fake.</summary>
public sealed class AccessibilityEndpointsTests
{
    private const string RenderedDom = "<html><head><title>X</title></head><body><button title=\"Save\"></button></body></html>";

    [Fact]
    public async Task RuntimeReturnsTheOriginalAndTheCorrectedDom()
    {
        using var factory = Factory(new FakeRuntime(_ => Task.FromResult(new RuntimeDomSnapshot("https://example.test/", "fake", DateTimeOffset.UtcNow, RenderedDom))));
        using var response = await factory.CreateClient().PostAsJsonAsync("/api/runtime", new { url = "https://example.test/" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(RenderedDom, json.RootElement.GetProperty("originalDom").GetString());
        Assert.Contains("aria-label=\"Save\"", json.RootElement.GetProperty("correctedDom").GetString(), StringComparison.Ordinal);
        Assert.NotEqual(0, json.RootElement.GetProperty("appliedFixes").GetArrayLength());
    }

    [Fact]
    public async Task CaptureTimeoutReturns504WithReadableMessage()
    {
        using var factory = Factory(new FakeRuntime(_ => throw new RuntimeCaptureTimeoutException("The page did not finish rendering within 45 seconds.")));
        using var response = await factory.CreateClient().PostAsJsonAsync("/api/runtime", new { url = "http://yahoo.com" });

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Contains("45 seconds", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BrowserFailureReturns503()
    {
        using var factory = Factory(new FakeRuntime(_ => throw new InvalidOperationException("The page could not be loaded (net::ERR_NAME_NOT_RESOLVED).")));
        using var response = await factory.CreateClient().PostAsJsonAsync("/api/runtime", new { url = "https://example.test/" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task NonHttpRuntimeUrlReturns400()
    {
        using var factory = Factory(new FakeRuntime(_ => throw new InvalidOperationException("must not be called")));
        using var response = await factory.CreateClient().PostAsJsonAsync("/api/runtime", new { url = "file:///C:/Windows/win.ini" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FindingStatusIsSerializedAsText()
    {
        using var factory = Factory();
        using var response = await factory.CreateClient().PostAsJsonAsync("/api/analyze-html", new { html = "<button></button>", sourceName = "x.html" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"status\":\"Error\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SarifIsReturnedWithItsMediaType()
    {
        using var factory = Factory();
        using var response = await factory.CreateClient().PostAsJsonAsync("/api/sarif-sources", new { sources = new[] { new { name = "a.html", content = "<img src='x'>" } } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/sarif+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UnknownApiRouteIs404InsteadOfTheAngularPage()
    {
        using var factory = Factory();
        using var response = await factory.CreateClient().GetAsync(new Uri("/api/does-not-exist", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static WebApplicationFactory<Program> Factory(IRuntimeDomProvider? runtime = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            if (runtime is not null) builder.ConfigureTestServices(services => services.AddSingleton(runtime));
        });

    private sealed record ErrorBody(string Error);

    private sealed class FakeRuntime(Func<string, Task<RuntimeDomSnapshot>> capture) : IRuntimeDomProvider
    {
        public Task<RuntimeDomSnapshot> CaptureAsync(string url, CancellationToken cancellationToken = default) => capture(url);
    }
}
