using System.ComponentModel;
using System.Threading.Channels;
using YTRadioMini.Models;
using YTRadioMini.Services;

namespace YTRadioMini.AppLogic;

/// <summary>
/// 协调 UI、播放队列、解析器、mpv 与存储。
///
/// 并发模型：所有会修改状态的操作（用户命令、mpv 事件、解析结果、定时器）都投递到同一个串行循环里依次执行，
/// 因此状态字段和 <see cref="PlaybackQueue"/> 不需要加锁。耗时的工作（解析播放列表、写文件、等待延时）在循环之外进行，
/// 完成后再把结果投递回循环。
///
/// 竞争保护：每次切换"当前要播放的条目"都会递增 <c>_gen</c>。延迟执行的加载、失败后的自动跳过、直播重试
/// 都带着创建时的序号，执行时发现序号已变就放弃，所以连续点击"下一首"只会真正加载最后一项。
/// mpv 的 end-file 事件只有在 reason 为 eof 且当前确实处于播放/暂停时才会触发自动下一首：
/// 手动切换产生的 stop 事件，以及切换后到达的旧文件的 eof 事件，都不会推进队列。
/// </summary>
public sealed class RadioController : IRadioController
{
    private readonly IMpvService _mpv;
    private readonly IYoutubeResolver _resolver;
    private readonly IStorageService _storage;
    private readonly RadioControllerOptions _opt;

    private readonly PlaybackQueue _queue = new();
    private readonly List<SavedLink> _savedLinks = new();
    private readonly Channel<Func<Task>> _work = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _loop;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _ingestGate = new(1, 1);

    // 以下字段只在串行循环中读写。
    private PlaybackState _state = PlaybackState.Idle;
    private string? _message;
    private long _gen;
    private int _consecutiveFailures;
    private int _liveRetries;
    private int _ingestInFlight;
    private PlaybackState _stateBeforeResolving = PlaybackState.Idle;
    private string? _ingestFailure;
    private CancellationTokenSource? _watchdog;

    private long _snapshotVersion = -1;
    private QueueItem[] _snapshot = Array.Empty<QueueItem>();

    private int _savePending;
    private volatile bool _disposed;
    private RadioViewState _view = RadioViewState.Empty;

    public RadioController(IMpvService mpv, IYoutubeResolver resolver, IStorageService storage, RadioControllerOptions? options = null)
    {
        _mpv = mpv;
        _resolver = resolver;
        _storage = storage;
        _opt = options ?? new RadioControllerOptions();

        _mpv.FileLoaded += OnFileLoaded;
        _mpv.EndFile += OnEndFile;
        _mpv.PauseChanged += OnPauseChanged;
        _mpv.MediaTitleChanged += OnMediaTitle;
        _mpv.LiveDetected += OnLiveDetected;
        _mpv.ProcessExited += OnProcessExited;

        _loop = Task.Run(RunLoopAsync);
    }

    public RadioViewState State => Volatile.Read(ref _view);
    public event Action<RadioViewState>? StateChanged;

    // ───────────────────────────── 公共命令 ─────────────────────────────

    public async Task RestoreAsync(CancellationToken ct = default)
    {
        AppState state;
        try
        {
            state = await _storage.LoadAsync(ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            state = new AppState(); // StorageService 自身不会抛，这里只防自定义实现
        }

        await Post(() =>
        {
            _queue.Restore(state.Queue, state.CurrentIndex);
            _savedLinks.Clear();
            _savedLinks.AddRange(state.SavedLinks);
            _state = PlaybackState.Idle; // 启动时不自动播放
            _message = null;
            Publish();
            return Task.CompletedTask;
        }).ConfigureAwait(false);
    }

    public Task SubmitAsync(string text)
    {
        if (_disposed) return Task.CompletedTask;

        var parsed = LinkParser.Parse(text);
        if (parsed.Valid.Count == 0 && parsed.Invalid.Count == 0) return Task.CompletedTask;

        if (parsed.Valid.Count == 0)
        {
            return Post(() =>
            {
                if (!HasActiveItem)
                {
                    _state = PlaybackState.Error;
                    _message = "INVALID LINK";
                }
                else _message = "INVALID LINK";
                Publish();
                return Task.CompletedTask;
            });
        }

        // 先在串行循环里登记，再在后台解析。
        var registered = Post(() =>
        {
            _ingestInFlight++;
            if (parsed.Invalid.Count > 0) _message = parsed.Invalid.Count == 1 ? "1 LINK SKIPPED" : $"{parsed.Invalid.Count} LINKS SKIPPED";
            if (!HasActiveItem && _state != PlaybackState.Resolving)
            {
                _stateBeforeResolving = _state == PlaybackState.Error ? PlaybackState.Idle : _state;
                _state = PlaybackState.Resolving;
                _ingestFailure = null;
            }
            Publish();
            return Task.CompletedTask;
        });

        _ = IngestAsync(parsed.Valid, registered);
        return registered;
    }

    public Task TogglePauseAsync() => Post(async () =>
    {
        switch (_state)
        {
            case PlaybackState.Playing:
                await SafeMpv(() => _mpv.SetPauseAsync(true, _cts.Token)).ConfigureAwait(false);
                break;
            case PlaybackState.Paused:
                await SafeMpv(() => _mpv.SetPauseAsync(false, _cts.Token)).ConfigureAwait(false);
                break;
            case PlaybackState.Idle or PlaybackState.Stopped or PlaybackState.Error when _queue.Current is not null:
                _consecutiveFailures = 0;
                SelectCurrent(resetRetries: true);
                break;
        }
    });

    public Task NextAsync() => Post(() =>
    {
        if (!_queue.TryMoveNext()) return Task.CompletedTask;
        _consecutiveFailures = 0;
        SelectCurrent(resetRetries: true, userInitiated: true);
        return Task.CompletedTask;
    });

    public Task PreviousAsync() => Post(() =>
    {
        if (!_queue.TryMovePrevious()) return Task.CompletedTask;
        _consecutiveFailures = 0;
        SelectCurrent(resetRetries: true, userInitiated: true);
        return Task.CompletedTask;
    });

    public Task PlayIndexAsync(int index) => Post(() =>
    {
        if (!_queue.TrySetCurrent(index)) return Task.CompletedTask;
        _consecutiveFailures = 0;
        SelectCurrent(resetRetries: true, userInitiated: true);
        return Task.CompletedTask;
    });

    public Task RemoveAsync(int index) => Post(() =>
    {
        var removingCurrent = index == _queue.CurrentIndex;
        var wasActive = removingCurrent && HasActiveItem;
        if (!_queue.RemoveAt(index)) return Task.CompletedTask;

        if (_queue.Count == 0)
        {
            ResetToIdle(stopMpv: wasActive);
        }
        else if (removingCurrent)
        {
            // 原来的下一项顶到了当前位置；它是最后一项被删时，当前项退回新的最后一项。
            var nextSlidIn = index < _queue.Count;
            if (wasActive && nextSlidIn)
            {
                _consecutiveFailures = 0;
                SelectCurrent(resetRetries: true);
                return Task.CompletedTask;
            }

            DisarmWatchdog();
            _gen++; // 取消尚未执行的自动跳过、重连与加载
            _message = null;
            _state = wasActive ? PlaybackState.Stopped : _state is PlaybackState.Error ? PlaybackState.Idle : _state;
            if (wasActive) StopMpvQuietly();
            Publish();
            SaveSoon();
            return Task.CompletedTask;
        }

        Publish();
        SaveSoon();
        return Task.CompletedTask;
    });

    public Task MoveAsync(int from, int to) => Post(() =>
    {
        if (_queue.Move(from, to))
        {
            Publish();
            SaveSoon();
        }
        return Task.CompletedTask;
    });

    public Task ClearAsync() => Post(() =>
    {
        var wasActive = HasActiveItem;
        _queue.Clear();
        ResetToIdle(stopMpv: wasActive);
        return Task.CompletedTask;
    });

    /// <summary>队列已空：回到初始待机状态。</summary>
    private void ResetToIdle(bool stopMpv)
    {
        DisarmWatchdog();
        _gen++;
        _state = PlaybackState.Idle;
        _message = null;
        _consecutiveFailures = 0;
        _liveRetries = 0;
        if (stopMpv) StopMpvQuietly();
        Publish();
        SaveSoon();
    }

    private void StopMpvQuietly() =>
        _ = _mpv.StopAsync(_cts.Token).ContinueWith(_ => { }, TaskScheduler.Default);

    // ───────────────────────────── 串行循环 ─────────────────────────────

    private async Task RunLoopAsync()
    {
        await foreach (var work in _work.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                await work().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"RadioController work item failed: {e}");
                try
                {
                    _state = PlaybackState.Error;
                    _message = "INTERNAL ERROR";
                    _gen++;
                    Publish();
                }
                catch (Exception) { }
            }
        }
    }

    /// <summary>投递工作项；返回的任务在该工作项执行完后完成。控制器已关闭时直接完成。</summary>
    private Task Post(Func<Task> work)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = _work.Writer.TryWrite(async () =>
        {
            try { await work().ConfigureAwait(false); }
            finally { tcs.TrySetResult(); }
        });
        return accepted ? tcs.Task : Task.CompletedTask;
    }

    private Task PostSync(Action action) => Post(() => { action(); return Task.CompletedTask; });

    // ───────────────────────────── 状态与播放 ─────────────────────────────

    private bool HasActiveItem => _state is PlaybackState.Loading or PlaybackState.Playing or PlaybackState.Paused;

    /// <summary>
    /// 开始播放队列的当前项：递增序号、进入 Loading，并安排真正的加载。
    /// userInitiated 为 true 时（用户手动切换）延迟一小段时间再加载，期间再次切换会让这次加载作废。
    /// </summary>
    private void SelectCurrent(bool resetRetries, bool keepMessage = false, bool userInitiated = false)
    {
        if (_queue.Current is null) return;
        if (resetRetries) _liveRetries = 0;

        var gen = ++_gen;
        _state = PlaybackState.Loading;
        if (!keepMessage) _message = null;
        Publish();
        SaveSoon();

        if (userInitiated && _opt.SelectDebounce > TimeSpan.Zero)
            _ = DelayThenPost(_opt.SelectDebounce, () => LoadAsync(gen));
        else
            _ = Post(() => LoadAsync(gen));
    }

    private async Task LoadAsync(long gen)
    {
        if (gen != _gen || _disposed) return;
        var item = _queue.Current;
        if (item is null) return;

        try
        {
            await _mpv.LoadAsync(item.Url, _cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception e)
        {
            if (gen != _gen) return;
            FailTerminal(e is MpvStartException { InnerException: Win32Exception or FileNotFoundException }
                ? "MPV NOT FOUND"
                : "PLAYER ERROR");
            return;
        }

        if (gen == _gen) ArmWatchdog(gen);
    }

    /// <summary>加载命令发出后开始计时，超时仍在 Loading 就按失败处理。</summary>
    private void ArmWatchdog(long gen)
    {
        _watchdog?.Cancel();
        _watchdog?.Dispose();
        var cts = _watchdog = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);

        _ = Task.Run(async () =>
        {
            try { await Task.Delay(_opt.LoadTimeout, cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            await PostSync(() =>
            {
                if (gen == _gen && _state == PlaybackState.Loading)
                {
                    StopMpvQuietly();
                    FailCurrent(YtDlpErrors.ShortMessage(ResolveErrorKind.Timeout));
                }
            }).ConfigureAwait(false);
        });
    }

    private void DisarmWatchdog()
    {
        _watchdog?.Cancel();
    }

    /// <summary>当前项播放失败：显示错误；还有下一项且未达到连续失败上限时，稍后自动跳过。</summary>
    private void FailCurrent(string message)
    {
        DisarmWatchdog();
        _consecutiveFailures++;
        _state = PlaybackState.Error;
        _message = message;
        var gen = ++_gen;
        Publish();
        SaveSoon();

        if (!_queue.HasNext || _consecutiveFailures >= _opt.MaxConsecutiveFailures) return;

        _ = DelayThenPost(_opt.ErrorSkipDelay, () =>
        {
            if (gen != _gen || _state != PlaybackState.Error) return Task.CompletedTask;
            if (_queue.TryMoveNext()) SelectCurrent(resetRetries: true);
            return Task.CompletedTask;
        });
    }

    /// <summary>无法继续的错误（找不到 mpv、播放器退出）：不自动跳过。</summary>
    private void FailTerminal(string message)
    {
        DisarmWatchdog();
        _state = PlaybackState.Error;
        _message = message;
        _gen++;
        Publish();
    }

    private async Task DelayThenPost(TimeSpan delay, Func<Task> work)
    {
        try { await Task.Delay(delay, _cts.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        await Post(work).ConfigureAwait(false);
    }

    private void Advance()
    {
        if (_queue.TryMoveNext())
        {
            SelectCurrent(resetRetries: true);
        }
        else
        {
            DisarmWatchdog();
            _state = PlaybackState.Stopped;
            _gen++;
            Publish();
            SaveSoon();
        }
    }

    // ───────────────────────────── mpv 事件 ─────────────────────────────
    // 这些回调运行在 mpv 读取线程上，只负责把工作投递到串行循环。

    private void OnFileLoaded() => _ = PostSync(() =>
    {
        if (_state != PlaybackState.Loading) return;
        DisarmWatchdog();
        _state = PlaybackState.Playing;
        _message = null;
        _consecutiveFailures = 0;
        Publish();
        SaveSoon();
    });

    private void OnPauseChanged(bool paused) => _ = PostSync(() =>
    {
        if (paused && _state == PlaybackState.Playing) _state = PlaybackState.Paused;
        else if (!paused && _state == PlaybackState.Paused) _state = PlaybackState.Playing;
        else return;
        Publish();
        SaveSoon();
    });

    private void OnMediaTitle(string title) => _ = PostSync(() =>
    {
        if (!HasActiveItem || _queue.Current is not { } item) return;
        var clean = CleanTitle(title);
        if (clean.Length == 0 || LooksLikeUrl(clean) || clean == item.Title) return;

        _queue.Replace(_queue.CurrentIndex, item with { Title = clean });
        Publish();
        SaveSoon();
    });

    private void OnLiveDetected(bool live) => _ = PostSync(() =>
    {
        if (!HasActiveItem || _queue.Current is not { } item || item.IsLive == live) return;
        _queue.Replace(_queue.CurrentIndex, item with { IsLive = live });
        Publish();
        SaveSoon();
    });

    private void OnEndFile(MpvEndFile e) => _ = PostSync(() =>
    {
        if (_queue.Current is not { } item) return;

        switch (e.Reason)
        {
            case MpvEndReason.Eof:
                // Loading 状态下收到 eof：属于已被替换的旧文件，不能推进队列。
                if (_state is not (PlaybackState.Playing or PlaybackState.Paused)) return;
                if (item.IsLive) OnLiveStreamEnded(abnormal: false);
                else
                {
                    _consecutiveFailures = 0;
                    Advance();
                }
                break;

            case MpvEndReason.Error:
                if (!HasActiveItem) return;
                var wasPlaying = _state is PlaybackState.Playing or PlaybackState.Paused;
                if (item.IsLive && wasPlaying) OnLiveStreamEnded(abnormal: true, e.ErrorText);
                else FailCurrent(MessageFor(e.ErrorText));
                break;

            default:
                // Stop / Quit / Redirect / Unknown：手动切换或我们自己的停止，不处理。
                break;
        }
    });

    private void OnProcessExited(string reason) => _ = PostSync(() =>
    {
        if (!HasActiveItem) return;
        FailTerminal("PLAYER STOPPED");
    });

    /// <summary>直播断流：在重试次数内等待后重新加载同一项；用完后按"直播结束"处理。</summary>
    private void OnLiveStreamEnded(bool abnormal, string? errorText = null)
    {
        if (_liveRetries < _opt.LiveRetryDelays.Count)
        {
            var delay = _opt.LiveRetryDelays[_liveRetries++];
            var gen = ++_gen;
            _state = PlaybackState.Loading;
            _message = "RECONNECTING";
            Publish();

            _ = DelayThenPost(delay, () => LoadAsync(gen));
            return;
        }

        if (abnormal)
        {
            FailCurrent(MessageFor(errorText));
            return;
        }

        if (_queue.HasNext) Advance();
        else
        {
            DisarmWatchdog();
            _state = PlaybackState.Stopped;
            _message = "STREAM ENDED";
            _gen++;
            Publish();
            SaveSoon();
        }
    }

    private static string MessageFor(string? errorText) =>
        YtDlpErrors.ShortMessage(YtDlpErrors.Classify(errorText));

    // ───────────────────────────── 链接解析与入队 ─────────────────────────────

    private async Task IngestAsync(IReadOnlyList<ParsedLink> links, Task registered)
    {
        try
        {
            await registered.ConfigureAwait(false);
            await _ingestGate.WaitAsync(_cts.Token).ConfigureAwait(false); // 保证先提交的先入队
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            // 同一批链接里只有第一个解析成功的可以触发自动播放；
            // 其余的只追加，避免第一个条目失败后（如找不到 mpv）后面的条目又接着被播放。
            var mayStart = true;
            foreach (var link in links)
            {
                // 自动合辑（Mix）分两步：先立刻加入种子视频，让它马上开始播放；
                // 再展开合辑，把其余条目追加在后面。展开失败时静默忽略，种子视频已经在播放。
                if (link.Kind == LinkKind.Playlist && link.MixSeedVideoId is { } seed)
                {
                    var seedLink = new ParsedLink(LinkKind.Video, LinkParser.VideoUrl(seed), seed, link.OriginalText);
                    var seedResult = await ResolveSafeAsync(seedLink).ConfigureAwait(false);
                    if (seedResult is null) return;
                    mayStart = await ApplyAsync(seedLink, seedResult, mayStart, quiet: false, remember: true).ConfigureAwait(false);

                    var mixResult = await ResolveSafeAsync(link).ConfigureAwait(false);
                    if (mixResult is null) return;
                    if (mixResult.Failure is null)
                    {
                        var rest = mixResult.Items.Where(i => i.Id != seed).ToList();
                        mixResult = ResolveResult.Ok(rest);
                    }
                    mayStart = await ApplyAsync(link, mixResult, mayStart, quiet: true, remember: false, dedupe: true).ConfigureAwait(false);
                    continue;
                }

                var result = await ResolveSafeAsync(link).ConfigureAwait(false);
                if (result is null) return;
                mayStart = await ApplyAsync(link, result, mayStart, quiet: false, remember: true).ConfigureAwait(false);
            }
        }
        finally
        {
            _ingestGate.Release();
            await PostSync(FinishIngest).ConfigureAwait(false);
        }
    }

    /// <summary>解析一个链接；取消时返回 null，其他异常转成失败结果。</summary>
    private async Task<ResolveResult?> ResolveSafeAsync(ParsedLink link)
    {
        try
        {
            return await _resolver.ResolveAsync(link, _cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception)
        {
            return ResolveResult.Fail(ResolveErrorKind.ParseFailed, YtDlpErrors.ShortMessage(ResolveErrorKind.ParseFailed));
        }
    }

    /// <summary>把解析结果交给串行循环处理，返回下一个链接是否仍可以触发自动播放。</summary>
    private async Task<bool> ApplyAsync(ParsedLink link, ResolveResult result, bool mayStart, bool quiet, bool remember, bool dedupe = false)
    {
        await PostSync(() => ApplyResolved(link, result, mayStart, quiet, remember, dedupe)).ConfigureAwait(false);
        return mayStart && result.Items.Count == 0;
    }

    private void ApplyResolved(ParsedLink link, ResolveResult result, bool allowStart, bool quiet, bool remember, bool dedupe)
    {
        if (result.Failure is { } failure)
        {
            if (quiet)
            {
                // 合辑展开失败：种子视频已经在队列里，只在 LCD 上提示一下，不改变播放状态。
                if (HasActiveItem)
                {
                    _message = "MIX: " + failure.Message;
                    Publish();
                }
                return;
            }
            _ingestFailure = failure.Message;
            if (HasActiveItem)
            {
                _message = failure.Message; // 不打断当前播放，只在 LCD 提示
                Publish();
            }
            return;
        }

        // 合辑每次展开的结果都不一样；队列里已有的视频不再重复追加。
        var items = dedupe
            ? result.Items.Where(i => _queue.Items.All(q => q.Id != i.Id)).ToList()
            : result.Items;
        if (items.Count == 0) return;

        var startPlaying = allowStart && !HasActiveItem;
        var first = _queue.Append(items);
        if (remember) RememberLink(link.OriginalText);

        if (startPlaying && first >= 0)
        {
            _queue.TrySetCurrent(first);
            _consecutiveFailures = 0;
            SelectCurrent(resetRetries: true, keepMessage: true); // 保留"已跳过 n 条"之类的提示，直到开始播放
        }
        else
        {
            Publish();
            SaveSoon();
        }
    }

    private void FinishIngest()
    {
        _ingestInFlight--;
        if (_ingestInFlight > 0 || _state != PlaybackState.Resolving) return;

        // 没有任何条目开始播放：恢复到解析前的状态，并显示失败原因。
        if (_ingestFailure is { } failure)
        {
            _state = _queue.Count == 0 ? PlaybackState.Error : _stateBeforeResolving;
            _message = failure;
        }
        else
        {
            _state = _stateBeforeResolving;
        }
        Publish();
    }

    private void RememberLink(string url)
    {
        _savedLinks.RemoveAll(l => l.Url == url);
        _savedLinks.Add(new SavedLink(url, DateTimeOffset.UtcNow));
        if (_savedLinks.Count > _opt.MaxSavedLinks)
            _savedLinks.RemoveRange(0, _savedLinks.Count - _opt.MaxSavedLinks);
    }

    // ───────────────────────────── 视图与持久化 ─────────────────────────────

    private void Publish()
    {
        var current = _queue.Current;
        var count = _queue.Count;
        var state = _state;

        if (_snapshotVersion != _queue.Version)
        {
            _snapshot = _queue.Items.ToArray();
            _snapshotVersion = _queue.Version;
        }

        var view = new RadioViewState(
            state,
            current?.Title ?? "",
            _queue.CurrentIndex >= 0 ? _queue.CurrentIndex + 1 : 0,
            count,
            current?.IsLive ?? false,
            _message,
            CanTogglePause: current is not null && state is PlaybackState.Playing or PlaybackState.Paused
                or PlaybackState.Idle or PlaybackState.Stopped or PlaybackState.Error,
            CanNext: _queue.HasNext,
            CanPrevious: _queue.HasPrevious,
            Queue: _snapshot);

        if (view == _view) return;
        Volatile.Write(ref _view, view);

        try { StateChanged?.Invoke(view); }
        catch (Exception) { /* 订阅者的异常不影响控制器 */ }
    }

    private AppState Snapshot() => new()
    {
        SavedLinks = _savedLinks.ToList(),
        Queue = _queue.Items.ToList(),
        CurrentIndex = Math.Max(0, _queue.CurrentIndex),
        WasPlaying = _state == PlaybackState.Playing,
    };

    private void SaveSoon()
    {
        if (_disposed || Interlocked.Exchange(ref _savePending, 1) != 0) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_opt.SaveDebounce, _cts.Token).ConfigureAwait(false);
                AppState? snapshot = null;
                await PostSync(() =>
                {
                    Volatile.Write(ref _savePending, 0); // 之后的变化会重新安排一次保存
                    snapshot = Snapshot();
                }).ConfigureAwait(false);

                if (snapshot is not null) await _storage.SaveAsync(snapshot, _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                // 保存失败（磁盘满、权限等）不影响播放；下一次变化会再次尝试。
                Volatile.Write(ref _savePending, 0);
                System.Diagnostics.Debug.WriteLine($"Saving state failed: {e.Message}");
            }
        });
    }

    // ───────────────────────────── 工具 ─────────────────────────────

    private async Task SafeMpv(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            FailTerminal("PLAYER ERROR");
        }
    }

    private static string CleanTitle(string title)
    {
        var chars = title.Where(c => !char.IsControl(c)).Take(StorageService.MaxTitleLength).ToArray();
        return new string(chars).Trim();
    }

    /// <summary>mpv 在解析完成前，media-title 可能只是 URL 或文件名片段，不能当作标题。</summary>
    private static bool LooksLikeUrl(string title) =>
        title.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        title.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        title.Contains("watch?v=", StringComparison.OrdinalIgnoreCase) ||
        title.Contains("youtu.be/", StringComparison.OrdinalIgnoreCase);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _mpv.FileLoaded -= OnFileLoaded;
        _mpv.EndFile -= OnEndFile;
        _mpv.PauseChanged -= OnPauseChanged;
        _mpv.MediaTitleChanged -= OnMediaTitle;
        _mpv.LiveDetected -= OnLiveDetected;
        _mpv.ProcessExited -= OnProcessExited;

        _cts.Cancel();
        _work.Writer.TryComplete();
        try { await _loop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch (TimeoutException) { }

        // 循环已结束，可以直接读取状态做最后一次保存。
        try
        {
            using var saveCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await _storage.SaveAsync(Snapshot(), saveCts.Token).ConfigureAwait(false);
        }
        catch (Exception) { }

        await _mpv.DisposeAsync().ConfigureAwait(false);
        _watchdog?.Dispose();
    }
}
