using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using AccessibilityAnalyzer.Application.Errors;
using AccessibilityAnalyzer.Infrastructure.Browser;

namespace AccessibilityAnalyzer.Infrastructure.IntegrationTests;

public sealed class ChromiumRuntimeDomProviderTests
{
    [Fact]
    public async Task NonHttpUrlIsRejected()
    {
        var provider = new ChromiumRuntimeDomProvider("unused", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        await Assert.ThrowsAsync<AnalysisRequestException>(() => provider.CaptureAsync("file:///C:/Windows/win.ini"));
    }

    [Fact]
    public async Task BrowserThatNeverBecomesReadyFailsWithTimeoutInsteadOfHanging()
    {
        var fake = CreateHangingExecutable();
        try
        {
            var provider = new ChromiumRuntimeDomProvider(fake, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
            var stopwatch = Stopwatch.StartNew();
            var error = await Assert.ThrowsAsync<RuntimeCaptureTimeoutException>(() => provider.CaptureAsync("https://example.test/"));
            Assert.True(stopwatch.Elapsed.TotalSeconds < 15, "The capture must stop at the hard timeout.");
            Assert.Contains("2 seconds", error.Message);
        }
        finally { TryDelete(fake); }
    }

    [Fact]
    public async Task CallerCancellationIsNotReportedAsTimeout()
    {
        var fake = CreateHangingExecutable();
        try
        {
            var provider = new ChromiumRuntimeDomProvider(fake, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30));
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            var thrown = await CatchAsync(() => provider.CaptureAsync("https://example.test/", cancel.Token));
            Assert.IsAssignableFrom<OperationCanceledException>(thrown);
        }
        finally { TryDelete(fake); }
    }

    [SkippableFact]
    public async Task SlowPageIsCapturedAfterTheLoadTimeoutWithScriptRenderedMarkup()
    {
        var browser = ChromiumRuntimeDomProvider.FindBrowser();
        Skip.If(browser is null, "No Chromium-based browser is installed on this machine.");
        // The image request never answers, so the load event never fires (like pages full of ads/trackers).
        await using var server = await TestHttpServer.StartAsync(hangingImage: true);
        var provider = new ChromiumRuntimeDomProvider(browser, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(40));
        var stopwatch = Stopwatch.StartNew();
        var snapshot = await provider.CaptureAsync(server.BaseUrl);
        Assert.True(stopwatch.Elapsed.TotalSeconds < 30);
        Assert.Contains("id=\"rendered-by-script\"", snapshot.Dom);
        Assert.Contains("<!DOCTYPE html>", snapshot.Dom);
    }

    [SkippableFact]
    public async Task FastPageIsCaptured()
    {
        var browser = ChromiumRuntimeDomProvider.FindBrowser();
        Skip.If(browser is null, "No Chromium-based browser is installed on this machine.");
        await using var server = await TestHttpServer.StartAsync(hangingImage: false);
        var provider = new ChromiumRuntimeDomProvider(browser, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(40));
        var snapshot = await provider.CaptureAsync(server.BaseUrl);
        Assert.Contains("id=\"rendered-by-script\"", snapshot.Dom);
        Assert.Contains("<h1>Runtime fixture</h1>", snapshot.Dom);
    }

    private static async Task<Exception?> CatchAsync(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception ex) { return ex; }
    }

    private static string CreateHangingExecutable()
    {
        var path = Path.Combine(Path.GetTempPath(), "aa-fake-browser-" + Guid.NewGuid().ToString("N") + (OperatingSystem.IsWindows() ? ".cmd" : ".sh"));
        if (OperatingSystem.IsWindows()) File.WriteAllText(path, "@ping -n 120 127.0.0.1 >nul\r\n");
        else
        {
            File.WriteAllText(path, "#!/bin/sh\nsleep 120\n");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return path;
    }

    private static void TryDelete(string path) { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    /// <summary>Tiny HTTP server on loopback (no URL ACL needed) serving a page whose markup is completed by script.</summary>
    private sealed class TestHttpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stop = new();
        private readonly bool _hangingImage;
        private readonly List<TcpClient> _held = [];
        private Task? _loop;

        private TestHttpServer(bool hangingImage)
        {
            _hangingImage = hangingImage;
            _listener = new TcpListener(IPAddress.Loopback, 0);
        }

        public string BaseUrl => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";

        public static Task<TestHttpServer> StartAsync(bool hangingImage)
        {
            var server = new TestHttpServer(hangingImage);
            server._listener.Start();
            server._loop = Task.Run(server.AcceptLoopAsync);
            return Task.FromResult(server);
        }

        private async Task AcceptLoopAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
                catch (Exception) { return; }
                _ = Task.Run(() => HandleAsync(client));
            }
        }

        private async Task HandleAsync(TcpClient client)
        {
            try
            {
                var stream = client.GetStream();
                var buffer = new byte[8192];
                var read = await stream.ReadAsync(buffer, _stop.Token);
                var requestLine = Encoding.ASCII.GetString(buffer, 0, read).Split("\r\n")[0];
                if (requestLine.Contains(" /hang.png", StringComparison.Ordinal))
                {
                    lock (_held) _held.Add(client); // never answer
                    return;
                }
                var image = _hangingImage ? "<img src=\"/hang.png\" alt=\"\">" : string.Empty;
                var body = requestLine.StartsWith("GET / ", StringComparison.Ordinal)
                    ? "<!DOCTYPE html><html lang=\"en\"><head><title>Fixture</title></head><body><h1>Runtime fixture</h1>" + image +
                      "<script>document.addEventListener('DOMContentLoaded',function(){var p=document.createElement('p');p.id='rendered-by-script';p.textContent='ok';document.body.appendChild(p);});</script></body></html>"
                    : string.Empty;
                var status = body.Length > 0 ? "200 OK" : "404 Not Found";
                var bytes = Encoding.UTF8.GetBytes(body);
                var header = $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header), _stop.Token);
                await stream.WriteAsync(bytes, _stop.Token);
                client.Dispose();
            }
            catch (Exception) { client.Dispose(); }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            if (_loop is not null) { try { await _loop; } catch (Exception) { } }
            lock (_held) { foreach (var client in _held) client.Dispose(); }
            _stop.Dispose();
        }
    }
}
