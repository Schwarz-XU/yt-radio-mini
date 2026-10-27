using YTRadioMini.Mocks;
using YTRadioMini.Models;
using YTRadioMini.Services;

namespace YTRadioMini.Tests;

// 阶段 0 冒烟测试：只验证骨架、链接进来的源码和模拟服务能工作，
// 不代表任何真实模块（LinkParser、MpvService 等）已经实现。
public class Phase0SmokeTests
{
    [Fact]
    public async Task FakeResolver_ExpandsPlaylistInOrder()
    {
        var link = new ParsedLink(LinkKind.Playlist, "https://www.youtube.com/playlist?list=PL1", "PL1", "x");
        var result = await new FakeYoutubeResolver().ResolveAsync(link, CancellationToken.None);

        Assert.Null(result.Failure);
        Assert.Equal(new[] { "PL1-1", "PL1-2", "PL1-3" }, result.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task FakeResolver_ReportsFailureWithoutThrowing()
    {
        var link = new ParsedLink(LinkKind.Video, "https://youtu.be/bad1", "bad1", "x");
        var result = await new FakeYoutubeResolver().ResolveAsync(link, CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(ResolveErrorKind.Unavailable, result.Failure?.Kind);
    }

    [Fact]
    public async Task InMemoryStorage_RoundTrips()
    {
        var storage = new InMemoryStorageService();
        var state = new AppState { CurrentIndex = 2 };
        await storage.SaveAsync(state, CancellationToken.None);

        Assert.Equal(2, (await storage.LoadAsync(CancellationToken.None)).CurrentIndex);
    }

    [Fact]
    public async Task FakeController_FirstSubmitStartsPlaying_NextStopsAtEnd()
    {
        var radio = new FakeRadioController(TimeSpan.FromMilliseconds(1));

        await radio.SubmitAsync("https://youtu.be/AAAA");
        Assert.Equal(PlaybackState.Playing, radio.State.State);
        Assert.Equal((1, 3), (radio.State.Position, radio.State.Count));

        await radio.NextAsync();
        await radio.NextAsync();
        Assert.False(radio.State.CanNext);

        await radio.NextAsync();
        Assert.Equal(PlaybackState.Stopped, radio.State.State);
    }
}
