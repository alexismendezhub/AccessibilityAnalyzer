using AccessibilityAnalyzer.Application.Contracts;
using AccessibilityAnalyzer.Application.Errors;
using AccessibilityAnalyzer.Application.Services;
using AccessibilityAnalyzer.Server.Contracts;
using AccessibilityAnalyzer.Server.Validation;

namespace AccessibilityAnalyzer.Server.Endpoints;

/// <summary>Minimal API endpoints under <c>/api</c>. Every handler only validates sizes, calls the application service and maps errors to HTTP codes.</summary>
public static class AccessibilityEndpoints
{
    public static IEndpointRouteBuilder MapAccessibilityEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");
        api.MapGet("/rules", Rules);
        api.MapPost("/analyze-html", AnalyzeHtml);
        api.MapPost("/analyze-and-fix-html", AnalyzeAndFixHtml);
        api.MapPost("/analyze-sources", AnalyzeSources);
        api.MapPost("/apply-safe-fixes", ApplySafeFixes);
        api.MapPost("/runtime", RuntimeAsync);
        api.MapPost("/sarif-sources", SarifSources);
        return app;
    }

    public static IResult Rules(AccessibilityAnalysisService service) => Results.Ok(service.GetRules());

    public static IResult AnalyzeHtml(AnalyzeHtmlRequest request, AccessibilityAnalysisService service)
    {
        var html = request.Html ?? string.Empty;
        if (html.Length > ApiRequestValidator.MaxSourceCharacters) return Error(StatusCodes.Status400BadRequest, "HTML input exceeds the 2 MiB analysis limit.");
        return Results.Ok(service.AnalyzeHtml(html, request.SourceName));
    }

    public static IResult AnalyzeAndFixHtml(AnalyzeAndFixHtmlRequest request, AccessibilityAnalysisService service)
    {
        var html = request.Html ?? string.Empty;
        if (html.Length > ApiRequestValidator.MaxSourceCharacters) return Error(StatusCodes.Status400BadRequest, "HTML input exceeds the 2 MiB analysis/correction limit.");
        try { return Results.Ok(service.AnalyzeHtmlWithSafeFixes(html, request.SourceName, request.PreferredLanguage)); }
        catch (ArgumentException ex) { return Error(StatusCodes.Status400BadRequest, ex.Message); }
    }

    public static IResult AnalyzeSources(AnalyzeSourcesRequest request, AccessibilityAnalysisService service)
    {
        var invalid = ValidateSources(request);
        if (invalid is not null) return invalid;
        return Results.Ok(service.AnalyzeSources(ToInputs(request)));
    }

    public static IResult ApplySafeFixes(SafeFixRequest request, AccessibilityAnalysisService service)
    {
        var html = request.Html ?? string.Empty;
        if (html.Length > ApiRequestValidator.MaxSourceCharacters) return Error(StatusCodes.Status400BadRequest, "HTML input exceeds the 2 MiB correction limit.");
        try { return Results.Ok(service.ApplySafeFixes(html, request.PreferredLanguage)); }
        catch (ArgumentException ex) { return Error(StatusCodes.Status400BadRequest, ex.Message); }
    }

    public static async Task<IResult> RuntimeAsync(RuntimeRequest request, AccessibilityAnalysisService service, CancellationToken cancellationToken)
    {
        var validation = ApiRequestValidator.ValidateUrl(request.Url);
        if (validation is not null) return Error(StatusCodes.Status400BadRequest, validation);
        try { return Results.Ok(await service.AnalyzeRuntimeAsync(request.Url!, cancellationToken).ConfigureAwait(false)); }
        catch (AnalysisRequestException ex) { return Error(StatusCodes.Status400BadRequest, ex.Message); }
        catch (RuntimeCaptureTimeoutException ex) { return Error(StatusCodes.Status504GatewayTimeout, ex.Message); }
        catch (InvalidOperationException ex) { return Error(StatusCodes.Status503ServiceUnavailable, ex.Message); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return Results.StatusCode(StatusCodes.Status499ClientClosedRequest); }
    }

    public static IResult SarifSources(AnalyzeSourcesRequest request, AccessibilityAnalysisService service)
    {
        var invalid = ValidateSources(request);
        if (invalid is not null) return invalid;
        return Results.Text(service.WriteSarifForSources(ToInputs(request)), "application/sarif+json");
    }

    private static IResult? ValidateSources(AnalyzeSourcesRequest request)
    {
        var sources = request.Sources ?? Array.Empty<SourceInputRequest>();
        if (sources.Count > ApiRequestValidator.MaxSources) return Error(StatusCodes.Status400BadRequest, $"At most {ApiRequestValidator.MaxSources} source files can be analyzed in one request.");
        if (sources.Any(static source => source.Content is not null && source.Content.Length > ApiRequestValidator.MaxSourceCharacters))
            return Error(StatusCodes.Status400BadRequest, "One or more source files exceed the 2 MiB per-file analysis limit.");
        return null;
    }

    private static IEnumerable<SourceInput> ToInputs(AnalyzeSourcesRequest request) =>
        (request.Sources ?? Array.Empty<SourceInputRequest>()).Select(static source => new SourceInput(NormalizeName(source.Name), source.Content ?? string.Empty));

    private static IResult Error(int statusCode, string message) => Results.Json(new ApiError(message), statusCode: statusCode);

    private static string NormalizeName(string? name) => string.IsNullOrWhiteSpace(name) ? "browser-selected-source" : name.Trim();
}
