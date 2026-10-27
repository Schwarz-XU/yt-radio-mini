using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using YTRadioMini.Services;

namespace YTRadioMini.Tests;

/// <summary>用一个脚本化的假 mpv（真实命名管道）测试 IPC 协议处理。</summary>
public sealed class MpvIpcClientTests : IAsyncLifetime
{
    private readonly string _pipeName = "ytradio-test-" + Guid.NewGuid().ToString("N");
    private readonly NamedPipeServerStream _server;
    private readonly StreamReader _serverIn;
    private readonly StreamWriter _serverOut;
    private readonly MpvIpcClient _client;
    private static readonly TimeSpan T = TimeSpan.FromSeconds(5);

    public MpvIpcClientTests()
    {
        _server = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var clientPipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        var accept = _server.WaitForConnectionAsync();
        clientPipe.Connect(5000);
        accept.Wait(5000);

        _serverIn = new StreamReader(_server, new UTF8Encoding(false));
        _serverOut = new StreamWriter(_server, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        _client = new MpvIpcClient(clientPipe);
        _client.Start();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    // xUnit v2 只识别 IAsyncLifetime，不会调用 IAsyncDisposable。
    public async Task DisposeAsync()
    {
        await _client.DisposeAsync();
        await _server.DisposeAsync();
    }

    private async Task<JsonElement> ReadRequest() =>
        JsonDocument.Parse((await _serverIn.ReadLineAsync().WaitAsync(T))!).RootElement.Clone();

    [Fact]
    public async Task Send_WritesCommandWithRequestId_AndReturnsData()
    {
        var send = _client.SendAsync(new object?[] { "get_property", "media-title" }, T, default);

        var req = await ReadRequest();
        Assert.Equal(new[] { "get_property", "media-title" }, req.GetProperty("command").EnumerateArray().Select(e => e.GetString()));
        var id = req.GetProperty("request_id").GetInt64();
        await _serverOut.WriteLineAsync($$"""{"data":"Hello","request_id":{{id}},"error":"success"}""");

        Assert.Equal("Hello", (await send).GetString());
    }

    [Fact]
    public async Task Send_SerializesBooleansAndStringsExactly_UrlIsNotInterpreted()
    {
        const string url = "https://www.youtube.com/watch?v=AAAAAAAAAAA\"; quit \\ $(x)\n";
        var send = _client.SendAsync(new object?[] { "loadfile", url, "replace" }, T, default);
        var send2 = _client.SendAsync(new object?[] { "set_property", "pause", true }, T, default);

        var r1 = await ReadRequest();
        var r2 = await ReadRequest();

        Assert.Equal(url, r1.GetProperty("command")[1].GetString());
        Assert.Equal(JsonValueKind.True, r2.GetProperty("command")[2].ValueKind);

        foreach (var r in new[] { r1, r2 })
            await _serverOut.WriteLineAsync($$"""{"error":"success","request_id":{{r.GetProperty("request_id").GetInt64()}}}""");
        await send; await send2;
    }

    [Fact]
    public async Task Send_ErrorResponse_ThrowsMpvCommandException()
    {
        var send = _client.SendAsync(new object?[] { "get_property", "duration" }, T, default);
        var id = (await ReadRequest()).GetProperty("request_id").GetInt64();
        await _serverOut.WriteLineAsync($$"""{"request_id":{{id}},"error":"property unavailable"}""");

        var ex = await Assert.ThrowsAsync<MpvCommandException>(() => send);
        Assert.Equal("property unavailable", ex.Error);
    }

    [Fact]
    public async Task Responses_AreMatchedByRequestId_NotByOrder()
    {
        var a = _client.SendAsync(new object?[] { "a" }, T, default);
        var b = _client.SendAsync(new object?[] { "b" }, T, default);
        var ra = await ReadRequest();
        var rb = await ReadRequest();

        await _serverOut.WriteLineAsync($$"""{"request_id":{{rb.GetProperty("request_id").GetInt64()}},"error":"success","data":"B"}""");
        await _serverOut.WriteLineAsync($$"""{"request_id":{{ra.GetProperty("request_id").GetInt64()}},"error":"success","data":"A"}""");

        Assert.Equal("A", (await a).GetString());
        Assert.Equal("B", (await b).GetString());
    }

    [Fact]
    public async Task ManyConcurrentRequests_AllGetTheirOwnAnswer()
    {
        var server = Task.Run(async () =>
        {
            for (var i = 0; i < 50; i++)
            {
                var req = await ReadRequest();
                var echo = req.GetProperty("command")[1].GetInt32();
                await _serverOut.WriteLineAsync($$"""{"request_id":{{req.GetProperty("request_id").GetInt64()}},"error":"success","data":{{echo}}}""");
            }
        });

        var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(async i =>
            (await _client.SendAsync(new object?[] { "echo", i }, T, default)).GetInt32()));
        await server;

        Assert.Equal(Enumerable.Range(0, 50), results);
    }

    [Fact]
    public async Task Events_AreDispatched_WithTheirPayload()
    {
        var events = new List<MpvEvent>();
        _client.EventReceived += e => { lock (events) events.Add(e); };

        await _serverOut.WriteLineAsync("""{"event":"file-loaded"}""");
        await _serverOut.WriteLineAsync("""{"event":"property-change","id":1,"name":"pause","data":true}""");
        await _serverOut.WriteLineAsync("""{"event":"end-file","reason":"eof","playlist_entry_id":3}""");
        await TestSupport.WaitUntil(() => { lock (events) return events.Count == 3; });

        Assert.Equal(new[] { "file-loaded", "property-change", "end-file" }, events.Select(e => e.Name));
        Assert.True(events[1].Root.GetProperty("data").GetBoolean());
        Assert.Equal("eof", events[2].Root.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task GarbageAndUnknownLines_AreIgnored()
    {
        var got = new TaskCompletionSource<string>();
        _client.EventReceived += e => got.TrySetResult(e.Name);

        await _serverOut.WriteLineAsync("not json at all");
        await _serverOut.WriteLineAsync("");
        await _serverOut.WriteLineAsync("[1,2,3]");
        await _serverOut.WriteLineAsync("""{"request_id":9999,"error":"success"}""");
        await _serverOut.WriteLineAsync("""{"event":"idle"}""");

        Assert.Equal("idle", await got.Task.WithTimeout());
    }

    [Fact]
    public async Task ThrowingEventHandler_DoesNotStopTheReadLoop()
    {
        var second = new TaskCompletionSource();
        var first = true;
        _client.EventReceived += e =>
        {
            if (first) { first = false; throw new InvalidOperationException("boom"); }
            second.TrySetResult();
        };

        await _serverOut.WriteLineAsync("""{"event":"a"}""");
        await _serverOut.WriteLineAsync("""{"event":"b"}""");

        await second.Task.WithTimeout();
        Assert.True(_client.IsConnected);
    }

    [Fact]
    public async Task Send_TimesOut_WhenMpvNeverAnswers()
    {
        await Assert.ThrowsAsync<TimeoutException>(() =>
            _client.SendAsync(new object?[] { "x" }, TimeSpan.FromMilliseconds(100), default));
        Assert.True(_client.IsConnected);
    }

    [Fact]
    public async Task Send_HonoursCancellation()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _client.SendAsync(new object?[] { "x" }, T, cts.Token));
    }

    [Fact]
    public async Task PeerClose_FailsPendingRequests_AndRaisesDisconnectedOnce()
    {
        var disconnects = 0;
        _client.Disconnected += _ => Interlocked.Increment(ref disconnects);

        var pending = _client.SendAsync(new object?[] { "x" }, T, default);
        await ReadRequest();
        _server.Disconnect();

        await Assert.ThrowsAsync<IOException>(() => pending);
        await TestSupport.WaitUntil(() => !_client.IsConnected);
        await Assert.ThrowsAsync<IOException>(() => _client.SendAsync(new object?[] { "y" }, T, default));
        Assert.Equal(1, disconnects);
    }
}
