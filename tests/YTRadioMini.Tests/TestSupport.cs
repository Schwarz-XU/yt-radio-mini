using System.Net;
using System.Net.Sockets;
using System.Text;

namespace YTRadioMini.Tests;

internal static class TestSupport
{
    public static async Task<T> WithTimeout<T>(this Task<T> task, int seconds = 15)
    {
        using var cts = new CancellationTokenSource();
        var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(seconds), cts.Token));
        if (done != task) throw new TimeoutException($"Timed out after {seconds}s");
        cts.Cancel();
        return await task;
    }

    public static async Task WithTimeout(this Task task, int seconds = 15)
    {
        using var cts = new CancellationTokenSource();
        var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(seconds), cts.Token));
        if (done != task) throw new TimeoutException($"Timed out after {seconds}s");
        cts.Cancel();
        await task;
    }

    public static async Task WaitUntil(Func<bool> condition, int seconds = 10, string? what = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Condition not met in {seconds}s: {what}");
            await Task.Delay(10);
        }
    }

    public static string? FindOnPath(string exe)
    {
        var names = OperatingSystem.IsWindows() ? new[] { exe + ".exe", exe } : new[] { exe };
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (var n in names)
            {
                var p = Path.Combine(dir, n);
                if (File.Exists(p)) return p;
            }
        return null;
    }

    /// <summary>16-bit 单声道 PCM WAV 头；<paramref name="dataSize"/> 为 uint.MaxValue 表示长度未知（流式）。</summary>
    public static byte[] WavHeader(uint dataSize, int sampleRate = 8000)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.ASCII);
        w.Write("RIFF"u8.ToArray());
        w.Write(dataSize == uint.MaxValue ? uint.MaxValue : 36 + dataSize);
        w.Write("WAVEfmt "u8.ToArray());
        w.Write(16u); w.Write((ushort)1); w.Write((ushort)1);
        w.Write((uint)sampleRate); w.Write((uint)(sampleRate * 2)); w.Write((ushort)2); w.Write((ushort)16);
        w.Write("data"u8.ToArray());
        w.Write(dataSize);
        return ms.ToArray();
    }

    /// <summary>1 秒 22.05 kHz 单声道 32 kbps MP3（无 ID3 / Xing 头，可无缝首尾相接）。</summary>
    public static readonly byte[] Mp3OneSecond = Convert.FromBase64String("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");

    public static byte[] SineWav(double seconds, int sampleRate = 8000)
    {
        var count = (int)(seconds * sampleRate);
        var data = new byte[count * 2];
        for (var i = 0; i < count; i++)
        {
            var v = (short)(Math.Sin(2 * Math.PI * 440 * i / sampleRate) * 8000);
            data[i * 2] = (byte)(v & 0xFF);
            data[i * 2 + 1] = (byte)(v >> 8);
        }
        return WavHeader((uint)data.Length, sampleRate).Concat(data).ToArray();
    }
}

/// <summary>本机回环 HTTP 服务：/tone.wav 返回固定长度音频，/live.wav 返回长度未知、持续输出的音频流。</summary>
internal sealed class TestHttpServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly byte[] _tone;

    public string BaseUrl { get; }

    public TestHttpServer(double toneSeconds = 3)
    {
        _tone = TestSupport.SineWav(toneSeconds);
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        BaseUrl = $"http://127.0.0.1:{port}";
        _listener.Prefixes.Add(BaseUrl + "/");
        _listener.Start();
        _ = Task.Run(AcceptLoop);
    }

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch (Exception) { return; }
            _ = Task.Run(() => Handle(ctx));
        }
    }

    private async Task Handle(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url!.AbsolutePath;
            ctx.Response.ContentType = "audio/wav";
            if (path == "/tone.wav")
            {
                ctx.Response.ContentLength64 = _tone.Length;
                await ctx.Response.OutputStream.WriteAsync(_tone, _cts.Token);
                ctx.Response.Close();
            }
            else if (path == "/long.wav")
            {
                var long_ = TestSupport.SineWav(20);
                ctx.Response.ContentLength64 = long_.Length;
                await ctx.Response.OutputStream.WriteAsync(long_, _cts.Token);
                ctx.Response.Close();
            }
            else if (path == "/live.wav" || path == "/live.mp3")
            {
                // 长度未知、持续输出的流；mp3 版本的 duration 会随数据到达而增长，用来模拟直播。
                var mp3 = path.EndsWith(".mp3");
                ctx.Response.ContentType = mp3 ? "audio/mpeg" : "audio/wav";
                ctx.Response.SendChunked = true;
                byte[] chunk;
                if (mp3) chunk = TestSupport.Mp3OneSecond;
                else
                {
                    await ctx.Response.OutputStream.WriteAsync(TestSupport.WavHeader(uint.MaxValue), _cts.Token);
                    chunk = TestSupport.SineWav(0.25)[44..];
                }
                while (!_cts.IsCancellationRequested)
                {
                    await ctx.Response.OutputStream.WriteAsync(chunk, _cts.Token);
                    await ctx.Response.OutputStream.FlushAsync(_cts.Token);
                    await Task.Delay(mp3 ? 900 : 200, _cts.Token);
                }
            }
            else
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
            }
        }
        catch (Exception)
        {
            try { ctx.Response.Abort(); } catch (Exception) { }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Close(); } catch (Exception) { }
    }
}
