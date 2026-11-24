using System.Diagnostics;
using System.Text.Json;
using AccessibilityAnalyzer.Application.Contracts;
using AccessibilityAnalyzer.Application.Errors;
using AccessibilityAnalyzer.Application.Ports;

namespace AccessibilityAnalyzer.Infrastructure.Browser;

/// <summary>
/// Captures the rendered DOM of a page with a headless Chromium-based browser (Edge or Chrome) driven over the
/// DevTools protocol. Stdout (<c>--dump-dom</c>) is not used: Edge relaunches itself on Windows, so the started
/// process exits while a child keeps the pipes open and the DOM never arrives.
/// </summary>
public sealed class ChromiumRuntimeDomProvider : IRuntimeDomProvider
{
    public static readonly TimeSpan DefaultLoadTimeout = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan DefaultHardTimeout = TimeSpan.FromSeconds(45);
    private static readonly string[] LinuxBrowserPaths = ["/usr/bin/chromium", "/usr/bin/chromium-browser", "/usr/bin/google-chrome", "/usr/bin/google-chrome-stable"];
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(1500);

    private readonly string? _browserPath;
    private readonly TimeSpan _loadTimeout;
    private readonly TimeSpan _hardTimeout;

    public ChromiumRuntimeDomProvider() : this(null, DefaultLoadTimeout, DefaultHardTimeout) { }

    /// <param name="browserPath">Executable to start; <c>null</c> searches the installed browsers.</param>
    /// <param name="loadTimeout">After this time loading is stopped and the DOM is captured as it is (slow pages, endless requests).</param>
    /// <param name="hardTimeout">After this time the browser is closed and a <see cref="RuntimeCaptureTimeoutException"/> is thrown.</param>
    public ChromiumRuntimeDomProvider(string? browserPath, TimeSpan loadTimeout, TimeSpan hardTimeout)
    {
        if (loadTimeout <= TimeSpan.Zero || hardTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(loadTimeout), "Timeouts must be positive.");
        _browserPath = browserPath;
        _loadTimeout = loadTimeout;
        _hardTimeout = hardTimeout;
    }

    public async Task<RuntimeDomSnapshot> CaptureAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new AnalysisRequestException("Runtime analysis accepts only absolute http/https URLs.");

        var browser = _browserPath ?? FindBrowser() ?? throw new InvalidOperationException("No supported Chromium/Chrome/Edge executable was found. Install a Chromium-based browser or use static analysis.");
        var profile = Path.Combine(Path.GetTempPath(), "accessibility-analyzer-browser-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile);

        var psi = new ProcessStartInfo(browser) { UseShellExecute = false, CreateNoWindow = true };
        if (!OperatingSystem.IsWindows()) psi.ArgumentList.Add("--no-sandbox");
        foreach (var arg in new[] { "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--disable-extensions",
                     "--disable-dev-shm-usage", "--remote-debugging-port=0", $"--user-data-dir={profile}", "about:blank" })
            psi.ArgumentList.Add(arg);

        using var hard = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        hard.CancelAfter(_hardTimeout);
        Process? launcher = null;
        CdpConnection? cdp = null;
        var browserProcessIds = new List<int>();
        try
        {
            launcher = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start the headless browser.");
            var endpoint = await WaitForDevToolsEndpointAsync(profile, launcher, hard.Token).ConfigureAwait(false);
            cdp = await CdpConnection.ConnectAsync(endpoint, hard.Token).ConfigureAwait(false);
            browserProcessIds.AddRange(await GetBrowserProcessIdsAsync(cdp, hard.Token).ConfigureAwait(false));

            var target = await cdp.SendAsync("Target.createTarget", new { url = "about:blank" }, null, hard.Token).ConfigureAwait(false);
            var attached = await cdp.SendAsync("Target.attachToTarget", new { targetId = target.GetProperty("targetId").GetString(), flatten = true }, null, hard.Token).ConfigureAwait(false);
            var session = attached.GetProperty("sessionId").GetString();

            var navigation = await cdp.SendAsync("Page.navigate", new { url = uri.AbsoluteUri }, session, hard.Token).ConfigureAwait(false);
            if (navigation.TryGetProperty("errorText", out var errorText) && !string.IsNullOrEmpty(errorText.GetString()))
                throw new InvalidOperationException($"The page could not be loaded ({errorText.GetString()}).");

            await WaitForLoadAsync(cdp, session, hard.Token).ConfigureAwait(false);
            var html = await cdp.SendAsync("Runtime.evaluate", new
            {
                expression = "(document.doctype ? new XMLSerializer().serializeToString(document.doctype) + '\\n' : '') + (document.documentElement ? document.documentElement.outerHTML : '')",
                returnByValue = true
            }, session, hard.Token).ConfigureAwait(false);
            var dom = html.GetProperty("result").TryGetProperty("value", out var value) ? value.GetString() : null;
            if (string.IsNullOrWhiteSpace(dom)) throw new InvalidOperationException("The headless browser returned an empty DOM.");
            return new(uri.AbsoluteUri, browser, DateTimeOffset.UtcNow, dom);
        }
        catch (OperationCanceledException) when (hard.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new RuntimeCaptureTimeoutException($"The page did not finish rendering within {_hardTimeout.TotalSeconds:0} seconds. Try again or analyze a lighter page.");
        }
        finally
        {
            await ShutdownAsync(cdp, launcher, browserProcessIds).ConfigureAwait(false);
            DeleteProfile(profile);
        }
    }

    private static async Task<Uri> WaitForDevToolsEndpointAsync(string profile, Process launcher, CancellationToken cancellationToken)
    {
        var portFile = Path.Combine(profile, "DevToolsActivePort");
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(portFile))
            {
                string[] lines;
                try { lines = await File.ReadAllLinesAsync(portFile, cancellationToken).ConfigureAwait(false); }
                catch (IOException) { lines = []; }
                if (lines.Length >= 2 && int.TryParse(lines[0], out var port) && port > 0)
                    return new Uri($"ws://127.0.0.1:{port}{lines[1].Trim()}");
            }
            // Edge relaunches itself, so an exit with code 0 is normal; a failing exit before the port appears is not.
            if (launcher.HasExited && launcher.ExitCode != 0 && !File.Exists(portFile))
                throw new InvalidOperationException($"The headless browser exited with code {launcher.ExitCode} before it was ready.");
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<IReadOnlyList<int>> GetBrowserProcessIdsAsync(CdpConnection cdp, CancellationToken cancellationToken)
    {
        try
        {
            var info = await cdp.SendAsync("SystemInfo.getProcessInfo", null, null, cancellationToken).ConfigureAwait(false);
            return info.GetProperty("processInfo").EnumerateArray().Select(static p => p.GetProperty("id").GetInt32()).ToArray();
        }
        catch (InvalidOperationException) { return []; }
    }

    private async Task WaitForLoadAsync(CdpConnection cdp, string? session, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        TimeSpan? completeAt = null;
        while (stopwatch.Elapsed < _loadTimeout)
        {
            var state = await cdp.SendAsync("Runtime.evaluate", new { expression = "document.readyState", returnByValue = true }, session, cancellationToken).ConfigureAwait(false);
            var readyState = state.GetProperty("result").TryGetProperty("value", out var v) ? v.GetString() : null;
            if (readyState == "complete") completeAt ??= stopwatch.Elapsed;
            // Give scripts a moment after the load event to render client-side markup.
            if (completeAt is not null && stopwatch.Elapsed - completeAt >= SettleDelay) return;
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }
        // Slow page (ads, long polling): stop loading and capture what has rendered so far.
        await cdp.SendAsync("Page.stopLoading", null, session, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ShutdownAsync(CdpConnection? cdp, Process? launcher, List<int> browserProcessIds)
    {
        if (cdp is not null)
        {
            try
            {
                using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await cdp.SendAsync("Browser.close", null, null, closeTimeout.Token).ConfigureAwait(false);
            }
            catch (Exception) { /* the browser may already be gone; the processes are killed below */ }
            await cdp.DisposeAsync().ConfigureAwait(false);
        }

        var deadline = DateTime.UtcNow.AddSeconds(5);
        foreach (var id in browserProcessIds)
        {
            try
            {
                using var process = Process.GetProcessById(id);
                var remaining = deadline - DateTime.UtcNow;
                if (remaining > TimeSpan.Zero && process.WaitForExit(remaining)) continue;
                process.Kill(entireProcessTree: true);
            }
            catch (ArgumentException) { /* already exited */ }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        if (launcher is not null)
        {
            try { if (!launcher.HasExited) launcher.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            launcher.Dispose();
        }
    }

    private static void DeleteProfile(string profile)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try { if (Directory.Exists(profile)) Directory.Delete(profile, recursive: true); return; }
            catch (IOException) { Thread.Sleep(200); }
            catch (UnauthorizedAccessException) { Thread.Sleep(200); }
        }
    }

    public static string? FindBrowser()
    {
        var candidates = new List<string>();
        var configured = Environment.GetEnvironmentVariable("ACCESSIBILITY_ANALYZER_BROWSER");
        if (!string.IsNullOrWhiteSpace(configured)) candidates.Add(configured);
        if (OperatingSystem.IsWindows())
        {
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            candidates.AddRange(new[] {
                Path.Combine(pf, "Microsoft", "Edge", "Application", "msedge.exe"), Path.Combine(pfx86, "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(pf, "Google", "Chrome", "Application", "chrome.exe"), Path.Combine(pfx86, "Google", "Chrome", "Application", "chrome.exe"), Path.Combine(local, "Google", "Chrome", "Application", "chrome.exe") });
        }
        else candidates.AddRange(LinuxBrowserPaths);
        return candidates.FirstOrDefault(File.Exists);
    }
}
