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
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly object _disposeLock = new();
    private CancellationTokenSource? _receiveCancellation;
    private CancellationToken _connectionToken;
    private Task? _receiveTask;
    private Task? _disposeTask;
    private int _nextId;

    public CdpClient() => _connectionToken = _lifetimeCancellation.Token;

    public event Action<string, JsonNode?, string?>? EventReceived;
    public event Action<Exception?>? Closed;

    public async Task ConnectAsync(string webSocketUrl, CancellationToken cancellationToken)
    {
        _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        _receiveCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _lifetimeCancellation.Token);
        _connectionToken = _receiveCancellation.Token;
        await _socket.ConnectAsync(new Uri(webSocketUrl), _connectionToken).ConfigureAwait(false);
        CancellationToken receiveToken = _connectionToken;
        _receiveTask = Task.Run(() => ReceiveLoopAsync(receiveToken));
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

        // 제한 시간은 응답 대기뿐 아니라 송신 잠금 대기와 실제 송신에도 적용한다.
        // 연결 종료 시 같은 토큰으로 송신·응답 대기를 모두 해제한다.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_connectionToken);
        timeout.CancelAfter(timeoutMs);
        using var registration = timeout.Token.Register(() => completion.TrySetCanceled(timeout.Token));
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(message.ToJsonString());
            await _sendLock.WaitAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                if (_socket.State != WebSocketState.Open)
                    throw new InvalidOperationException(L.T("error.cdpSocketClosed"));

                await _socket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    timeout.Token).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
            return await completion.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!_connectionToken.IsCancellationRequested)
        {
            throw new TimeoutException(L.Format("error.cdpTimeout", method));
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
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
            _lifetimeCancellation.Cancel();
            foreach (var item in _pending)
                item.Value.TrySetCanceled();
            _pending.Clear();
            Closed?.Invoke(failure);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeLock)
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        // CDP 연결만 끊는다. 상대의 close 응답을 기다리는 CloseAsync는 종료 시 사용하지 않는다.
        // Chrome 자체 종료는 사용자가 선택한 경우 Browser.close 명령으로 별도 처리한다.
        _lifetimeCancellation.Cancel();
        _socket.Abort();
        if (_receiveTask is not null)
        {
            try
            {
                await _receiveTask.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // 이벤트 처리기가 늦어져도 프로세스 종료를 막지 않는다.
            }
        }

        _receiveCancellation?.Dispose();
        _socket.Dispose();
        // 송신 finally에서 Release할 수 있다. 커널 핸들을 만들지 않는 이 세마포어는 GC에 맡긴다.
    }
}
