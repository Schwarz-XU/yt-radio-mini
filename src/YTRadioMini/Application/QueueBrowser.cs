namespace YTRadioMini.AppLogic;

/// <summary>
/// 队列列表视图的光标与滚动窗口逻辑（不涉及绘制，便于测试）。
/// 窗口一次显示 <see cref="Rows"/> 行；光标始终保持在窗口内。
/// </summary>
public sealed class QueueBrowser
{
    public QueueBrowser(int rows) => Rows = Math.Max(1, rows);

    public int Rows { get; }
    public int Count { get; private set; }
    public int Cursor { get; private set; }

    /// <summary>窗口第一行对应的队列索引。</summary>
    public int Top { get; private set; }

    /// <summary>进入列表视图：光标放在指定项上，并让它出现在窗口里。</summary>
    public void Open(int cursor, int count)
    {
        Count = Math.Max(0, count);
        Cursor = Clamp(cursor);
        Top = Math.Max(0, Cursor - Rows / 2); // 让当前项大致居中
        EnsureVisible();
    }

    /// <summary>队列内容变化后同步条目数，光标和窗口保持在有效范围内。</summary>
    public void SetCount(int count)
    {
        Count = Math.Max(0, count);
        Cursor = Clamp(Cursor);
        EnsureVisible();
    }

    public void MoveBy(int delta) => SetCursor(Cursor + delta);
    public void Home() => SetCursor(0);
    public void End() => SetCursor(Count - 1);

    public void SetCursor(int index)
    {
        Cursor = Clamp(index);
        EnsureVisible();
    }

    /// <summary>窗口中第 <paramref name="row"/> 行（从 0 起）对应的队列索引；该行没有条目时返回 -1。</summary>
    public int IndexAtRow(int row)
    {
        var index = Top + row;
        return row >= 0 && row < Rows && index < Count ? index : -1;
    }

    private int Clamp(int index) => Count == 0 ? 0 : Math.Min(Math.Max(index, 0), Count - 1);

    private void EnsureVisible()
    {
        if (Cursor < Top) Top = Cursor;
        else if (Cursor >= Top + Rows) Top = Cursor - Rows + 1;

        // 末尾不要留空行：尽量让窗口填满。
        var maxTop = Math.Max(0, Count - Rows);
        Top = Math.Min(Math.Max(Top, 0), maxTop);
    }
}
