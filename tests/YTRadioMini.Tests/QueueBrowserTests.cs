using YTRadioMini.AppLogic;

namespace YTRadioMini.Tests;

public class QueueBrowserTests
{
    [Fact]
    public void Open_CentersTheChosenItem()
    {
        var b = new QueueBrowser(4);
        b.Open(cursor: 10, count: 30);
        Assert.Equal(10, b.Cursor);
        Assert.Equal(8, b.Top);
        Assert.Equal(10, b.IndexAtRow(2));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(29, 26)]   // 窗口末尾不留空行
    public void Open_NearTheEdges_KeepsTheWindowFull(int cursor, int expectedTop)
    {
        var b = new QueueBrowser(4);
        b.Open(cursor, 30);
        Assert.Equal(expectedTop, b.Top);
    }

    [Fact]
    public void MoveBy_ScrollsOnlyWhenTheCursorLeavesTheWindow()
    {
        var b = new QueueBrowser(4);
        b.Open(0, 20);

        b.MoveBy(3);
        Assert.Equal((3, 0), (b.Cursor, b.Top));
        b.MoveBy(1);
        Assert.Equal((4, 1), (b.Cursor, b.Top));
        b.MoveBy(-4);
        Assert.Equal((0, 0), (b.Cursor, b.Top));
    }

    [Fact]
    public void MoveBy_ClampsAtBothEnds_NoWrap()
    {
        var b = new QueueBrowser(4);
        b.Open(0, 5);
        b.MoveBy(-1);
        Assert.Equal(0, b.Cursor);
        b.MoveBy(100);
        Assert.Equal(4, b.Cursor);
        Assert.Equal(1, b.Top);
    }

    [Fact]
    public void HomeAndEnd_JumpToTheEnds()
    {
        var b = new QueueBrowser(4);
        b.Open(5, 50);
        b.End();
        Assert.Equal((49, 46), (b.Cursor, b.Top));
        b.Home();
        Assert.Equal((0, 0), (b.Cursor, b.Top));
    }

    [Fact]
    public void SetCount_AfterRemovals_PullsCursorAndWindowBack()
    {
        var b = new QueueBrowser(4);
        b.Open(9, 10);
        b.SetCount(9);
        Assert.Equal(8, b.Cursor);
        Assert.Equal(5, b.Top);

        b.SetCount(0);
        Assert.Equal(0, b.Cursor);
        Assert.Equal(0, b.Top);
    }

    [Fact]
    public void ShortQueues_FillFromTheTop()
    {
        var b = new QueueBrowser(4);
        b.Open(1, 2);
        Assert.Equal(0, b.Top);
        Assert.Equal(0, b.IndexAtRow(0));
        Assert.Equal(1, b.IndexAtRow(1));
        Assert.Equal(-1, b.IndexAtRow(2));
        Assert.Equal(-1, b.IndexAtRow(-1));
        Assert.Equal(-1, b.IndexAtRow(4));
    }

    [Fact]
    public void EmptyQueue_HasNoRows()
    {
        var b = new QueueBrowser(4);
        b.Open(3, 0);
        b.MoveBy(1);
        Assert.Equal(0, b.Cursor);
        Assert.Equal(-1, b.IndexAtRow(0));
    }
}
