#nullable enable

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace TabBouncer;

internal sealed class CdpClient : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonNode?>> _pending = new();
    private CancellationTokenSource? _receiveCancellation;
    private int _nextId;

    public event Action<string, JsonNode?, string?>? EventReceived;
    public event Action<Exception?>? Closed;

    public async Task ConnectAsync(string webSocketUrl, CancellationToken cancellationToken)
    {
        _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        await _socket.ConnectAsync(new Uri(webSocketUrl), cancellationToken).ConfigureAwait(false);
        _receiveCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = Task.Run(() => ReceiveLoopAsync(_receiveCancellation.Token));
    }

    public async Task<JsonNode?> SendAsync(
        string method,
        JsonObject? parameters = null,
        string? sessionId = null,
        int timeoutMs = 5000)
    {
        int id = Interlocked.Increment(ref _nextId);
        var message = new JsonObject
        {
            ["id"] = id,
            ["method"] = method
        };
        if (parameters is not null)
            message["params"] = parameters;
        if (sessionId is not null)
            message["sessionId"] = sessionId;

        var completion = new TaskCompletionSource<JsonNode?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;

        byte[] bytes = Encoding.UTF8.GetBytes(message.ToJsonString());
        await _sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_socket.State != WebSocketState.Open)
                throw new InvalidOperationException(L.T("error.cdpSocketClosed"));

            await _socket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            _pending.TryRemove(id, out _);
            throw;
        }
        finally
        {
            _sendLock.Release();
        }

        using var timeout = new CancellationTokenSource(timeoutMs);
        using var registration = timeout.Token.Register(() =>
        {
            if (_pending.TryRemove(id, out var timedOut))
                timedOut.TrySetException(new TimeoutException(L.Format("error.cdpTimeout", method)));
        });
        return await completion.Task.ConfigureAwait(false);
    }

    public void Fire(string method, JsonObject? parameters = null, string? sessionId = null)
    {
        _ = SendAsync(method, parameters, sessionId).ContinueWith(
            _ => { },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        using var stream = new MemoryStream();
        Exception? failure = null;

        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                   _socket.State == WebSocketState.Open)
            {
                stream.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(
                        new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                        throw new WebSocketException(L.T("error.cdpClosedByChrome"));
                    stream.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                JsonNode? node;
                try
                {
                    node = JsonNode.Parse(Encoding.UTF8.GetString(
                        stream.GetBuffer(), 0, checked((int)stream.Length)));
                }
                catch (JsonException)
                {
                    continue;
                }

                if (node is null)
                    continue;

                if (node["id"] is JsonNode idNode)
                {
                    int id = idNode.GetValue<int>();
                    if (!_pending.TryRemove(id, out var completion))
                        continue;

                    if (node["error"] is JsonNode error)
                        completion.TrySetException(new InvalidOperationException(error.ToJsonString()));
                    else
                        completion.TrySetResult(node["result"]);
                    continue;
                }

                string? method = node["method"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(method))
                {
                    EventReceived?.Invoke(
                        method,
                        node["params"],
                        node["sessionId"]?.GetValue<string>());
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            foreach (var item in _pending)
                item.Value.TrySetCanceled();
            _pending.Clear();
            Closed?.Invoke(failure);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _receiveCancellation?.Cancel();
        }
        catch
        {
        }

        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                await _socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "TabBouncer 종료",
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch
        {
        }

        _receiveCancellation?.Dispose();
        _socket.Dispose();
        _sendLock.Dispose();
    }
}
