using System.Globalization;
using System.Text.Json;

namespace SlimShift;

public sealed record VideoInfo(
    int Width,
    int Height,
    double Fps,
    TimeSpan? Duration,
    string VideoCodecName,
    IReadOnlyList<string> AudioCodecs) {
    /// <summary>The codec family of the source, when SlimShift can encode to it.</summary>
    public VideoCodec? Codec => VideoCodecName switch {
        "h264" => VideoCodec.H264,
        "hevc" => VideoCodec.H265,
        "vp9" => VideoCodec.VP9,
        "av1" => VideoCodec.AV1,
        _ => null
    };
}

/// <summary>Reads stream information from a media file with ffprobe.</summary>
public static class VideoProbe {
    public static async Task<VideoInfo> ProbeAsync(
        FFmpegTools tools, string path, CancellationToken cancellationToken = default) {
        ProcessResult result = await FFmpegProcess.RunAsync(tools.FFprobePath, [
            "-v", "error", "-print_format", "json", "-show_streams", "-show_format", path
        ], TimeSpan.FromSeconds(30), cancellationToken);

        if (!result.Succeeded)
            throw new InvalidOperationException($"ffprobe could not read '{path}': {result.StdErr.Trim()}");

        return ParseJson(result.StdOut);
    }

    public static VideoInfo ParseJson(string json) {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        JsonElement[] streams = root.TryGetProperty("streams", out JsonElement s)
            ? [.. s.EnumerateArray()] : [];

        // Skip attached pictures (cover art), which ffprobe reports as video streams.
        JsonElement? video = streams
            .Where(x => Str(x, "codec_type") == "video" && !IsAttachedPicture(x))
            .Select(x => (JsonElement?)x).FirstOrDefault();
        if (video is not { } v)
            throw new InvalidOperationException("No video stream found in the file.");

        string[] audio = streams.Where(x => Str(x, "codec_type") == "audio")
            .Select(x => Str(x, "codec_name") ?? "").ToArray();

        double fps = ParseRate(Str(v, "avg_frame_rate"));
        if (fps <= 0) fps = ParseRate(Str(v, "r_frame_rate"));

        TimeSpan? duration = ParseSeconds(Str(v, "duration"));
        if (duration == null && root.TryGetProperty("format", out JsonElement format))
            duration = ParseSeconds(Str(format, "duration"));

        return new VideoInfo(
            v.TryGetProperty("width", out JsonElement w) ? w.GetInt32() : 0,
            v.TryGetProperty("height", out JsonElement h) ? h.GetInt32() : 0,
            fps, duration, Str(v, "codec_name") ?? "", audio);
    }

    static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    static bool IsAttachedPicture(JsonElement stream) =>
        stream.TryGetProperty("disposition", out JsonElement d) &&
        d.TryGetProperty("attached_pic", out JsonElement a) && a.ValueKind == JsonValueKind.Number && a.GetInt32() == 1;

    static TimeSpan? ParseSeconds(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double sec) && sec > 0
            ? TimeSpan.FromSeconds(sec) : null;

    /// <summary>Parses ffprobe rates like "30000/1001" or "25".</summary>
    public static double ParseRate(string? rate) {
        if (string.IsNullOrEmpty(rate)) return 0;
        string[] parts = rate.Split('/');
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double num)) return 0;
        if (parts.Length == 1) return num;
        return double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double den) && den != 0
            ? num / den : 0;
    }
}
