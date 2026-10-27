using YTRadioMini.Models;
using YTRadioMini.Services;

namespace YTRadioMini.Tests;

public sealed class StorageServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ytradio-tests-" + Guid.NewGuid().ToString("N"));
    private string StatePath => Path.Combine(_dir, "sub", "state.json");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static QueueItem Item(string id, string title = "t", bool live = false) =>
        new(id, $"https://www.youtube.com/watch?v={id}", title, live);

    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task Load_MissingFile_ReturnsEmptyState()
    {
        var s = await new StorageService(StatePath).LoadAsync(Ct);
        Assert.Empty(s.Queue);
        Assert.Empty(s.SavedLinks);
        Assert.Equal(AppState.CurrentSchemaVersion, s.SchemaVersion);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTrips_AndCreatesDirectory()
    {
        var svc = new StorageService(StatePath);
        var when = new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);
        await svc.SaveAsync(new AppState
        {
            SavedLinks = { new SavedLink("https://www.youtube.com/playlist?list=PLrAXtmErZgOeiKm4sgNOknGvNjby9efdf", when) },
            Queue = { Item("AAAAAAAAAAA", "第一首"), Item("BBBBBBBBBBB", "Live", live: true) },
            CurrentIndex = 1,
            WasPlaying = true,
        }, Ct);

        var s = await new StorageService(StatePath).LoadAsync(Ct);

        Assert.Equal(2, s.Queue.Count);
        Assert.Equal("第一首", s.Queue[0].Title);
        Assert.True(s.Queue[1].IsLive);
        Assert.Equal(1, s.CurrentIndex);
        Assert.True(s.WasPlaying);
        Assert.Equal(when, Assert.Single(s.SavedLinks).AddedAt);
        Assert.False(File.Exists(StatePath + ".tmp"));
    }

    [Fact]
    public async Task Save_OverwritesExistingFile_WithoutLeavingTemp()
    {
        var svc = new StorageService(StatePath);
        await svc.SaveAsync(new AppState { Queue = { Item("AAAAAAAAAAA") } }, Ct);
        await svc.SaveAsync(new AppState { Queue = { Item("BBBBBBBBBBB") } }, Ct);

        var s = await svc.LoadAsync(Ct);
        Assert.Equal("BBBBBBBBBBB", Assert.Single(s.Queue).Id);
        Assert.Equal(new[] { "state.json" }, Directory.GetFiles(Path.GetDirectoryName(StatePath)!).Select(Path.GetFileName));
    }

    [Fact]
    public async Task ConcurrentSaves_NeverCorruptTheFile()
    {
        var svc = new StorageService(StatePath);
        await Task.WhenAll(Enumerable.Range(0, 30).Select(i =>
            svc.SaveAsync(new AppState { CurrentIndex = i, Queue = { Item("AAAAAAAAAAA") } }, Ct)));

        var s = await svc.LoadAsync(Ct);
        Assert.Single(s.Queue);
        Assert.InRange(s.CurrentIndex, 0, 0); // 索引 > 队列长度时被规整为 0，且文件仍可解析
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]
    [InlineData("null")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"schemaVersion\":1,\"queue\":\"oops\"}")]
    [InlineData("{\"schemaVersion\":99,\"queue\":[]}")]
    [InlineData("{\"schemaVersion\":0}")]
    public async Task Load_Unusable_BacksUpAndReturnsEmpty(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        await File.WriteAllTextAsync(StatePath, content);

        var s = await new StorageService(StatePath).LoadAsync(Ct);

        Assert.Empty(s.Queue);
        Assert.False(File.Exists(StatePath));
        var backup = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(StatePath)!, "state.json.corrupt-*"));
        Assert.Equal(content, await File.ReadAllTextAsync(backup));
    }

    [Fact]
    public async Task Load_AfterBackup_NextSaveWorks()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        await File.WriteAllTextAsync(StatePath, "garbage");
        var svc = new StorageService(StatePath);

        await svc.LoadAsync(Ct);
        await svc.SaveAsync(new AppState { Queue = { Item("AAAAAAAAAAA") } }, Ct);

        Assert.Single((await svc.LoadAsync(Ct)).Queue);
    }

    [Fact]
    public async Task Backups_AreLimitedToMostRecent()
    {
        var dir = Path.GetDirectoryName(StatePath)!;
        Directory.CreateDirectory(dir);
        var svc = new StorageService(StatePath);

        for (var i = 0; i < StorageService.MaxBackupFiles + 3; i++)
        {
            await File.WriteAllTextAsync(StatePath, "bad" + i);
            await svc.LoadAsync(Ct);
            await Task.Delay(5); // 备份名含毫秒时间戳
        }

        Assert.Equal(StorageService.MaxBackupFiles, Directory.GetFiles(dir, "state.json.corrupt-*").Length);
    }

    [Fact]
    public async Task Load_DropsEntriesThatAreNotYoutubeVideos()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        await File.WriteAllTextAsync(StatePath, """
        {
          "schemaVersion": 1,
          "queue": [
            { "id": "x", "url": "file:///etc/passwd", "title": "evil", "isLive": false },
            { "id": "y", "url": "https://evil.com/watch?v=AAAAAAAAAAA", "title": "evil", "isLive": false },
            { "id": "z", "url": "https://www.youtube.com/playlist?list=PLrAXtmErZgOeiKm4sgNOknGvNjby9efdf", "title": "list", "isLive": false },
            { "id": "q", "url": "", "title": "empty", "isLive": false },
            null,
            { "id": "ok", "url": "https://youtu.be/BBBBBBBBBBB?t=5", "title": "good", "isLive": true }
          ],
          "savedLinks": [
            { "url": "https://evil.com/x", "addedAt": "2026-10-27T09:00:00Z" },
            { "url": "https://youtu.be/BBBBBBBBBBB", "addedAt": "2026-10-27T09:00:00Z" }
          ],
          "currentIndex": 0
        }
        """);

        var s = await new StorageService(StatePath).LoadAsync(Ct);

        var item = Assert.Single(s.Queue);
        Assert.Equal("BBBBBBBBBBB", item.Id);                                  // ID 与 URL 以解析结果为准
        Assert.Equal("https://www.youtube.com/watch?v=BBBBBBBBBBB", item.Url);
        Assert.True(item.IsLive);
        Assert.Single(s.SavedLinks);
        Assert.Equal(0, s.CurrentIndex);
    }

    [Fact]
    public async Task Load_KeepsCurrentItem_WhenEarlierEntriesAreDropped()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        await File.WriteAllTextAsync(StatePath, """
        {
          "queue": [
            { "id": "a", "url": "https://evil.com/", "title": "", "isLive": false },
            { "id": "b", "url": "https://youtu.be/BBBBBBBBBBB", "title": "", "isLive": false },
            { "id": "c", "url": "https://youtu.be/CCCCCCCCCCC", "title": "", "isLive": false }
          ],
          "currentIndex": 2
        }
        """);

        var s = await new StorageService(StatePath).LoadAsync(Ct);

        Assert.Equal(2, s.Queue.Count);
        Assert.Equal("CCCCCCCCCCC", s.Queue[s.CurrentIndex].Id);
    }

    [Theory]
    [InlineData(-3)]
    [InlineData(500)]
    public async Task Load_OutOfRangeIndex_FallsBackToFirst(int index)
    {
        var svc = new StorageService(StatePath);
        await svc.SaveAsync(new AppState { Queue = { Item("AAAAAAAAAAA"), Item("BBBBBBBBBBB") }, CurrentIndex = index }, Ct);
        Assert.Equal(0, (await svc.LoadAsync(Ct)).CurrentIndex);
    }

    [Fact]
    public async Task Load_StripsControlCharactersAndLimitsTitleLength()
    {
        var svc = new StorageService(StatePath);
        await svc.SaveAsync(new AppState { Queue = { Item("AAAAAAAAAAA", "a\u0007b\nc" + new string('x', 1000)) } }, Ct);

        var title = (await svc.LoadAsync(Ct)).Queue[0].Title;

        Assert.StartsWith("abcxxx", title);
        Assert.Equal(StorageService.MaxTitleLength, title.Length);
    }

    [Fact]
    public async Task Load_LimitsSavedLinksToMostRecent()
    {
        var svc = new StorageService(StatePath);
        var state = new AppState();
        for (var i = 0; i < StorageService.MaxSavedLinks + 10; i++)
            state.SavedLinks.Add(new SavedLink($"https://youtu.be/{i:D11}", DateTimeOffset.UnixEpoch.AddMinutes(i)));
        await svc.SaveAsync(state, Ct);

        var links = (await svc.LoadAsync(Ct)).SavedLinks;

        Assert.Equal(StorageService.MaxSavedLinks, links.Count);
        Assert.EndsWith($"{StorageService.MaxSavedLinks + 9:D11}", links[^1].Url);
    }
}
