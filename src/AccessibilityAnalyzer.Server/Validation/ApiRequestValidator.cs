namespace AccessibilityAnalyzer.Server.Validation;
public static class ApiRequestValidator
{
    public const int MaxSources = 2000;
    public const int MaxSourceCharacters = 2 * 1024 * 1024;

    public static string? ValidateUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "Enter an absolute http or https URL.";
        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? null : "Runtime URL must be an absolute http or https URL.";
    }
}
