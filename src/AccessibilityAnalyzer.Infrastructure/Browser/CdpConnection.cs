using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AccessibilityAnalyzer.Infrastructure.Browser;

/// <summary>Minimal Chrome DevTools Protocol client over the browser WebSocket (flattened sessions).</summary>
internal sealed class CdpConnection : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _closing = new();
    private Task? _receiveLoop;
    private int _nextId;

    public static async Task<CdpConnection> ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        var connection = new CdpConnection();
        connection._socket.Options.KeepAliveInterval = TimeSpan.Zero;
        await connection._socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
        connection._receiveLoop = Task.Run(connection.ReceiveLoopAsync, CancellationToken.None);
        return connection;
    }

    public async Task<JsonElement> SendAsync(string method, object? parameters, string? sessionId, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        var message = new JsonObject { ["id"] = id, ["method"] = method, ["params"] = JsonSerializer.SerializeToNode(parameters ?? new { }) };
        if (sessionId is not null) message["sessionId"] = sessionId;
        var bytes = Encoding.UTF8.GetBytes(message.ToJsonString());
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false); }
        finally { _sendLock.Release(); }
        using (cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken)))
        {
            try { return await completion.Task.ConfigureAwait(false); }
            finally { _pending.TryRemove(id, out _); }
        }
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        try
        {
            while (_socket.State == WebSocketState.Open && !_closing.IsCancellationRequested)
            {
                var result = await _socket.ReceiveAsync(buffer, _closing.Token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) break;
                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;
                Dispatch(message.ToArray());
                message.SetLength(0);
            }
        }
        catch (Exception) { /* socket closed or browser gone: pending calls fail below */ }
        foreach (var pending in _pending.Values) pending.TrySetException(new InvalidOperationException("The headless browser closed the DevTools connection."));
    }

    private void Dispatch(byte[] payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        if (!root.TryGetProperty("id", out var idElement) || !_pending.TryGetValue(idElement.GetInt32(), out var completion)) return;
        if (root.TryGetProperty("error", out var error))
            completion.TrySetException(new InvalidOperationException($"DevTools error: {(error.TryGetProperty("message", out var m) ? m.GetString() : error.ToString())}"));
        else
            completion.TrySetResult(root.TryGetProperty("result", out var result) ? result.Clone() : default);
    }

    public async ValueTask DisposeAsync()
    {
        _closing.Cancel();
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, closeTimeout.Token).ConfigureAwait(false);
            }
        }
        catch (Exception) { /* best effort */ }
        if (_receiveLoop is not null) { try { await _receiveLoop.ConfigureAwait(false); } catch (Exception) { } }
        _socket.Dispose();
        _sendLock.Dispose();
        _closing.Dispose();
    }
}
