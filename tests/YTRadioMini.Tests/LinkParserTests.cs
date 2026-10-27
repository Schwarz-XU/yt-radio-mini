using YTRadioMini.Services;

namespace YTRadioMini.Tests;

public class LinkParserTests
{
    private const string Vid = "dQw4w9WgXcQ";
    private const string Pl = "PLrAXtmErZgOeiKm4sgNOknGvNjby9efdf";

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/watch?v=dQw4w9WgXcQ&t=42s")]
    [InlineData("https://m.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://music.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?si=abc&t=3")]
    [InlineData("https://www.youtube.com/live/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/live/dQw4w9WgXcQ?feature=share")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/shorts/dQw4w9WgXcQ?feature=share")]
    [InlineData("https://m.youtube.com/shorts/dQw4w9WgXcQ")]
    [InlineData("HTTPS://WWW.YOUTUBE.COM/watch?v=dQw4w9WgXcQ")]
    [InlineData("http://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("  https://youtu.be/dQw4w9WgXcQ  ")]
    public void Video_IsAcceptedAndCanonicalized(string input)
    {
        var r = LinkParser.Parse(input);

        var link = Assert.Single(r.Valid);
        Assert.Empty(r.Invalid);
        Assert.Equal(LinkKind.Video, link.Kind);
        Assert.Equal(Vid, link.Id);
        Assert.Equal($"https://www.youtube.com/watch?v={Vid}", link.Url);
        Assert.Equal(input.Trim(), link.OriginalText);
    }

    [Theory]
    [InlineData("https://www.youtube.com/playlist?list=PLrAXtmErZgOeiKm4sgNOknGvNjby9efdf")]
    [InlineData("https://www.youtube.com/playlist?list=PLrAXtmErZgOeiKm4sgNOknGvNjby9efdf&si=x")]
    // 同时带 v 与 list 的链接优先按 Playlist 处理
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=PLrAXtmErZgOeiKm4sgNOknGvNjby9efdf")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?list=PLrAXtmErZgOeiKm4sgNOknGvNjby9efdf")]
    [InlineData("https://www.youtube.com/watch?list=PLrAXtmErZgOeiKm4sgNOknGvNjby9efdf&v=dQw4w9WgXcQ&index=3")]
    public void Playlist_IsAcceptedAndCanonicalized(string input)
    {
        var link = Assert.Single(LinkParser.Parse(input).Valid);

        Assert.Equal(LinkKind.Playlist, link.Kind);
        Assert.Equal(Pl, link.Id);
        Assert.Equal($"https://www.youtube.com/playlist?list={Pl}", link.Url);
    }

    [Theory]
    // 实际遇到的链接：视频同时属于自动合辑
    [InlineData("https://www.youtube.com/watch?v=eMe4vE1X60M&list=RDeMe4vE1X60M&start_radio=1&t=14s", "RDeMe4vE1X60M", "eMe4vE1X60M")]
    [InlineData("https://www.youtube.com/watch?v=eMe4vE1X60M&list=RDMMeMe4vE1X60M", "RDMMeMe4vE1X60M", "eMe4vE1X60M")]
    [InlineData("https://www.youtube.com/watch?v=eMe4vE1X60M&list=RDGMEMCMFH2exzjBeE_zAHHJOdxg", "RDGMEMCMFH2exzjBeE_zAHHJOdxg", "eMe4vE1X60M")]
    [InlineData("https://youtu.be/eMe4vE1X60M?list=RDeMe4vE1X60M", "RDeMe4vE1X60M", "eMe4vE1X60M")]
    [InlineData("https://www.youtube.com/live/eMe4vE1X60M?list=RDeMe4vE1X60M", "RDeMe4vE1X60M", "eMe4vE1X60M")]
    // 只给合辑地址：用列表 ID 里的种子视频
    [InlineData("https://www.youtube.com/playlist?list=RDeMe4vE1X60M", "RDeMe4vE1X60M", "eMe4vE1X60M")]
    [InlineData("https://www.youtube.com/playlist?list=RDAMVMeMe4vE1X60M", "RDAMVMeMe4vE1X60M", "eMe4vE1X60M")]
    public void MixLinks_AreExpandablePlaylists_WithTheirSeedVideo(string input, string listId, string seed)
    {
        var link = Assert.Single(LinkParser.Parse(input).Valid);

        Assert.Equal(LinkKind.Playlist, link.Kind);
        Assert.Equal(listId, link.Id);
        Assert.Equal(seed, link.MixSeedVideoId);
        // 合辑只能用带 v= 的 watch 地址展开
        Assert.Equal($"https://www.youtube.com/watch?v={seed}&list={listId}", link.Url);
    }

    [Theory]
    [InlineData("https://www.youtube.com/playlist?list=RDCLAK5uy_kmPRjHDECIcuVwnKsx2Ng7fyNgFKWNJFs")]
    [InlineData("https://www.youtube.com/watch?v=eMe4vE1X60M&list=RDCLAK5uy_kmPRjHDECIcuVwnKsx2Ng7fyNgFKWNJFs")]
    public void RdclakAlbumPlaylists_AreStillPlaylists(string input)
    {
        var link = Assert.Single(LinkParser.Parse(input).Valid);
        Assert.Equal(LinkKind.Playlist, link.Kind);
    }

    [Fact]
    public void MixWithoutDerivableSeed_OnPlaylistUrl_StaysAPlainPlaylist()
    {
        var link = Assert.Single(LinkParser.Parse("https://www.youtube.com/playlist?list=RDGMEMCMFH2exzjBeE_zAHHJOdxg").Valid);
        Assert.Equal(LinkKind.Playlist, link.Kind);
        Assert.Null(link.MixSeedVideoId);
    }

    [Fact]
    public void Shorts_AreCanonicalizedToTheWatchUrl()
    {
        var link = Assert.Single(LinkParser.Parse("https://www.youtube.com/shorts/dIxySpfJf-8").Valid);
        Assert.Equal(LinkKind.Video, link.Kind);
        Assert.Equal("https://www.youtube.com/watch?v=dIxySpfJf-8", link.Url);
    }

    [Fact]
    public void PlainPlaylists_HaveNoMixSeed()
    {
        var link = Assert.Single(LinkParser.Parse("https://www.youtube.com/playlist?list=PLrAXtmErZgOeiKm4sgNOknGvNjby9efdf").Valid);
        Assert.Null(link.MixSeedVideoId);
    }

    [Fact]
    public void WatchWithInvalidList_FallsBackToVideo()
    {
        var link = Assert.Single(LinkParser.Parse("https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=x").Valid);
        Assert.Equal(LinkKind.Video, link.Kind);
    }

    [Theory]
    // 域名伪装
    [InlineData("https://youtube.com.evil.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://evilyoutube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com.evil.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://evil.com/?u=https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://evil.com/youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtube.com@evil.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com@evil.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://user:pw@www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com:8443/watch?v=dQw4w9WgXcQ")]
    // 协议与非 URL
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("--script=evil.lua")]
    [InlineData("dQw4w9WgXcQ")]
    [InlineData("hello world")]
    [InlineData("/home/user/music.mp3")]
    // 不受支持的路径
    [InlineData("https://www.youtube.com/")]
    [InlineData("https://www.youtube.com/results?search_query=x")]
    [InlineData("https://www.youtube.com/@channel")]
    [InlineData("https://www.youtube.com/shorts")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ/extra")]
    [InlineData("https://www.youtube.com/shorts/short")]
    [InlineData("https://www.youtube.com/watch/dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ/extra")]
    // ID 不合法
    [InlineData("https://www.youtube.com/watch")]
    [InlineData("https://www.youtube.com/watch?v=")]
    [InlineData("https://www.youtube.com/watch?v=short")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ0")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXc%20")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXc;")]
    [InlineData("https://www.youtube.com/playlist")]
    [InlineData("https://www.youtube.com/playlist?list=short")]
    [InlineData("https://www.youtube.com/playlist?list=PL$(calc)aaaaaaaaaaaaa")]
    public void Unsupported_IsRejectedWithReason(string input)
    {
        var r = LinkParser.Parse(input);

        Assert.Empty(r.Valid);
        var bad = Assert.Single(r.Invalid);
        Assert.False(string.IsNullOrEmpty(bad.Reason));
    }

    [Fact]
    public void TooLongLine_IsRejected()
    {
        var r = LinkParser.Parse("https://www.youtube.com/watch?v=dQw4w9WgXcQ&x=" + new string('a', LinkParser.MaxLineLength));
        Assert.Empty(r.Valid);
        Assert.Equal("TOO LONG", Assert.Single(r.Invalid).Reason);
    }

    [Fact]
    public void MultiLine_KeepsOrderAndIsolatesFailures()
    {
        var text = string.Join("\r\n",
            "https://youtu.be/AAAAAAAAAAA",
            "",
            "not a link",
            "   ",
            "https://www.youtube.com/playlist?list=PLrAXtmErZgOeiKm4sgNOknGvNjby9efdf",
            "https://youtu.be/BBBBBBBBBBB");

        var r = LinkParser.Parse(text);

        Assert.Equal(new[] { "AAAAAAAAAAA", Pl, "BBBBBBBBBBB" }, r.Valid.Select(l => l.Id));
        Assert.Equal("not a link", Assert.Single(r.Invalid).Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n \r\n ")]
    [InlineData(null)]
    public void EmptyInput_YieldsNothing(string? input)
    {
        var r = LinkParser.Parse(input);
        Assert.Empty(r.Valid);
        Assert.Empty(r.Invalid);
    }

    [Fact]
    public void SameVideoTwice_IsKeptTwice()
    {
        var r = LinkParser.Parse("https://youtu.be/AAAAAAAAAAA\nhttps://youtu.be/AAAAAAAAAAA");
        Assert.Equal(2, r.Valid.Count);
    }
}
