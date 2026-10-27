using YTRadioMini.Models;

namespace YTRadioMini.AppLogic;

/// <summary>
/// 内存中的有序队列与当前索引，不做 I/O、不含 await。
/// 异步流程（播放、下一首、播放结束）和切换序号由 RadioController 持有；
/// 本类只允许在 RadioController 的串行上下文中使用，自身不加锁。
/// 空队列时 CurrentIndex 为 -1。
/// </summary>
public sealed class PlaybackQueue
{
    private readonly List<QueueItem> _items = new();

    public IReadOnlyList<QueueItem> Items => _items;
    public int Count => _items.Count;
    public int CurrentIndex { get; private set; } = -1;
    public QueueItem? Current => CurrentIndex >= 0 ? _items[CurrentIndex] : null;
    public bool HasNext => CurrentIndex >= 0 && CurrentIndex < _items.Count - 1;
    public bool HasPrevious => CurrentIndex > 0;

    /// <summary>条目列表（增删、移动、替换）每变化一次加 1，用来判断是否需要重新生成快照。</summary>
    public long Version { get; private set; }

    /// <summary>追加到队尾；当前索引不变（空队列时指向新的第一项）。返回第一个新条目的索引，没有新增时返回 -1。</summary>
    public int Append(IEnumerable<QueueItem> items)
    {
        var first = _items.Count;
        _items.AddRange(items);
        if (_items.Count == first) return -1;
        if (CurrentIndex < 0) CurrentIndex = 0;
        Version++;
        return first;
    }

    /// <summary>用保存的队列替换当前内容；索引越界时回到 0，空队列为 -1。</summary>
    public void Restore(IEnumerable<QueueItem> items, int currentIndex)
    {
        _items.Clear();
        _items.AddRange(items);
        CurrentIndex = _items.Count == 0 ? -1
            : currentIndex >= 0 && currentIndex < _items.Count ? currentIndex : 0;
        Version++;
    }

    /// <summary>前进一项；已在队尾时不移动并返回 false（不循环）。</summary>
    public bool TryMoveNext()
    {
        if (!HasNext) return false;
        CurrentIndex++;
        return true;
    }

    /// <summary>后退一项；已在队首时不移动并返回 false（不循环）。</summary>
    public bool TryMovePrevious()
    {
        if (!HasPrevious) return false;
        CurrentIndex--;
        return true;
    }

    /// <summary>直接跳到指定索引；越界时不移动并返回 false。</summary>
    public bool TrySetCurrent(int index)
    {
        if (index < 0 || index >= _items.Count) return false;
        CurrentIndex = index;
        return true;
    }

    /// <summary>替换指定位置的条目（例如用 mpv 报告的标题补全）。</summary>
    public void Replace(int index, QueueItem item)
    {
        _items[index] = item;
        Version++;
    }

    /// <summary>
    /// 删除指定位置的条目。删除当前项之前的条目时当前索引随之前移；
    /// 删除当前项时，索引保持不变（即原来的下一项顶上来），若它是最后一项则退到新的最后一项。
    /// 越界时不做任何事并返回 false。
    /// </summary>
    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= _items.Count) return false;

        _items.RemoveAt(index);
        if (_items.Count == 0) CurrentIndex = -1;
        else if (index < CurrentIndex) CurrentIndex--;
        else if (CurrentIndex >= _items.Count) CurrentIndex = _items.Count - 1;

        Version++;
        return true;
    }

    /// <summary>把 from 位置的条目移到 to 位置；当前项始终指向同一个条目。越界或位置相同时返回 false。</summary>
    public bool Move(int from, int to)
    {
        if (from == to || from < 0 || to < 0 || from >= _items.Count || to >= _items.Count) return false;

        var item = _items[from];
        _items.RemoveAt(from);
        _items.Insert(to, item);

        if (CurrentIndex == from) CurrentIndex = to;
        else if (from < CurrentIndex && to >= CurrentIndex) CurrentIndex--;
        else if (from > CurrentIndex && to <= CurrentIndex) CurrentIndex++;

        Version++;
        return true;
    }

    public void Clear()
    {
        if (_items.Count == 0) return;
        _items.Clear();
        CurrentIndex = -1;
        Version++;
    }
}
