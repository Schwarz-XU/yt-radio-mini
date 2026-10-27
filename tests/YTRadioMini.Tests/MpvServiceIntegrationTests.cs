using System.Diagnostics;
using YTRadioMini.Services;

namespace YTRadioMini.Tests;

/// <summary>
/// 用真实的 mpv 进程验证 MpvService（需要 PATH 中有 mpv，没有则直接返回）。
/// 使用 --ao=null 与 --ytdl=no，音频来自本机回环 HTTP 服务，不访问外网。
/// </summary>
public sealed class MpvServiceIntegrationTests : IAsyncLifetime
{
    private static readonly string? MpvPath = TestSupport.FindOnPath("mpv");
    private readonly TestHttpServer _http = new(toneSeconds: 3);
    private MpvService? _mpv;
    private readonly List<string> _log = new();
    private readonly TaskCompletionSource<MpvEndFile> _end = new();
    private readonly TaskCompletionSource _loaded = new();
    private readonly TaskCompletionSource<bool> _live = new();
    private readonly TaskCompletionSource<string> _title = new();
    private readonly TaskCompletionSource<string> _exited = new();
    private int _pauseEvents;
    private bool _lastPause;

    private MpvService Create()
    {
        var mpv = new MpvService(MpvPath!, new MpvServiceOptions
        {
            ExtraArguments = new[] { "--ao=null", "--ao-null-untimed=no", "--ytdl=no" },
            ConnectTimeout = TimeSpan.FromSeconds(15),
            LiveProbeInterval = TimeSpan.FromSeconds(1),
            LiveProbeCount = 3,
        });
        mpv.FileLoaded += () => _loaded.TrySetResult();
        mpv.EndFile += e => _end.TrySetResult(e);
        mpv.LiveDetected += l => _live.TrySetResult(l);
        mpv.MediaTitleChanged += t => _title.TrySetResult(t);
        mpv.ProcessExited += r => _exited.TrySetResult(r);
        mpv.PauseChanged += p => { _lastPause = p; Interlocked.Increment(ref _pauseEvents); };
        return _mpv = mpv;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    // xUnit v2 只识别 IAsyncLifetime，不会调用 IAsyncDisposable。
    public async Task DisposeAsync()
    {
        if (_mpv is not null) await _mpv.DisposeAsync();
        _http.Dispose();
    }

    [Fact]
    public async Task Plays_ReportsEvents_AndEndsWithEof()
    {
        if (MpvPath is null) return;
        var mpv = Create();

        await mpv.StartAsync(default);
        await mpv.LoadAsync(_http.BaseUrl + "/tone.wav", default);

        await _loaded.Task.WithTimeout();
        Assert.Equal("tone.wav", await _title.Task.WithTimeout());
        var end = await _end.Task.WithTimeout(20);
        Assert.Equal(MpvEndReason.Eof, end.Reason);
    }

    [Fact]
    public async Task Pause_TogglesAndReportsPropertyChanges()
    {
        if (MpvPath is null) return;
        var mpv = Create();
        await mpv.LoadAsync(_http.BaseUrl + "/live.wav", default);
        await _loaded.Task.WithTimeout();

        await mpv.SetPauseAsync(true, default);
        await TestSupport.WaitUntil(() => _lastPause, what: "pause=true event");
        await mpv.SetPauseAsync(false, default);
        await TestSupport.WaitUntil(() => !_lastPause, what: "pause=false event");
    }

    [Fact]
    public async Task LoadWhilePaused_StartsPlayingAgain()
    {
        if (MpvPath is null) return;
        var mpv = Create();
        await mpv.LoadAsync(_http.BaseUrl + "/live.wav", default);
        await _loaded.Task.WithTimeout();
        await mpv.SetPauseAsync(true, default);
        await TestSupport.WaitUntil(() => _lastPause);

        await mpv.LoadAsync(_http.BaseUrl + "/tone.wav", default);

        await TestSupport.WaitUntil(() => !_lastPause, what: "unpaused by LoadAsync");
    }

    [Fact]
    public async Task StreamWhoseDurationKeepsGrowing_IsDetectedAsLive()
    {
        if (MpvPath is null) return;
        var mpv = Create();
        await mpv.LoadAsync(_http.BaseUrl + "/live.mp3", default);

        Assert.True(await _live.Task.WithTimeout(20));
    }

    [Fact]
    public async Task FileWithStableDuration_IsDetectedAsNotLive()
    {
        if (MpvPath is null) return;
        var mpv = Create();
        await mpv.LoadAsync(_http.BaseUrl + "/long.wav", default);

        Assert.False(await _live.Task.WithTimeout(20));
    }

    [Fact]
    public async Task LiveDetection_IsDroppedWhenTheFileChangesFirst()
    {
        if (MpvPath is null) return;
        var mpv = Create();
        await mpv.LoadAsync(_http.BaseUrl + "/live.mp3", default);
        await _loaded.Task.WithTimeout();
        await mpv.StopAsync(default);

        await Task.Delay(TimeSpan.FromSeconds(4)); // 超过全部探测所需时间
        Assert.False(_live.Task.IsCompleted);
    }

    [Fact]
    public async Task Replacing_ReportsStopNotEof()
    {
        if (MpvPath is null) return;
        var mpv = Create();
        await mpv.LoadAsync(_http.BaseUrl + "/live.wav", default);
        await _loaded.Task.WithTimeout();

        await mpv.LoadAsync(_http.BaseUrl + "/tone.wav", default);

        var end = await _end.Task.WithTimeout();
        Assert.Equal(MpvEndReason.Stop, end.Reason);
    }

    [Fact]
    public async Task Stop_EndsPlaybackWithStop_AndKeepsProcess()
    {
        if (MpvPath is null) return;
        var mpv = Create();
        await mpv.LoadAsync(_http.BaseUrl + "/live.wav", default);
        await _loaded.Task.WithTimeout();

        await mpv.StopAsync(default);

        Assert.Equal(MpvEndReason.Stop, (await _end.Task.WithTimeout()).Reason);
        Assert.False(_exited.Task.IsCompleted);
    }

    [Fact]
    public async Task UnreachableUrl_EndsWithError()
    {
        if (MpvPath is null) return;
        var mpv = Create();

        await mpv.LoadAsync("http://127.0.0.1:9/nothing.wav", default);

        var end = await _end.Task.WithTimeout(20);
        Assert.Equal(MpvEndReason.Error, end.Reason);
        Assert.False(string.IsNullOrWhiteSpace(end.ErrorText));
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("/etc/passwd")]
    [InlineData("ytdl://abc")]
    [InlineData("--vo=gpu")]
    public async Task NonHttpTargets_AreRejectedBeforeReachingMpv(string target)
    {
        if (MpvPath is null) return;
        var mpv = Create();
        await Assert.ThrowsAsync<ArgumentException>(() => mpv.LoadAsync(target, default));
    }

    [Fact]
    public async Task KilledExternally_RaisesProcessExited_AndNextLoadRestarts()
    {
        if (MpvPath is null) return;
        var mpv = Create();
        await mpv.StartAsync(default);
        var pid = MpvPid();

        Process.GetProcessById(pid).Kill();

        Assert.Contains("mpv", await _exited.Task.WithTimeout());

        // 重新启动：换新的 TaskCompletionSource 没必要，新的实例照常触发事件
        await mpv.LoadAsync(_http.BaseUrl + "/tone.wav", default);
        await _loaded.Task.WithTimeout();
        Assert.NotEqual(pid, MpvPid());
    }

    [Fact]
    public async Task Dispose_TerminatesTheMpvProcess()
    {
        if (MpvPath is null) return;
        var mpv = Create();
        await mpv.LoadAsync(_http.BaseUrl + "/live.wav", default);
        await _loaded.Task.WithTimeout();
        var pid = MpvPid();

        await mpv.DisposeAsync();

        await TestSupport.WaitUntil(() => !ProcessExists(pid), 5, "mpv gone after dispose");
        Assert.False(_exited.Task.IsCompleted); // 主动关闭不应报告为意外退出
        await Assert.ThrowsAsync<ObjectDisposedException>(() => mpv.StartAsync(default));
    }

    [Fact]
    public async Task MissingExecutable_ThrowsMpvStartException()
    {
        var mpv = new MpvService("/definitely/not/mpv");
        await Assert.ThrowsAsync<MpvStartException>(() => mpv.StartAsync(default));
    }

    [Fact]
    public async Task ConcurrentStarts_LaunchOnlyOneProcess()
    {
        if (MpvPath is null) return;
        var mpv = Create();

        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => mpv.StartAsync(default)));

        Assert.Single(Process.GetProcessesByName("mpv"), p => ParentIsUs(p));
    }

    private int MpvPid()
    {
        // 通过反射读取当前实例的 PID（测试专用）。
        var cur = typeof(MpvService).GetField("_current", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(_mpv)!;
        var proc = (Process)cur.GetType().GetProperty("Process")!.GetValue(cur)!;
        return proc.Id;
    }

    private static bool ProcessExists(int pid)
    {
        try { return !Process.GetProcessById(pid).HasExited; }
        catch (ArgumentException) { return false; }
    }

    private static bool ParentIsUs(Process p)
    {
        if (!OperatingSystem.IsLinux()) return true;
        try
        {
            var stat = File.ReadAllText($"/proc/{p.Id}/stat");
            var after = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
            return after[0] != "Z" && int.Parse(after[1]) == Environment.ProcessId; // 排除已被结束但尚未回收的僵尸进程
        }
        catch (Exception) { return false; }
    }
}
