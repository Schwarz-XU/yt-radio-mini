using YTRadioMini.AppLogic;
using YTRadioMini.Mocks;
using YTRadioMini.Models;
using YTRadioMini.Services;

namespace YTRadioMini.Tests;

public sealed class RadioControllerTests : IAsyncLifetime
{
    private const string A = "AAAAAAAAAAA", B = "BBBBBBBBBBB", C = "CCCCCCCCCCC", D = "DDDDDDDDDDD";

    private sealed class ScriptedResolver : IYoutubeResolver
    {
        public Func<ParsedLink, CancellationToken, Task<ResolveResult>>? Handler { get; set; }
        public int Calls;

        public Task<ResolveResult> ResolveAsync(ParsedLink link, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            if (Handler is not null) return Handler(link, ct);
            if (link.Kind == LinkKind.Playlist)
                return Task.FromResult(ResolveResult.Ok(Enumerable.Range(1, 3)
                    .Select(i => Item($"{link.Id[..8]}{i:D3}", $"{link.Id} #{i}")).ToList()));
            return Task.FromResult(ResolveResult.Ok(new[] { new QueueItem(link.Id, link.Url, "", false) }));
        }
    }

    private sealed class CountingStorage : IStorageService
    {
        public AppState Stored = new();
        public int Saves;
        public Exception? SaveFails;

        public Task<AppState> LoadAsync(CancellationToken ct) => Task.FromResult(Stored);
        public Task SaveAsync(AppState state, CancellationToken ct)
        {
            if (SaveFails is not null) return Task.FromException(SaveFails);
            Stored = state;
            Interlocked.Increment(ref Saves);
            return Task.CompletedTask;
        }
    }

    private readonly FakeMpvService _mpv = new();
    private readonly ScriptedResolver _resolver = new();
    private readonly CountingStorage _storage = new();
    private readonly List<RadioViewState> _views = new();
    private RadioController _radio = null!;

    public Task InitializeAsync()
    {
        _radio = Create();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _radio.DisposeAsync();

    private RadioController Create(RadioControllerOptions? options = null)
    {
        var radio = new RadioController(_mpv, _resolver, _storage, options ?? new RadioControllerOptions
        {
            ErrorSkipDelay = TimeSpan.FromMilliseconds(40),
            LiveRetryDelays = new[] { TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(40) },
            SaveDebounce = TimeSpan.FromMilliseconds(20),
            SelectDebounce = TimeSpan.FromMilliseconds(40),
            LoadTimeout = TimeSpan.FromSeconds(30),
        });
        radio.StateChanged += v => { lock (_views) _views.Add(v); };
        return radio;
    }

    private static QueueItem Item(string id, string title = "", bool live = false) =>
        new(id, $"https://www.youtube.com/watch?v={id}", title, live);

    private static string Url(string id) => $"https://www.youtube.com/watch?v={id}";
    private static string Link(string id) => $"https://youtu.be/{id}";

    private Task WaitState(PlaybackState s, string? what = null) =>
        TestSupport.WaitUntil(() => _radio.State.State == s, 5, what ?? $"state {s}, actual {_radio.State}");

    private Task WaitLoads(int n) =>
        TestSupport.WaitUntil(() => _mpv.Loads.Count >= n, 5, $"{n} load commands, actual {_mpv.Loads.Count}");

    /// <summary>提交后等到第一次 load 命令发出，再模拟 mpv 开始播放。</summary>
    private async Task StartPlaying(params string[] ids)
    {
        await _radio.SubmitAsync(string.Join("\n", ids.Select(Link)));
        await WaitLoads(1);
        _mpv.RaiseFileLoaded();
        await WaitState(PlaybackState.Playing);
    }

    // ───────── 基本播放 ─────────

    [Fact]
    public async Task Submit_OnEmptyQueue_ResolvesLoadsAndPlays()
    {
        await _radio.SubmitAsync(Link(A));

        await WaitLoads(1);
        Assert.Equal(Url(A), _mpv.Loads[0]);
        Assert.Equal(PlaybackState.Loading, _radio.State.State);
        Assert.Equal((1, 1), (_radio.State.Position, _radio.State.Count));

        _mpv.RaiseFileLoaded();
        await WaitState(PlaybackState.Playing);
        Assert.True(_radio.State.CanTogglePause);
        Assert.False(_radio.State.CanNext);
    }

    [Fact]
    public async Task MediaTitle_FillsTheLcdTitle_ButUrlLikeTitlesAreIgnored()
    {
        await StartPlaying(A);

        _mpv.RaiseMediaTitle("watch?v=AAAAAAAAAAA");
        _mpv.RaiseMediaTitle("https://www.youtube.com/watch?v=AAAAAAAAAAA");
        await Task.Delay(50);
        Assert.Equal("", _radio.State.Title);

        _mpv.RaiseMediaTitle("Real Title");
        await TestSupport.WaitUntil(() => _radio.State.Title == "Real Title");
    }

    [Fact]
    public async Task Eof_AdvancesThroughTheQueue_ThenStops_WithoutLooping()
    {
        await StartPlaying(A, B, C);
        Assert.Equal((1, 3), (_radio.State.Position, _radio.State.Count));

        _mpv.RaiseEof();
        await WaitLoads(2);
        Assert.Equal(Url(B), _mpv.Loads[1]);
        _mpv.RaiseFileLoaded();
        await WaitState(PlaybackState.Playing);

        _mpv.RaiseEof();
        await WaitLoads(3);
        _mpv.RaiseFileLoaded();
        await WaitState(PlaybackState.Playing);
        Assert.False(_radio.State.CanNext);

        _mpv.RaiseEof();
        await WaitState(PlaybackState.Stopped);
        await Task.Delay(100);
        Assert.Equal(3, _mpv.Loads.Count); // 没有循环
        Assert.Equal(3, _radio.State.Position);
    }

    [Fact]
    public async Task Playlist_ExpandsInOrderAndKeepsLinkOrder()
    {
        await _radio.SubmitAsync($"{Link(A)}\nhttps://www.youtube.com/playlist?list=PLrAXtmErZgOeiKm4sgNOknGvNjby9efdf\n{Link(B)}");
        await WaitLoads(1);
        _mpv.RaiseFileLoaded();
        await TestSupport.WaitUntil(() => _radio.State.Count == 5, 5, "5 items");

        for (var i = 0; i < 4; i++)
        {
            _mpv.RaiseEof();
            await WaitLoads(i + 2);
            _mpv.RaiseFileLoaded();
            await WaitState(PlaybackState.Playing);
        }

        var expected = new[] { Url(A), Url("PLrAXtmE001"), Url("PLrAXtmE002"), Url("PLrAXtmE003"), Url(B) };
        Assert.Equal(expected, _mpv.Loads);
    }

    [Fact]
    public async Task EndFileStop_FromAManualSwitch_DoesNotAdvance()
    {
        await StartPlaying(A, B, C);

        _mpv.RaiseEndFile(new MpvEndFile(MpvEndReason.Stop, null));
        _mpv.RaiseEndFile(new MpvEndFile(MpvEndReason.Redirect, null));
        _mpv.RaiseEndFile(new MpvEndFile(MpvEndReason.Quit, null));
        await Task.Delay(100);

        Assert.Equal(1, _radio.State.Position);
        Assert.Single(_mpv.Loads);
        Assert.Equal(PlaybackState.Playing, _radio.State.State);
    }

    [Fact]
    public async Task StaleEof_ArrivingWhileTheNextItemIsLoading_IsIgnored()
    {
        await StartPlaying(A, B, C);
        await _radio.NextAsync();
        await WaitLoads(2);
        Assert.Equal(PlaybackState.Loading, _radio.State.State);

        _mpv.RaiseEof(); // 属于已被替换的旧文件
        await Task.Delay(100);

        Assert.Equal(2, _radio.State.Position);
        Assert.Equal(2, _mpv.Loads.Count);
    }

    // ───────── 下一首 ─────────

    [Fact]
    public async Task Next_SkipsToTheNextItem_AndIsDisabledAtTheEnd()
    {
        await StartPlaying(A, B);
        Assert.True(_radio.State.CanNext);

        await _radio.NextAsync();
        await WaitLoads(2);
        Assert.Equal(Url(B), _mpv.Loads[1]);
        Assert.False(_radio.State.CanNext);

        await _radio.NextAsync(); // 队尾：无效果
        await Task.Delay(100);
        Assert.Equal(2, _radio.State.Position);
        Assert.Equal(2, _mpv.Loads.Count);
    }

    [Fact]
    public async Task RapidNextPresses_LoadOnlyTheFinalItem_AndNeverDriftTheIndex()
    {
        await StartPlaying(A, B, C, D);

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => _radio.NextAsync()));
        await WaitLoads(2);
        await Task.Delay(100);

        Assert.Equal(4, _radio.State.Position);
        Assert.Equal(new[] { Url(A), Url(D) }, _mpv.Loads); // 中间项全部被合并，没有真正加载
        Assert.False(_radio.State.CanNext);
    }

    [Fact]
    public async Task Next_WhilePaused_PlaysTheNextItem()
    {
        await StartPlaying(A, B);
        await _radio.TogglePauseAsync();
        await WaitState(PlaybackState.Paused);

        await _radio.NextAsync();

        await WaitLoads(2);
        Assert.Equal(PlaybackState.Loading, _radio.State.State);
    }

    // ───────── 暂停 ─────────

    [Fact]
    public async Task TogglePause_PausesAndResumes_OnMpvConfirmation()
    {
        await StartPlaying(A);

        await _radio.TogglePauseAsync();
        await WaitState(PlaybackState.Paused);
        Assert.Contains("pause True", _mpv.Commands);

        await _radio.TogglePauseAsync();
        await WaitState(PlaybackState.Playing);
        Assert.Contains("pause False", _mpv.Commands);
    }

    [Fact]
    public async Task TogglePause_WhileLoading_DoesNothing()
    {
        await _radio.SubmitAsync(Link(A));
        await WaitLoads(1);

        await _radio.TogglePauseAsync();

        Assert.DoesNotContain(_mpv.Commands, c => c.StartsWith("pause"));
        Assert.Equal(PlaybackState.Loading, _radio.State.State);
    }

    [Fact]
    public async Task TogglePause_AfterStopped_ReplaysTheLastItem()
    {
        await StartPlaying(A);
        _mpv.RaiseEof();
        await WaitState(PlaybackState.Stopped);

        await _radio.TogglePauseAsync();

        await WaitLoads(2);
        Assert.Equal(new[] { Url(A), Url(A) }, _mpv.Loads);
    }

    // ───────── 提交与队列 ─────────

    [Fact]
    public async Task Submit_WhilePlaying_AppendsWithoutInterrupting()
    {
        await StartPlaying(A);

        await _radio.SubmitAsync($"{Link(B)}\n{Link(C)}");
        await TestSupport.WaitUntil(() => _radio.State.Count == 3);

        Assert.Single(_mpv.Loads);
        Assert.Equal(PlaybackState.Playing, _radio.State.State);
        Assert.Equal(1, _radio.State.Position);
        Assert.True(_radio.State.CanNext);
    }

    [Fact]
    public async Task Submit_WhileStopped_StartsPlayingTheNewItems()
    {
        await StartPlaying(A);
        _mpv.RaiseEof();
        await WaitState(PlaybackState.Stopped);

        await _radio.SubmitAsync(Link(B));

        await WaitLoads(2);
        Assert.Equal(Url(B), _mpv.Loads[1]);
        Assert.Equal((2, 2), (_radio.State.Position, _radio.State.Count));
    }

    [Fact]
    public async Task SlowFirstSubmit_StillEntersTheQueueBeforeALaterSubmit()
    {
        var gate = new TaskCompletionSource();
        _resolver.Handler = async (link, ct) =>
        {
            if (link.Id == A) await gate.Task;
            return ResolveResult.Ok(new[] { new QueueItem(link.Id, link.Url, "", false) });
        };

        await _radio.SubmitAsync(Link(A));
        await _radio.SubmitAsync(Link(B));
        await Task.Delay(100);
        Assert.Equal(0, _radio.State.Count);
        Assert.Equal(PlaybackState.Resolving, _radio.State.State);

        gate.SetResult();
        await WaitLoads(1);

        await TestSupport.WaitUntil(() => _radio.State.Count == 2);
        Assert.Equal(Url(A), _mpv.Loads[0]); // 先提交的先播放
    }

    [Fact]
    public async Task InvalidInput_ShowsAnErrorAndLeavesTheQueueAlone()
    {
        await _radio.SubmitAsync("https://evil.example/watch?v=AAAAAAAAAAA");

        await WaitState(PlaybackState.Error);
        Assert.Equal("INVALID LINK", _radio.State.Message);
        Assert.Equal(0, _radio.State.Count);
        Assert.Equal(0, _resolver.Calls);
        Assert.Empty(_mpv.Loads);
    }

    [Fact]
    public async Task InvalidInput_WhilePlaying_DoesNotChangeThePlaybackState()
    {
        await StartPlaying(A);

        await _radio.SubmitAsync("nonsense");

        await TestSupport.WaitUntil(() => _radio.State.Message == "INVALID LINK");
        Assert.Equal(PlaybackState.Playing, _radio.State.State);
    }

    [Fact]
    public async Task MixedInput_KeepsTheValidLinks()
    {
        await _radio.SubmitAsync($"junk\n{Link(A)}\nmore junk");

        await WaitLoads(1);
        Assert.Equal(Url(A), _mpv.Loads[0]);
        Assert.Equal("2 LINKS SKIPPED", _radio.State.Message);
    }

    [Fact]
    public async Task EmptyInput_DoesNothing()
    {
        await _radio.SubmitAsync("  \n ");
        await Task.Delay(50);
        Assert.Equal(RadioViewState.Empty, _radio.State);
    }

    [Fact]
    public async Task ResolverFailure_WithEmptyQueue_ShowsTheReason()
    {
        _resolver.Handler = (_, _) => Task.FromResult(ResolveResult.Fail(ResolveErrorKind.Unavailable, "VIDEO UNAVAILABLE"));

        await _radio.SubmitAsync("https://www.youtube.com/playlist?list=PLrAXtmErZgOeiKm4sgNOknGvNjby9efdf");

        await WaitState(PlaybackState.Error);
        Assert.Equal("VIDEO UNAVAILABLE", _radio.State.Message);
        Assert.Empty(_mpv.Loads);
    }

    [Fact]
    public async Task ResolverFailure_WhilePlaying_OnlyShowsAMessage()
    {
        await StartPlaying(A);
        _resolver.Handler = (_, _) => Task.FromResult(ResolveResult.Fail(ResolveErrorKind.Network, "NO NETWORK"));

        await _radio.SubmitAsync(Link(B));

        await TestSupport.WaitUntil(() => _radio.State.Message == "NO NETWORK");
        Assert.Equal(PlaybackState.Playing, _radio.State.State);
        Assert.Equal(1, _radio.State.Count);
    }

    [Fact]
    public async Task OneFailingLink_DoesNotBlockTheOthers()
    {
        _resolver.Handler = (link, _) => Task.FromResult(link.Id == A
            ? ResolveResult.Fail(ResolveErrorKind.Unavailable, "VIDEO UNAVAILABLE")
            : ResolveResult.Ok(new[] { new QueueItem(link.Id, link.Url, "", false) }));

        await _radio.SubmitAsync($"{Link(A)}\n{Link(B)}");

        await WaitLoads(1);
        Assert.Equal(Url(B), _mpv.Loads[0]);
        Assert.Equal(1, _radio.State.Count);
    }

    [Fact]
    public async Task ResolverThrowing_IsReportedNotCrashed()
    {
        _resolver.Handler = (_, _) => throw new InvalidOperationException("boom");

        await _radio.SubmitAsync(Link(A));

        await WaitState(PlaybackState.Error);
        Assert.Equal("CAN'T PLAY", _radio.State.Message);
    }

    // ───────── 失败处理 ─────────

    [Fact]
    public async Task FailedItem_ShowsTheErrorThenSkipsToTheNext()
    {
        await _radio.SubmitAsync($"{Link(A)}\n{Link(B)}");
        await WaitLoads(1);

        _mpv.RaiseError("ERROR: [youtube] AAAAAAAAAAA: Private video");
        await WaitState(PlaybackState.Error);
        Assert.Equal("VIDEO UNAVAILABLE", _radio.State.Message);

        await WaitLoads(2); // 延时后自动跳过
        Assert.Equal(Url(B), _mpv.Loads[1]);
        Assert.Equal(PlaybackState.Loading, _radio.State.State);
    }

    [Fact]
    public async Task LastItemFailing_StaysInError_WithoutLooping()
    {
        await _radio.SubmitAsync(Link(A));
        await WaitLoads(1);

        _mpv.RaiseError("whatever");
        await WaitState(PlaybackState.Error);
        await Task.Delay(150);

        Assert.Equal(PlaybackState.Error, _radio.State.State);
        Assert.Single(_mpv.Loads);
    }

    [Fact]
    public async Task ThreeConsecutiveFailures_StopSkipping()
    {
        await _radio.SubmitAsync(string.Join("\n", new[] { A, B, C, D, "EEEEEEEEEEE" }.Select(Link)));
        await WaitLoads(1);

        for (var i = 1; i <= 3; i++)
        {
            _mpv.RaiseError("Unable to download webpage");
            if (i < 3) await WaitLoads(i + 1);
        }
        await Task.Delay(200);

        Assert.Equal(3, _mpv.Loads.Count);
        Assert.Equal(PlaybackState.Error, _radio.State.State);
        Assert.Equal("NO NETWORK", _radio.State.Message);
        Assert.Equal(3, _radio.State.Position);
        Assert.True(_radio.State.CanNext); // 用户仍可手动继续
    }

    [Fact]
    public async Task ASuccessfulPlay_ResetsTheFailureCounter()
    {
        await _radio.SubmitAsync(string.Join("\n", new[] { A, B, C, D }.Select(Link)));
        await WaitLoads(1);

        _mpv.RaiseError("x");  await WaitLoads(2);
        _mpv.RaiseError("x");  await WaitLoads(3);
        _mpv.RaiseFileLoaded(); await WaitState(PlaybackState.Playing);
        _mpv.RaiseError("x");  // 播放中途出错，计数从 0 重新开始

        await WaitLoads(4);
        Assert.Equal(Url(D), _mpv.Loads[3]);
    }

    [Fact]
    public async Task ErrorWhileAutoSkipping_CanBeInterruptedByNext()
    {
        await _radio.SubmitAsync(string.Join("\n", new[] { A, B, C }.Select(Link)));
        await WaitLoads(1);
        _mpv.RaiseError("x");
        await WaitState(PlaybackState.Error);

        await _radio.NextAsync(); // 用户在自动跳过的等待期内手动下一首
        await WaitLoads(2);
        await Task.Delay(150);

        Assert.Equal(2, _mpv.Loads.Count);       // 自动跳过的那一次被取消
        Assert.Equal(2, _radio.State.Position);
    }

    [Fact]
    public async Task MpvNotFound_ShowsAClearMessage_AndDoesNotSkip()
    {
        _mpv.LoadException = new MpvStartException("x", new System.ComponentModel.Win32Exception(2));

        await _radio.SubmitAsync($"{Link(A)}\n{Link(B)}");

        await WaitState(PlaybackState.Error);
        Assert.Equal("MPV NOT FOUND", _radio.State.Message);
        await Task.Delay(150);
        Assert.Single(_mpv.Loads);
    }

    [Fact]
    public async Task MpvProcessDying_IsReported_AndPlayRestartsIt()
    {
        await StartPlaying(A, B);

        _mpv.RaiseProcessExited("mpv exited (1)");
        await WaitState(PlaybackState.Error);
        Assert.Equal("PLAYER STOPPED", _radio.State.Message);
        await Task.Delay(100);
        Assert.Single(_mpv.Loads);

        await _radio.TogglePauseAsync();
        await WaitLoads(2);
        Assert.Equal(Url(A), _mpv.Loads[1]);
    }

    [Fact]
    public async Task LoadingThatNeverFinishes_TimesOut()
    {
        await _radio.DisposeAsync();
        _radio = Create(new RadioControllerOptions
        {
            LoadTimeout = TimeSpan.FromMilliseconds(80),
            ErrorSkipDelay = TimeSpan.FromMilliseconds(30),
        });

        await _radio.SubmitAsync($"{Link(A)}\n{Link(B)}");
        await WaitLoads(1);

        await WaitState(PlaybackState.Error);
        Assert.Equal("TIMED OUT", _radio.State.Message);
        Assert.Contains("stop", _mpv.Commands);
        await WaitLoads(2); // 超时也是普通失败：随后跳到下一项
    }

    // ───────── 直播 ─────────

    [Fact]
    public async Task LiveDetection_ShowsLive_AndReconnectsAtMostTwice_ThenMovesOn()
    {
        await StartPlaying(A, B);
        _mpv.RaiseLiveDetected(true);
        await TestSupport.WaitUntil(() => _radio.State.IsLive);

        _mpv.RaiseEof(); // 断流 1
        await WaitLoads(2);
        Assert.Equal(Url(A), _mpv.Loads[1]);
        Assert.Equal("RECONNECTING", _radio.State.Message);
        _mpv.RaiseFileLoaded();
        await WaitState(PlaybackState.Playing);
        Assert.Null(_radio.State.Message);

        _mpv.RaiseError("network"); // 断流 2
        await WaitLoads(3);
        Assert.Equal(Url(A), _mpv.Loads[2]);
        _mpv.RaiseFileLoaded();
        await WaitState(PlaybackState.Playing);

        _mpv.RaiseEof(); // 断流 3：不再重试，直播视为结束并进入下一项
        await WaitLoads(4);
        Assert.Equal(Url(B), _mpv.Loads[3]);
    }

    [Fact]
    public async Task LiveEnding_OnTheLastItem_StopsWithAMessage()
    {
        await StartPlaying(A);
        _mpv.RaiseLiveDetected(true);
        await TestSupport.WaitUntil(() => _radio.State.IsLive);

        for (var i = 0; i < 2; i++)
        {
            _mpv.RaiseEof();
            await WaitLoads(i + 2);
            _mpv.RaiseFileLoaded();
            await WaitState(PlaybackState.Playing);
        }
        _mpv.RaiseEof();

        await WaitState(PlaybackState.Stopped);
        Assert.Equal("STREAM ENDED", _radio.State.Message);
        Assert.Equal(3, _mpv.Loads.Count);
    }

    [Fact]
    public async Task LiveReconnect_IsCancelledByNext()
    {
        await StartPlaying(A, B);
        _mpv.RaiseLiveDetected(true);
        await TestSupport.WaitUntil(() => _radio.State.IsLive);

        _mpv.RaiseEof();
        await _radio.NextAsync();
        await WaitLoads(2);
        await Task.Delay(150);

        Assert.Equal(new[] { Url(A), Url(B) }, _mpv.Loads); // 没有对 A 的重连
    }

    [Fact]
    public async Task LiveItemFlaggedByThePlaylist_ButDetectedAsNotLive_IsTreatedAsOrdinary()
    {
        _resolver.Handler = (link, _) => Task.FromResult(ResolveResult.Ok(new[] { Item(A, "x", live: true), Item(B) }));
        await _radio.SubmitAsync(Link(C));
        await WaitLoads(1);
        _mpv.RaiseFileLoaded();
        await TestSupport.WaitUntil(() => _radio.State.IsLive);

        _mpv.RaiseLiveDetected(false);
        await TestSupport.WaitUntil(() => !_radio.State.IsLive);
        _mpv.RaiseEof();

        await WaitLoads(2);
        Assert.Equal(Url(B), _mpv.Loads[1]);
    }

    [Fact]
    public async Task LiveDetectionForAnOldFile_DoesNotLeakIntoANewState()
    {
        await _radio.DisposeAsync();
        _radio = Create();
        _mpv.RaiseLiveDetected(true); // 没有当前项
        await Task.Delay(50);
        Assert.Equal(RadioViewState.Empty, _radio.State);
    }

    // ───────── 持久化 ─────────

    [Fact]
    public async Task Restore_LoadsQueueAndPosition_WithoutPlaying()
    {
        _storage.Stored = new AppState
        {
            Queue = { Item(A, "One"), Item(B, "Two"), Item(C, "Three") },
            CurrentIndex = 1,
            WasPlaying = true,
        };

        await _radio.RestoreAsync();

        Assert.Equal(PlaybackState.Idle, _radio.State.State);
        Assert.Equal((2, 3), (_radio.State.Position, _radio.State.Count));
        Assert.Equal("Two", _radio.State.Title);
        Assert.Empty(_mpv.Commands); // 默认不自动播放，也不启动 mpv

        await _radio.TogglePauseAsync();
        await WaitLoads(1);
        Assert.Equal(Url(B), _mpv.Loads[0]);
    }

    [Fact]
    public async Task Changes_AreSavedToStorage_WithLinksQueueAndIndex()
    {
        await StartPlaying(A, B);
        await _radio.NextAsync();
        await WaitLoads(2);
        _mpv.RaiseFileLoaded();

        await TestSupport.WaitUntil(() => _storage.Stored.CurrentIndex == 1 && _storage.Stored.WasPlaying, 5, "saved snapshot");
        Assert.Equal(new[] { A, B }, _storage.Stored.Queue.Select(i => i.Id));
        Assert.Equal(new[] { Link(A), Link(B) }, _storage.Stored.SavedLinks.Select(l => l.Url));
    }

    [Fact]
    public async Task SavedLinks_AreDeduplicated()
    {
        await _radio.SubmitAsync(Link(A));
        await _radio.SubmitAsync(Link(A));
        await TestSupport.WaitUntil(() => _radio.State.Count == 2);
        await TestSupport.WaitUntil(() => _storage.Stored.Queue.Count == 2);

        Assert.Single(_storage.Stored.SavedLinks);
    }

    [Fact]
    public async Task SaveFailure_DoesNotAffectPlayback()
    {
        _storage.SaveFails = new IOException("disk full");

        await StartPlaying(A, B);
        await Task.Delay(100);
        _mpv.RaiseEof();

        await WaitLoads(2);
        Assert.Equal(PlaybackState.Loading, _radio.State.State);
    }

    [Fact]
    public async Task Dispose_SavesFinalState_AndDisposesMpv()
    {
        await StartPlaying(A, B);
        await _radio.NextAsync();
        await WaitLoads(2);

        await _radio.DisposeAsync();

        Assert.True(_mpv.Disposed);
        Assert.Equal(1, _storage.Stored.CurrentIndex);
    }

    [Fact]
    public async Task AfterDispose_CommandsAndEventsAreHarmless()
    {
        await StartPlaying(A, B);
        await _radio.DisposeAsync();

        await _radio.NextAsync();
        await _radio.TogglePauseAsync();
        await _radio.SubmitAsync(Link(C));
        _mpv.RaiseEof();
        _mpv.RaiseFileLoaded();
        await Task.Delay(50);

        Assert.Single(_mpv.Loads);
    }

    [Fact]
    public async Task StateChanged_IsOnlyRaisedForActualChanges()
    {
        await StartPlaying(A);
        int before;
        lock (_views) before = _views.Count;

        _mpv.RaisePauseChanged(false); // 已经是播放中：无变化
        _mpv.RaiseLiveDetected(false);
        await Task.Delay(50);

        lock (_views) Assert.Equal(before, _views.Count);
    }

    // ───────── 上一首与队列编辑 ─────────

    private static string[] Ids(RadioViewState v) => v.Queue!.Select(i => i.Id).ToArray();

    [Fact]
    public async Task Previous_GoesBack_AndIsDisabledAtTheStart()
    {
        await StartPlaying(A, B, C);
        Assert.False(_radio.State.CanPrevious);

        await _radio.NextAsync();
        await WaitLoads(2);
        _mpv.RaiseFileLoaded();
        await WaitState(PlaybackState.Playing);
        Assert.True(_radio.State.CanPrevious);

        await _radio.PreviousAsync();
        await WaitLoads(3);
        Assert.Equal(Url(A), _mpv.Loads[2]);
        Assert.False(_radio.State.CanPrevious);

        await _radio.PreviousAsync(); // 队首：无效果
        await Task.Delay(80);
        Assert.Equal(3, _mpv.Loads.Count);
    }

    [Fact]
    public async Task Previous_AfterTheQueueEnded_PlaysThePreviousItem()
    {
        await StartPlaying(A, B);
        _mpv.RaiseEof(); await WaitLoads(2); _mpv.RaiseFileLoaded(); await WaitState(PlaybackState.Playing);
        _mpv.RaiseEof(); await WaitState(PlaybackState.Stopped);

        await _radio.PreviousAsync();

        await WaitLoads(3);
        Assert.Equal(Url(A), _mpv.Loads[2]);
    }

    [Fact]
    public async Task PlayIndex_JumpsToTheChosenItem_EvenBackwards()
    {
        await StartPlaying(A, B, C, D);
        await _radio.PlayIndexAsync(3);
        await WaitLoads(2);
        _mpv.RaiseFileLoaded();
        await WaitState(PlaybackState.Playing);
        Assert.Equal(4, _radio.State.Position);

        await _radio.PlayIndexAsync(1);

        await WaitLoads(3);
        Assert.Equal(new[] { Url(A), Url(D), Url(B) }, _mpv.Loads);
        Assert.Equal(2, _radio.State.Position);
    }

    [Fact]
    public async Task PlayIndex_OutOfRange_IsIgnored()
    {
        await StartPlaying(A, B);
        await _radio.PlayIndexAsync(5);
        await _radio.PlayIndexAsync(-1);
        await Task.Delay(80);
        Assert.Single(_mpv.Loads);
        Assert.Equal(1, _radio.State.Position);
    }

    [Fact]
    public async Task PlayIndex_OfThePlayingItem_RestartsIt()
    {
        await StartPlaying(A, B);
        await _radio.PlayIndexAsync(0);
        await WaitLoads(2);
        Assert.Equal(new[] { Url(A), Url(A) }, _mpv.Loads);
    }

    [Fact]
    public async Task Remove_ANonCurrentItem_DoesNotDisturbPlayback()
    {
        await StartPlaying(A, B, C);
        await _radio.NextAsync(); await WaitLoads(2); _mpv.RaiseFileLoaded(); await WaitState(PlaybackState.Playing);

        await _radio.RemoveAsync(0); // 当前项之前
        await TestSupport.WaitUntil(() => _radio.State.Count == 2);
        Assert.Equal(new[] { B, C }, Ids(_radio.State));
        Assert.Equal(1, _radio.State.Position);
        Assert.Equal(PlaybackState.Playing, _radio.State.State);

        await _radio.RemoveAsync(1); // 当前项之后
        await TestSupport.WaitUntil(() => _radio.State.Count == 1);
        Assert.Equal(new[] { B }, Ids(_radio.State));
        Assert.False(_radio.State.CanNext);
        Assert.Equal(2, _mpv.Loads.Count);
        Assert.DoesNotContain("stop", _mpv.Commands);
    }

    [Fact]
    public async Task Remove_ThePlayingItem_StartsTheNextOne()
    {
        await StartPlaying(A, B, C);

        await _radio.RemoveAsync(0);

        await WaitLoads(2);
        Assert.Equal(Url(B), _mpv.Loads[1]);
        Assert.Equal(new[] { B, C }, Ids(_radio.State));
        Assert.Equal(PlaybackState.Loading, _radio.State.State);
        Assert.Equal(1, _radio.State.Position);
    }

    [Fact]
    public async Task Remove_ThePlayingLastItem_StopsPlayback()
    {
        await StartPlaying(A, B);
        await _radio.NextAsync(); await WaitLoads(2); _mpv.RaiseFileLoaded(); await WaitState(PlaybackState.Playing);

        await _radio.RemoveAsync(1);

        await WaitState(PlaybackState.Stopped);
        Assert.Contains("stop", _mpv.Commands);
        Assert.Equal(new[] { A }, Ids(_radio.State));
        Assert.Equal(1, _radio.State.Position);
        Assert.Equal(2, _mpv.Loads.Count); // 没有自动播放"退回"的那一项
    }

    [Fact]
    public async Task Remove_TheOnlyItem_ReturnsToIdle()
    {
        await StartPlaying(A);

        await _radio.RemoveAsync(0);

        await WaitState(PlaybackState.Idle);
        Assert.Equal(0, _radio.State.Count);
        Assert.Contains("stop", _mpv.Commands);
        Assert.False(_radio.State.CanTogglePause);
    }

    [Fact]
    public async Task Remove_WhileLoadingTheItem_CancelsItsPendingLoad()
    {
        await StartPlaying(A, B, C);
        await _radio.NextAsync();            // 进入 B 的加载
        await _radio.RemoveAsync(1);         // 在 B 的加载命令发出前就删除 B
        await Task.Delay(100);

        Assert.DoesNotContain(Url(B), _mpv.Loads.Skip(1)); // 要么没发过 B 的加载，要么随后被 C 取代
        Assert.Equal(new[] { A, C }, Ids(_radio.State));
    }

    [Fact]
    public async Task Remove_AFailedItem_DropsItWithoutAutoSkipping()
    {
        await _radio.SubmitAsync(Link(A) + "\n" + Link(B));
        await WaitLoads(1);
        _mpv.RaiseError("x");
        await WaitState(PlaybackState.Error);

        await _radio.RemoveAsync(0);
        await Task.Delay(120);

        Assert.Single(_mpv.Loads);                       // 自动跳过被取消
        Assert.Equal(new[] { B }, Ids(_radio.State));
        Assert.Equal(PlaybackState.Idle, _radio.State.State);
        Assert.Null(_radio.State.Message);
    }

    [Fact]
    public async Task Remove_OutOfRange_IsIgnored()
    {
        await StartPlaying(A, B);
        await _radio.RemoveAsync(7);
        await _radio.RemoveAsync(-1);
        await Task.Delay(50);
        Assert.Equal(2, _radio.State.Count);
    }

    [Fact]
    public async Task Move_ReordersTheQueue_WithoutInterruptingPlayback()
    {
        await StartPlaying(A, B, C, D);

        await _radio.MoveAsync(3, 0);

        await TestSupport.WaitUntil(() => Ids(_radio.State).SequenceEqual(new[] { D, A, B, C }));
        Assert.Equal(2, _radio.State.Position);  // 正在播放的 A 现在排第二
        Assert.Equal(PlaybackState.Playing, _radio.State.State);
        Assert.Single(_mpv.Loads);

        _mpv.RaiseEof();                         // 播完后接着播放原来在 A 后面的 B
        await WaitLoads(2);
        Assert.Equal(Url(B), _mpv.Loads[1]);
    }

    [Fact]
    public async Task Clear_StopsAndEmptiesTheQueue()
    {
        await StartPlaying(A, B);

        await _radio.ClearAsync();

        await WaitState(PlaybackState.Idle);
        Assert.Equal(0, _radio.State.Count);
        Assert.Empty(_radio.State.Queue!);
        Assert.Contains("stop", _mpv.Commands);
        await TestSupport.WaitUntil(() => _storage.Stored.Queue.Count == 0, 5, "cleared queue saved");
    }

    [Fact]
    public async Task Clear_ThenSubmit_StartsFresh()
    {
        await StartPlaying(A);
        await _radio.ClearAsync();
        await WaitState(PlaybackState.Idle);

        await _radio.SubmitAsync(Link(B));

        await WaitLoads(2);
        Assert.Equal(Url(B), _mpv.Loads[1]);
        Assert.Equal((1, 1), (_radio.State.Position, _radio.State.Count));
    }

    [Fact]
    public async Task ViewState_ExposesAQueueSnapshot_ThatOnlyChangesWithTheQueue()
    {
        await StartPlaying(A, B);
        var first = _radio.State.Queue;
        Assert.Equal(new[] { A, B }, first!.Select(i => i.Id));

        await _radio.TogglePauseAsync();         // 状态变化不应换新快照
        await WaitState(PlaybackState.Paused);
        Assert.Same(first, _radio.State.Queue);

        _mpv.RaiseMediaTitle("Real"); // 标题补全属于队列内容变化
        await TestSupport.WaitUntil(() => !ReferenceEquals(first, _radio.State.Queue));
        Assert.Equal("Real", _radio.State.Queue![0].Title);
    }

    [Fact]
    public async Task EditedQueue_IsPersisted()
    {
        await StartPlaying(A, B, C);
        await _radio.RemoveAsync(2);
        await _radio.MoveAsync(1, 0);

        await TestSupport.WaitUntil(() => _storage.Stored.Queue.Select(i => i.Id).SequenceEqual(new[] { B, A }), 5, "edited queue saved");
        Assert.Equal(1, _storage.Stored.CurrentIndex);
    }

    // ───────── 自动合辑（Mix） ─────────

    private static string MixLink(string seed) =>
        $"https://www.youtube.com/watch?v={seed}&list=RD{seed}&start_radio=1";

    [Fact]
    public async Task Mix_PlaysTheSeedImmediately_ThenAppendsTheRestWithoutDuplicates()
    {
        var gate = new TaskCompletionSource();
        _resolver.Handler = async (link, ct) =>
        {
            if (link.Kind == LinkKind.Video)
                return ResolveResult.Ok(new[] { new QueueItem(link.Id, link.Url, "", false) });

            await gate.Task; // 合辑展开比较慢
            return ResolveResult.Ok(new[] { Item(A), Item(B), Item(C) }); // 第一项就是种子
        };

        await _radio.SubmitAsync(MixLink(A));

        await WaitLoads(1);                       // 展开完成前，种子视频已经开始加载
        Assert.Equal(Url(A), _mpv.Loads[0]);
        Assert.Equal(1, _radio.State.Count);

        gate.SetResult();
        await TestSupport.WaitUntil(() => _radio.State.Count == 3);
        Assert.Equal(new[] { A, B, C }, Ids(_radio.State));
        Assert.Single(_mpv.Loads);                // 追加不打断当前播放
    }

    [Fact]
    public async Task Mix_ResolvesTheWatchUrlWithList()
    {
        var seen = new List<ParsedLink>();
        _resolver.Handler = (link, _) =>
        {
            lock (seen) seen.Add(link);
            return Task.FromResult(ResolveResult.Ok(new[] { Item(link.Id) }));
        };

        await _radio.SubmitAsync(MixLink(A));
        await TestSupport.WaitUntil(() => { lock (seen) return seen.Count == 2; });

        lock (seen)
        {
            Assert.Equal(LinkKind.Video, seen[0].Kind);
            Assert.Equal(LinkKind.Playlist, seen[1].Kind);
            Assert.Equal($"https://www.youtube.com/watch?v={A}&list=RD{A}", seen[1].Url);
        }
    }

    [Fact]
    public async Task Mix_ExpansionFailure_ShowsAHint_AndTheSeedKeepsPlaying()
    {
        var gate = new TaskCompletionSource();
        _resolver.Handler = async (link, _) =>
        {
            if (link.Kind == LinkKind.Video)
                return ResolveResult.Ok(new[] { new QueueItem(link.Id, link.Url, "", false) });
            await gate.Task;
            return ResolveResult.Fail(ResolveErrorKind.Timeout, "TIMED OUT");
        };

        await _radio.SubmitAsync(MixLink(A));
        await WaitLoads(1);
        _mpv.RaiseFileLoaded();
        await WaitState(PlaybackState.Playing);

        gate.SetResult();

        await TestSupport.WaitUntil(() => _radio.State.Message == "MIX: TIMED OUT");
        Assert.Equal(PlaybackState.Playing, _radio.State.State);
        Assert.Equal(1, _radio.State.Count);
    }

    [Fact]
    public async Task Mix_SkipsVideosThatAreAlreadyInTheQueue()
    {
        await StartPlaying(B);
        _resolver.Handler = (link, _) => Task.FromResult(link.Kind == LinkKind.Video
            ? ResolveResult.Ok(new[] { new QueueItem(link.Id, link.Url, "", false) })
            : ResolveResult.Ok(new[] { Item(A), Item(B), Item(C), Item(D) }));

        await _radio.SubmitAsync(MixLink(A));

        await TestSupport.WaitUntil(() => _radio.State.Count == 4);
        Assert.Equal(new[] { B, A, C, D }, Ids(_radio.State));

        await _radio.SubmitAsync(MixLink(A)); // 再提交一次：种子视频按用户意愿再加一次，合辑里的其余视频不再重复
        await TestSupport.WaitUntil(() => _radio.State.Count == 5);
        await Task.Delay(100);
        Assert.Equal(new[] { B, A, C, D, A }, Ids(_radio.State));
    }

    [Fact]
    public async Task Mix_WithAnUnavailableSeed_StillPlaysTheExpandedItems()
    {
        _resolver.Handler = (link, _) => Task.FromResult(link.Kind == LinkKind.Video
            ? ResolveResult.Fail(ResolveErrorKind.Unavailable, "VIDEO UNAVAILABLE")
            : ResolveResult.Ok(new[] { Item(B), Item(C) }));

        await _radio.SubmitAsync(MixLink(A));

        await WaitLoads(1);
        Assert.Equal(Url(B), _mpv.Loads[0]);
    }

    [Fact]
    public async Task Mix_IsRememberedOnce()
    {
        await _radio.SubmitAsync(MixLink(A));
        await TestSupport.WaitUntil(() => _storage.Stored.Queue.Count >= 1);
        await Task.Delay(100);

        Assert.Single(_storage.Stored.SavedLinks);
    }

    // ───────── 压力 ─────────

    [Fact]
    public async Task RandomInterleaving_NeverBreaksTheInvariants()
    {
        var rnd = new Random(12345);
        _resolver.Handler = (link, _) => Task.FromResult(ResolveResult.Ok(
            Enumerable.Range(0, 3).Select(i => Item($"{link.Id[..8]}{i:D3}")).ToList()));

        var ids = new[] { A, B, C, D };
        var tasks = new List<Task>();
        for (var i = 0; i < 1500; i++)
        {
            switch (rnd.Next(10))
            {
                case 0: tasks.Add(_radio.SubmitAsync(Link(ids[rnd.Next(ids.Length)]))); break;
                case 1 or 2: tasks.Add(_radio.NextAsync()); break;
                case 3: tasks.Add(_radio.TogglePauseAsync()); break;
                case 4: _mpv.RaiseFileLoaded(); break;
                case 5: _mpv.RaiseEof(); break;
                case 6: _mpv.RaiseError("x"); break;
                case 7: _mpv.RaiseEndFile(new MpvEndFile(MpvEndReason.Stop, null)); break;
                case 8: _mpv.RaiseLiveDetected(rnd.Next(2) == 0); break;
                case 9: _mpv.RaiseMediaTitle("t" + i); break;
            }
            if (i % 50 == 0) await Task.Yield();
        }
        await Task.WhenAll(tasks);
        await Task.Delay(300);

        var s = _radio.State;
        Assert.InRange(s.Position, s.Count == 0 ? 0 : 1, s.Count);
        Assert.Equal(s.Count > 0 && s.Position < s.Count, s.CanNext);
        lock (_views)
            foreach (var v in _views)
            {
                Assert.InRange(v.Position, 0, v.Count);
                Assert.False(v.Position == 0 && v.Count > 0);
            }
    }
}
