using System.Globalization;
using System.Text.Json;

namespace Halka.Core;

public sealed class MediaOffer
{
    public string Kind { get; init; } = "";
    public string Label { get; init; } = "";
    public string Detail { get; init; } = "";
    public string SizeLabel { get; init; } = "—";
    public string Selector { get; init; } = "";
    public string? MergeExt { get; init; }
    public string? AudioFormat { get; init; }
    public bool NeedsFfmpeg { get; init; }
}

public sealed class MediaCatalog
{
    public string Title { get; init; } = "";
    public string DurationLabel { get; init; } = "";
    public IReadOnlyList<MediaOffer> Offers { get; init; } = [];
}

public static class FormatList
{
    public static MediaCatalog Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Object &&
            !root.TryGetProperty("formats", out _) &&
            root.TryGetProperty("entries", out var entries) &&
            entries.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("formats", out _))
                {
                    root = entry;
                    break;
                }
            }
        }

        var title = Str(root, "title");
        if (string.IsNullOrWhiteSpace(title))
            title = Str(root, "fulltitle");
        title = title.Trim();
        if (title.Length > 140)
            title = title[..140].Trim();

        var duration = Num(root, "duration") ?? 0;
        var streams = new List<Stream>();
        if (root.TryGetProperty("formats", out var formats) && formats.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in formats.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;
                var stream = Read(item);
                if (stream is not null)
                    streams.Add(stream);
            }
        }

        var videos = streams.Where(item => item.HasVideo && item.Height > 0).ToList();
        var audios = streams.Where(item => item.HasAudio && !item.HasVideo).ToList();
        var audio = PickAudio(audios, "m4a") ?? PickAudio(audios, "mp4");
        var opus = PickAudio(audios, "webm");
        var offers = new List<MediaOffer>();

        foreach (var height in videos.Select(item => item.Height).Distinct().OrderByDescending(item => item))
        {
            var atHeight = videos.Where(item => item.Height == height).ToList();
            var fps = atHeight.Max(item => item.Fps);
            var chosen = PickVideo(atHeight.Where(item => item.Fps == fps).ToList());
            if (chosen is null)
                continue;
            offers.Add(VideoOffer(chosen, audio, fps >= 48 ? $"{height}p{fps}" : $"{height}p"));
        }

        if (audio is not null)
            offers.Add(AudioFile("M4A", "m4a", audio, null));
        if (opus is not null && opus.Id != audio?.Id)
            offers.Add(AudioFile("OPUS", "opus", opus, null));

        var source = audio ?? opus ?? videos.Where(item => item.HasAudio).OrderByDescending(item => item.Tbr).FirstOrDefault();
        if (source is not null)
        {
            offers.Add(AudioFile("MP3", "mp3", source, "mp3"));
            offers.Add(AudioFile("WAV", "wav", source, "wav"));
        }

        return new MediaCatalog
        {
            Title = title,
            DurationLabel = Clock(duration),
            Offers = offers
        };
    }

    static MediaOffer VideoOffer(Stream video, Stream? audio, string label)
    {
        var codec = Codec(video.Vcodec);
        if (video.HasAudio || audio is null)
        {
            return new MediaOffer
            {
                Kind = "video",
                Label = label,
                Detail = video.Ext + " · " + codec,
                SizeLabel = SizeText(video.Bytes, false),
                Selector = video.Id,
                NeedsFfmpeg = false
            };
        }

        var merge = video.Ext.Equals("webm", StringComparison.OrdinalIgnoreCase) ? "webm" : "mp4";
        long? bytes = video.Bytes is > 0 && audio.Bytes is > 0 ? video.Bytes + audio.Bytes : video.Bytes ?? audio.Bytes;
        return new MediaOffer
        {
            Kind = "video",
            Label = label,
            Detail = merge + " · " + codec,
            SizeLabel = SizeText(bytes, false),
            Selector = video.Id + "+" + audio.Id,
            MergeExt = merge,
            NeedsFfmpeg = true
        };
    }

    static MediaOffer AudioFile(string label, string detail, Stream source, string? convert)
    {
        return new MediaOffer
        {
            Kind = "audio",
            Label = label,
            Detail = detail,
            SizeLabel = SizeText(source.Bytes, convert is not null),
            Selector = source.Id,
            AudioFormat = convert,
            NeedsFfmpeg = convert is not null
        };
    }

    static Stream? PickVideo(List<Stream> group)
    {
        if (group.Count == 0)
            return null;
        var https = group.Where(item => item.Protocol.StartsWith("https", StringComparison.OrdinalIgnoreCase)).ToList();
        var pool = https.Count > 0 ? https : group.Where(item => !item.Protocol.Contains("m3u8", StringComparison.OrdinalIgnoreCase)).ToList();
        if (pool.Count == 0)
            pool = group;
        var avc = pool.Where(item => item.Vcodec.StartsWith("avc1", StringComparison.OrdinalIgnoreCase) && item.Ext.Equals("mp4", StringComparison.OrdinalIgnoreCase)).ToList();
        var mp4 = pool.Where(item => item.Ext.Equals("mp4", StringComparison.OrdinalIgnoreCase)).ToList();
        var chosen = avc.Count > 0 ? avc : mp4.Count > 0 ? mp4 : pool;
        return chosen.OrderByDescending(item => item.HasAudio ? 0 : 1).ThenByDescending(item => item.Tbr).First();
    }

    static Stream? PickAudio(List<Stream> audios, string ext)
    {
        var pool = audios.Where(item => item.Ext.Equals(ext, StringComparison.OrdinalIgnoreCase)).ToList();
        if (pool.Count == 0)
            return null;
        var https = pool.Where(item => item.Protocol.Length == 0 || item.Protocol.StartsWith("https", StringComparison.OrdinalIgnoreCase)).ToList();
        if (https.Count > 0)
            pool = https;
        return pool.OrderByDescending(ScoreAudio).First();
    }

    static double ScoreAudio(Stream item)
    {
        var score = item.Abr ?? item.Tbr;
        var note = item.Note;
        if (note.Contains("original", StringComparison.OrdinalIgnoreCase))
            score += 10_000;
        if (note.Contains("default", StringComparison.OrdinalIgnoreCase))
            score += 1_000;
        if (note.Contains("DRC", StringComparison.OrdinalIgnoreCase))
            score -= 5_000;
        if (note.Contains("low", StringComparison.OrdinalIgnoreCase))
            score -= 200;
        return score;
    }

    static Stream? Read(JsonElement item)
    {
        var id = Str(item, "format_id");
        if (id.Length is 0 or > 40)
            return null;
        if (id.StartsWith("sb", StringComparison.OrdinalIgnoreCase))
            return null;
        var ext = Str(item, "ext").ToLowerInvariant();
        if (ext is "" or "mhtml" or "jpg" or "png" or "webp")
            return null;
        var protocol = Str(item, "protocol");
        if (protocol.Contains("mhtml", StringComparison.OrdinalIgnoreCase))
            return null;
        var vcodec = Str(item, "vcodec");
        var acodec = Str(item, "acodec");
        var height = (int)(Num(item, "height") ?? 0);
        var fps = (int)Math.Round(Num(item, "fps") ?? 0);
        return new Stream(
            id,
            ext,
            vcodec,
            acodec,
            height,
            fps,
            LongOf(item, "filesize") ?? LongOf(item, "filesize_approx"),
            Num(item, "tbr") ?? 0,
            Num(item, "abr"),
            protocol,
            Str(item, "format_note"));
    }

    static string Codec(string vcodec)
    {
        if (vcodec.StartsWith("avc1", StringComparison.OrdinalIgnoreCase))
            return "avc1";
        if (vcodec.StartsWith("vp9", StringComparison.OrdinalIgnoreCase) || vcodec.StartsWith("vp09", StringComparison.OrdinalIgnoreCase))
            return "vp9";
        if (vcodec.StartsWith("av01", StringComparison.OrdinalIgnoreCase))
            return "av1";
        var dot = vcodec.IndexOf('.');
        var shortName = dot > 0 ? vcodec[..dot] : vcodec;
        return shortName.Length == 0 ? "video" : shortName;
    }

    static string Clock(double seconds)
    {
        if (seconds <= 0 || double.IsNaN(seconds) || seconds > 86_400 * 48)
            return "";
        var time = TimeSpan.FromSeconds(seconds);
        return $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
    }

    static string SizeText(long? bytes, bool approx)
    {
        if (bytes is null or <= 0)
            return "—";
        var text = bytes >= 1024 * 1024
            ? (bytes.Value / 1048576m).ToString("0.00", CultureInfo.InvariantCulture) + " MB"
            : (bytes.Value / 1024m).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
        return approx ? "~" + text : text;
    }

    static string Str(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    static double? Num(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number))
            return null;
        return number;
    }

    static long? LongOf(JsonElement item, string name)
    {
        var number = Num(item, name);
        if (number is null or <= 0 or > 500_000_000_000)
            return null;
        return (long)number;
    }

    sealed record Stream(
        string Id,
        string Ext,
        string Vcodec,
        string Acodec,
        int Height,
        int Fps,
        long? Bytes,
        double Tbr,
        double? Abr,
        string Protocol,
        string Note)
    {
        public bool HasVideo => Vcodec.Length > 0 && !Vcodec.Equals("none", StringComparison.OrdinalIgnoreCase);
        public bool HasAudio => Acodec.Length > 0 && !Acodec.Equals("none", StringComparison.OrdinalIgnoreCase);
    }
}
