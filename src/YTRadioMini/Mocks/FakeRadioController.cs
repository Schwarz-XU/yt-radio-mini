using YTRadioMini.AppLogic;
using YTRadioMini.Models;

namespace YTRadioMini.Mocks;

/// <summary>
/// 供 UI 开发和演示使用的模拟控制器：不启动任何外部进程。
/// 每次提交都会追加 3 条模拟曲目；文本含 "bad" 时显示错误。
/// 支持上一首/下一首、从队列中选择、删除、移动和清空。
/// </summary>
public sealed class FakeRadioController : IRadioController
{
    private readonly TimeSpan _delay;
    private readonly object _gate = new();
    private readonly PlaybackQueue _queue = new();
    private PlaybackState _state = PlaybackState.Idle;
    private string? _message;
    private int _serial;
    private RadioViewState _view = RadioViewState.Empty;

    public FakeRadioController(TimeSpan? simulatedDelay = null) =>
        _delay = simulatedDelay ?? TimeSpan.FromMilliseconds(700);

    public RadioViewState State { get { lock (_gate) return _view; } }
    public event Action<RadioViewState>? StateChanged;

    public Task RestoreAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async Task SubmitAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        if (text.Contains("bad", StringComparison.OrdinalIgnoreCase))
        {
            Publish(PlaybackState.Error, message: "VIDEO UNAVAILABLE");
            return;
        }

        bool wasEmpty;
        lock (_gate) wasEmpty = _queue.Count == 0;

        Publish(PlaybackState.Resolving);
        await Task.Delay(_delay).ConfigureAwait(false);

        lock (_gate)
        {
            var items = Enumerable.Range(1, 3).Select(_ =>
            {
                var n = ++_serial;
                return new QueueItem($"fake{n:D7}", $"https://www.youtube.com/watch?v=fake{n:D7}", $"Fake track {n}", false);
            });
            _queue.Append(items);
        }

        Publish(wasEmpty ? PlaybackState.Playing : null);
    }

    public Task TogglePauseAsync()
    {
        PlaybackState s;
        lock (_gate) s = _state;
        if (s == PlaybackState.Playing) Publish(PlaybackState.Paused);
        else if (s == PlaybackState.Paused) Publish(PlaybackState.Playing);
        return Task.CompletedTask;
    }

    public Task NextAsync()
    {
        bool moved;
        lock (_gate) moved = _queue.TryMoveNext();
        Publish(moved ? PlaybackState.Playing : PlaybackState.Stopped);
        return Task.CompletedTask;
    }

    public Task PreviousAsync()
    {
        bool moved;
        lock (_gate) moved = _queue.TryMovePrevious();
        if (moved) Publish(PlaybackState.Playing);
        return Task.CompletedTask;
    }

    public Task PlayIndexAsync(int index)
    {
        bool ok;
        lock (_gate) ok = _queue.TrySetCurrent(index);
        if (ok) Publish(PlaybackState.Playing);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(int index)
    {
        bool ok;
        lock (_gate) ok = _queue.RemoveAt(index);
        if (ok)
        {
            bool empty;
            lock (_gate) empty = _queue.Count == 0;
            Publish(empty ? PlaybackState.Idle : null);
        }
        return Task.CompletedTask;
    }

    public Task MoveAsync(int from, int to)
    {
        bool ok;
        lock (_gate) ok = _queue.Move(from, to);
        if (ok) Publish(null);
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        lock (_gate) _queue.Clear();
        Publish(PlaybackState.Idle);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <param name="newState">null 表示状态不变，仅刷新队列信息。</param>
    private void Publish(PlaybackState? newState, string? message = null)
    {
        RadioViewState view;
        lock (_gate)
        {
            if (newState is { } s) _state = s;
            _message = message;
            var active = _state is PlaybackState.Playing or PlaybackState.Paused;
            view = new RadioViewState(
                _state,
                _queue.Current?.Title ?? "",
                _queue.CurrentIndex >= 0 ? _queue.CurrentIndex + 1 : 0,
                _queue.Count,
                IsLive: false,
                _message,
                CanTogglePause: active,
                CanNext: _queue.HasNext,
                CanPrevious: _queue.HasPrevious,
                Queue: _queue.Items.ToArray());
            _view = view;
        }
        StateChanged?.Invoke(view);
    }
}
