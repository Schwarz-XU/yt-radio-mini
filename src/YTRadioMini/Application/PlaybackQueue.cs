using YTRadioMini.Models;

namespace YTRadioMini.AppLogic;

// 阶段 0 占位：由 Agent 3 实现。
// 设计约定：PlaybackQueue 只是内存中的有序列表与当前索引，不做 I/O、不含 await；
// 异步流程（PlayAsync / NextAsync / OnPlaybackEndedAsync）和切换序号由 RadioController 持有，
// 这样所有竞争判断集中在一处。仅供 RadioController 在同一串行上下文中使用。
public sealed class PlaybackQueue
{
    public IReadOnlyList<QueueItem> Items => throw new NotImplementedException();
    public int CurrentIndex => throw new NotImplementedException();
    public QueueItem? Current => throw new NotImplementedException();
    public bool HasNext => throw new NotImplementedException();

    public void Append(IEnumerable<QueueItem> items) => throw new NotImplementedException();
    public void Restore(IEnumerable<QueueItem> items, int currentIndex) => throw new NotImplementedException();
    public bool TryMoveNext() => throw new NotImplementedException();
}
