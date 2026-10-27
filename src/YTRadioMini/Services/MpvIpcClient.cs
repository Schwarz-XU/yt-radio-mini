using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace YTRadioMini.Services;

public sealed class MpvCommandException : Exception
{
    public MpvCommandException(string error) : base($"mpv command failed: {error}") => Error = error;
    public string Error { get; }
}

/// <summary>mpv 推送的事件；<see cref="Root"/> 是独立副本，可在事件处理之外安全使用。</summary>
public sealed record MpvEvent(string Name, JsonElement Root);

/// <summary>
/// mpv JSON IPC 协议（按行分隔的 JSON）的客户端，只依赖一个双工 <see cref="Stream"/>，
/// 因此可以用任意管道或内存流测试。
/// 一个持续运行的读取任务负责分发响应与事件；每条请求带 request_id，按 id 回填结果。
/// </summary>
public sealed class MpvIpcClient : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private Task _readLoop = Task.CompletedTask;
    private long _nextId;
    private int _disconnected;

    public MpvIpcClient(Stream stream) => _stream = stream;

    /// <summary>在读取任务线程上同步触发，处理函数不得阻塞。</summary>
    public event Action<MpvEvent>? EventReceived;

    /// <summary>读取结束（对端关闭或出错）时触发一次。</summary>
    public event Action<Exception?>? Disconnected;

    public bool IsConnected => Volatile.Read(ref _disconnected) == 0;

    public void Start() => _readLoop = Task.Run(ReadLoopAsync);

    /// <summary>发送命令并等待响应，返回响应中的 data（可能是 JsonValueKind.Null 或 Undefined 的空元素）。</summary>
    public async Task<JsonElement> SendAsync(IReadOnlyList<object?> command, TimeSpan timeout, CancellationToken ct)
    {
        if (!IsConnected) throw new IOException("mpv IPC is not connected");

        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?>
            {
                ["command"] = command,
                ["request_id"] = id,
            });

            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await _stream.WriteAsync(payload, ct).ConfigureAwait(false);
                await _stream.WriteAsync("\n"u8.ToArray(), ct).ConfigureAwait(false);
                await _stream.FlushAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }

            return await tcs.Task.WaitAsync(timeout, ct).ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task ReadLoopAsync()
    {
        Exception? failure = null;
        try
        {
            using var reader = new StreamReader(_stream, new UTF8Encoding(false), false, 16 * 1024, leaveOpen: true);
            while (!_cts.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(_cts.Token).ConfigureAwait(false);
                if (line is null) break; // 对端关闭
                if (line.Length == 0) continue;
                Dispatch(line);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
        {
            failure = e;
        }

        Fail(failure);
    }

    private void Dispatch(string line)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(line); }
        catch (JsonException) { return; } // 忽略无法解析的行

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;

            if (root.TryGetProperty("request_id", out var idEl) && idEl.TryGetInt64(out var id))
            {
                if (!_pending.TryGetValue(id, out var tcs)) return;

                var error = root.TryGetProperty("error", out var errEl) && errEl.ValueKind == JsonValueKind.String
                    ? errEl.GetString() : "success";
                if (error != "success")
                {
                    tcs.TrySetException(new MpvCommandException(error ?? "unknown"));
                    return;
                }

                tcs.TrySetResult(root.TryGetProperty("data", out var data) ? data.Clone() : default);
                return;
            }

            if (root.TryGetProperty("event", out var evEl) && evEl.ValueKind == JsonValueKind.String)
            {
                var handler = EventReceived;
                if (handler is null) return;
                try { handler(new MpvEvent(evEl.GetString()!, root.Clone())); }
                catch (Exception) { /* 订阅者的异常不能中断读取循环 */ }
            }
        }
    }

    private void Fail(Exception? cause)
    {
        if (Interlocked.Exchange(ref _disconnected, 1) != 0) return;

        var error = new IOException("mpv IPC closed", cause);
        foreach (var tcs in _pending.Values) tcs.TrySetException(error);

        try { Disconnected?.Invoke(cause); }
        catch (Exception) { }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { _stream.Dispose(); } catch (Exception) { }
        try { await _readLoop.ConfigureAwait(false); } catch (Exception) { }
        Fail(null);
        _cts.Dispose();
    }
}
