using YTRadioMini.AppLogic;
using YTRadioMini.Models;

namespace YTRadioMini.Mocks;

/// <summary>
/// 供 UI 开发和演示使用的模拟控制器：不启动任何外部进程。
/// 提交任意文本后生成 3 条队列；文本含 "bad" 时显示错误。
/// </summary>
public sealed class FakeRadioController : IRadioController
{
    private readonly TimeSpan _delay;
    private readonly object _gate = new();
    private List<string> _titles = new();
    private int _index;
    private PlaybackState _state = PlaybackState.Idle;
    private string? _message;
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
        lock (_gate) wasEmpty = _titles.Count == 0;

        Publish(PlaybackState.Resolving);
        await Task.Delay(_delay).ConfigureAwait(false);

        lock (_gate)
        {
            var start = _titles.Count;
            _titles = _titles.Concat(Enumerable.Range(1, 3).Select(i => $"Fake track {start + i}")).ToList();
            if (wasEmpty) _index = 0;
        }

        if (wasEmpty) Publish(PlaybackState.Playing);
        else Publish(null);
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
        bool atEnd;
        lock (_gate)
        {
            atEnd = _index >= _titles.Count - 1;
            if (!atEnd) _index++;
        }
        Publish(atEnd ? PlaybackState.Stopped : PlaybackState.Playing);
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
            var count = _titles.Count;
            var title = count > 0 ? _titles[_index] : "";
            var active = _state is PlaybackState.Playing or PlaybackState.Paused;
            view = new RadioViewState(
                _state,
                title,
                count == 0 ? 0 : _index + 1,
                count,
                IsLive: false,
                _message,
                CanTogglePause: active,
                CanNext: count > 0 && _index < count - 1);
            _view = view;
        }
        StateChanged?.Invoke(view);
    }
}
