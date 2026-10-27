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
}
