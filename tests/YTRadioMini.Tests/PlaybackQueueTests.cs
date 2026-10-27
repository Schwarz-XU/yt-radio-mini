using YTRadioMini.AppLogic;
using YTRadioMini.Models;

namespace YTRadioMini.Tests;

public class PlaybackQueueTests
{
    private static QueueItem Item(string id) => new(id, $"https://www.youtube.com/watch?v={id}", id, false);

    [Fact]
    public void NewQueue_IsEmptyWithNoCurrent()
    {
        var q = new PlaybackQueue();
        Assert.Equal(0, q.Count);
        Assert.Equal(-1, q.CurrentIndex);
        Assert.Null(q.Current);
        Assert.False(q.HasNext);
        Assert.False(q.TryMoveNext());
    }

    [Fact]
    public void Append_ToEmpty_SelectsFirstAndReturnsItsIndex()
    {
        var q = new PlaybackQueue();
        Assert.Equal(0, q.Append(new[] { Item("a"), Item("b") }));
        Assert.Equal(0, q.CurrentIndex);
        Assert.Equal("a", q.Current!.Id);
    }

    [Fact]
    public void Append_ToExisting_KeepsCurrentIndex()
    {
        var q = new PlaybackQueue();
        q.Append(new[] { Item("a"), Item("b") });
        q.TryMoveNext();

        Assert.Equal(2, q.Append(new[] { Item("c") }));
        Assert.Equal(1, q.CurrentIndex);
        Assert.Equal(3, q.Count);
    }

    [Fact]
    public void Append_Nothing_ReturnsMinusOne()
    {
        var q = new PlaybackQueue();
        Assert.Equal(-1, q.Append(Array.Empty<QueueItem>()));
        Assert.Equal(-1, q.CurrentIndex);
    }

    [Fact]
    public void TryMoveNext_StopsAtEnd_NoWrap()
    {
        var q = new PlaybackQueue();
        q.Append(new[] { Item("a"), Item("b") });

        Assert.True(q.TryMoveNext());
        Assert.False(q.HasNext);
        Assert.False(q.TryMoveNext());
        Assert.Equal(1, q.CurrentIndex);
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(0, 0)]
    [InlineData(-5, 0)]
    [InlineData(3, 0)]
    [InlineData(99, 0)]
    public void Restore_ClampsOutOfRangeIndex(int saved, int expected)
    {
        var q = new PlaybackQueue();
        q.Restore(new[] { Item("a"), Item("b"), Item("c") }, saved);
        Assert.Equal(expected, q.CurrentIndex);
    }

    [Fact]
    public void Restore_Empty_ResetsToNoCurrent()
    {
        var q = new PlaybackQueue();
        q.Append(new[] { Item("a") });
        q.Restore(Array.Empty<QueueItem>(), 0);
        Assert.Equal(-1, q.CurrentIndex);
        Assert.Equal(0, q.Count);
    }

    [Fact]
    public void TrySetCurrent_RejectsOutOfRange()
    {
        var q = new PlaybackQueue();
        q.Append(new[] { Item("a"), Item("b") });
        Assert.False(q.TrySetCurrent(2));
        Assert.False(q.TrySetCurrent(-1));
        Assert.True(q.TrySetCurrent(1));
        Assert.Equal(1, q.CurrentIndex);
    }

    [Fact]
    public void Replace_UpdatesItemInPlace()
    {
        var q = new PlaybackQueue();
        q.Append(new[] { Item("a") });
        q.Replace(0, Item("a") with { Title = "Real title" });
        Assert.Equal("Real title", q.Current!.Title);
    }

    private static PlaybackQueue Of(params string[] ids)
    {
        var q = new PlaybackQueue();
        q.Append(ids.Select(Item));
        return q;
    }

    private static string Order(PlaybackQueue q) => string.Concat(q.Items.Select(i => i.Id));

    [Fact]
    public void TryMovePrevious_StopsAtStart_NoWrap()
    {
        var q = Of("a", "b");
        Assert.False(q.HasPrevious);
        Assert.False(q.TryMovePrevious());

        q.TryMoveNext();
        Assert.True(q.HasPrevious);
        Assert.True(q.TryMovePrevious());
        Assert.Equal(0, q.CurrentIndex);
        Assert.False(q.TryMovePrevious());
    }

    [Theory]
    // 队列 a b c d，当前项下标 cur；删除 index 后期望 (当前下标, 当前项 ID)
    [InlineData(2, 0, 1, "c")]  // 删除当前项之前：当前项索引前移，仍是 c
    [InlineData(2, 1, 1, "c")]
    [InlineData(2, 2, 2, "d")]  // 删除当前项：下一项顶上来
    [InlineData(2, 3, 2, "c")]  // 删除当前项之后：不变
    [InlineData(3, 3, 2, "c")]  // 删除最后一项且它是当前项：退到新的最后一项
    [InlineData(0, 0, 0, "b")]
    public void RemoveAt_KeepsCurrentConsistent(int cur, int remove, int expectedIndex, string expectedId)
    {
        var q = Of("a", "b", "c", "d");
        q.TrySetCurrent(cur);

        Assert.True(q.RemoveAt(remove));

        Assert.Equal(3, q.Count);
        Assert.Equal(expectedIndex, q.CurrentIndex);
        Assert.Equal(expectedId, q.Current!.Id);
    }

    [Fact]
    public void RemoveAt_LastRemainingItem_EmptiesTheQueue()
    {
        var q = Of("a");
        Assert.True(q.RemoveAt(0));
        Assert.Equal(-1, q.CurrentIndex);
        Assert.Null(q.Current);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void RemoveAt_OutOfRange_IsRejected(int index)
    {
        var q = Of("a", "b");
        var before = q.Version;
        Assert.False(q.RemoveAt(index));
        Assert.Equal(2, q.Count);
        Assert.Equal(before, q.Version);
    }

    [Theory]
    // 队列 a b c d e；移动 from→to；当前项始终是同一个条目
    [InlineData(0, 3, 5, "bcdae")]
    [InlineData(3, 0, 1, "dabce")]
    [InlineData(1, 4, 2, "acdeb")]
    [InlineData(2, 0, 0, "cabde")]
    [InlineData(2, 4, 4, "abdec")]
    [InlineData(4, 2, 2, "abecd")]
    public void Move_ReordersAndTheCurrentItemFollows(int from, int to, int unused, string expectedOrder)
    {
        _ = unused;
        foreach (var cur in Enumerable.Range(0, 5))
        {
            var q = Of("a", "b", "c", "d", "e");
            q.TrySetCurrent(cur);
            var currentId = q.Current!.Id;

            Assert.True(q.Move(from, to));

            Assert.Equal(expectedOrder, Order(q));
            Assert.Equal(currentId, q.Current!.Id);
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(-1, 2)]
    [InlineData(0, 5)]
    [InlineData(5, 0)]
    public void Move_InvalidRequests_AreRejected(int from, int to)
    {
        var q = Of("a", "b", "c");
        var before = q.Version;
        Assert.False(q.Move(from, to));
        Assert.Equal("abc", Order(q));
        Assert.Equal(before, q.Version);
    }

    [Fact]
    public void Clear_EmptiesTheQueue_AndBumpsVersionOnce()
    {
        var q = Of("a", "b");
        var before = q.Version;

        q.Clear();
        q.Clear(); // 空队列再清空：无变化

        Assert.Equal(0, q.Count);
        Assert.Equal(-1, q.CurrentIndex);
        Assert.Equal(before + 1, q.Version);
    }

    [Fact]
    public void Version_ChangesOnlyWhenTheItemListChanges()
    {
        var q = Of("a", "b", "c");
        var v = q.Version;

        q.TryMoveNext(); q.TryMovePrevious(); q.TrySetCurrent(2);
        Assert.Equal(v, q.Version);

        q.Replace(0, Item("a") with { Title = "x" });
        Assert.NotEqual(v, q.Version);
    }
}
