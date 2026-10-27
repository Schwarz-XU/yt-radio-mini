using System.Text.Json;
using YTRadioMini.Models;

namespace YTRadioMini.Services;

/// <summary>解析 <c>yt-dlp --flat-playlist --dump-single-json</c> 的输出。</summary>
public static class YtDlpPlaylistParser
{
    public const int MaxTitleLength = 300;

    private static readonly HashSet<string> PlaceholderTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        "[Private video]", "[Deleted video]", "[Unavailable video]",
    };

    public static ResolveResult Parse(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return ResolveResult.Fail(ResolveErrorKind.ParseFailed, "BAD YT-DLP OUTPUT");
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("entries", out var entries) ||
                entries.ValueKind != JsonValueKind.Array)
            {
                return ResolveResult.Fail(ResolveErrorKind.ParseFailed, "BAD YT-DLP OUTPUT");
            }

            var items = new List<QueueItem>();
            var seenPlaceholders = 0;

            foreach (var e in entries.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;

                var id = GetString(e, "id");
                if (id is null || !LinkParser.IsValidVideoId(id)) continue; // 嵌套播放列表、频道等

                var title = GetString(e, "title") ?? "";
                if (PlaceholderTitles.Contains(title)) { seenPlaceholders++; continue; }

                items.Add(new QueueItem(id, LinkParser.VideoUrl(id), CleanTitle(title), IsLive(e)));
            }

            if (items.Count == 0)
            {
                return ResolveResult.Fail(ResolveErrorKind.Unavailable,
                    seenPlaceholders > 0 ? "NO PLAYABLE VIDEOS" : "EMPTY PLAYLIST");
            }

            return ResolveResult.Ok(items);
        }
    }

    private static bool IsLive(JsonElement e)
    {
        if (GetString(e, "live_status") == "is_live") return true;
        return e.TryGetProperty("is_live", out var v) && v.ValueKind == JsonValueKind.True;
    }

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string CleanTitle(string title)
    {
        var chars = title.Where(c => !char.IsControl(c)).Take(MaxTitleLength).ToArray();
        return new string(chars).Trim();
    }
}
