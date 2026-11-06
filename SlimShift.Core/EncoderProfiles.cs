namespace SlimShift;

public sealed record QualityRange(int Min, int Max, int Default);

/// <summary>Per-encoder quality/preset options and the ffmpeg arguments they translate to.</summary>
/// <remarks>
/// Option names were checked against <c>ffmpeg -h encoder=...</c> for NVENC, QSV, AMF, SVT-AV1, libaom and
/// libvpx-vp9. VideoToolbox is not available in the Windows build and is untested.
/// </remarks>
public static class EncoderProfiles {
    static readonly string[] CpuPresets =
        ["ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow"];
    static readonly string[] SimplePresets = ["fast", "medium", "slow"];

    public static IReadOnlyList<string> GetPresets(EncoderInfo encoder) => encoder.Name switch {
        "libx264" or "libx265" => CpuPresets,
        "libvpx-vp9" or "libaom-av1" => [], // no meaningful preset
        _ when encoder.Vendor == EncoderVendor.Apple => [],
        _ => SimplePresets
    };

    public static string? GetDefaultPreset(EncoderInfo encoder) =>
        GetPresets(encoder).Count == 0 ? null : "medium";

    public static QualityRange GetQualityRange(EncoderInfo encoder) {
        int def = encoder.Codec switch {
            VideoCodec.H264 => 23,
            VideoCodec.H265 => 28,
            _ => 30
        };

        bool wide = encoder.Codec is VideoCodec.VP9 or VideoCodec.AV1;
        int max = wide ? 63 : 51;

        // NVENC treats cq 0 as "automatic" and QSV ICQ starts at 1.
        int min = encoder.Vendor is EncoderVendor.Nvidia or EncoderVendor.Intel ? 1 : 0;
        return new QualityRange(min, max, def);
    }

    public static string DefaultExtension(VideoCodec codec) => codec switch {
        VideoCodec.VP9 => ".webm",
        VideoCodec.AV1 => ".mkv",
        _ => ".mp4"
    };

    /// <summary>Video encoder arguments as separate tokens (safe to pass via ArgumentList).</summary>
    public static List<string> BuildVideoArgs(EncoderInfo encoder, string? preset, int quality) {
        string q = quality.ToString();
        List<string> args = ["-c:v", encoder.Name];

        switch (encoder.Name) {
            case "libx264":
                args.AddRange(["-preset", preset ?? "medium", "-crf", q]);
                break;
            case "libx265":
                args.AddRange(["-preset", preset ?? "medium", "-crf", q, "-tag:v", "hvc1"]);
                break;
            case "libvpx-vp9":
                args.AddRange(["-b:v", "0", "-crf", q, "-row-mt", "1"]);
                break;
            case "libaom-av1":
                args.AddRange(["-b:v", "0", "-crf", q, "-cpu-used", "4", "-row-mt", "1", "-tile-columns", "2", "-tile-rows", "2"]);
                break;
            case "libsvtav1": {
                    string svt = preset switch { "fast" => "10", "slow" => "4", _ => "6" };
                    args.AddRange(["-crf", q, "-preset", svt]);
                    break;
                }
            default:
                switch (encoder.Vendor) {
                    case EncoderVendor.Nvidia: {
                            string p = preset switch { "fast" => "p3", "slow" => "p6", _ => "p4" };
                            args.AddRange(["-preset", p, "-rc", "vbr", "-cq", q, "-b:v", "0"]);
                            break;
                        }
                    case EncoderVendor.Intel:
                        args.AddRange(["-preset", preset ?? "medium", "-global_quality", q]);
                        break;
                    case EncoderVendor.Amd: {
                            string amf = preset switch { "fast" => "speed", "slow" => "quality", _ => "balanced" };
                            // av1_amf uses a 0-255 QP scale; the others use 0-51.
                            string qp = encoder.Codec == VideoCodec.AV1 ? (quality * 4).ToString() : q;
                            args.AddRange(["-quality", amf, "-rc", "cqp", "-qp_i", qp, "-qp_p", qp]);
                            break;
                        }
                    case EncoderVendor.Apple: {
                            // -q:v is 1-100 where higher is better; invert the CRF-style scale.
                            int vt = Math.Clamp(100 - quality * 2, 1, 100);
                            args.AddRange(["-q:v", vt.ToString()]);
                            break;
                        }
                }
                break;
        }

        if (encoder.Codec == VideoCodec.H265 && encoder.Vendor != EncoderVendor.Software && !args.Contains("-tag:v"))
            args.AddRange(["-tag:v", "hvc1"]);

        return args;
    }

    static readonly HashSet<string> Mp4Audio = ["aac", "mp3", "ac3", "eac3", "alac", "opus"];
    static readonly HashSet<string> WebmAudio = ["opus", "vorbis"];

    /// <summary>Copy audio when the container accepts it, otherwise transcode.</summary>
    public static List<string> BuildAudioArgs(string extension, IReadOnlyList<string> sourceAudioCodecs) {
        if (sourceAudioCodecs.Count == 0) return ["-an"];

        bool compatible = extension.ToLowerInvariant() switch {
            ".mkv" => true,
            ".webm" => sourceAudioCodecs.All(WebmAudio.Contains),
            _ => sourceAudioCodecs.All(Mp4Audio.Contains)
        };
        
        if (compatible) return ["-c:a", "copy"];

        return extension.Equals(".webm", StringComparison.OrdinalIgnoreCase)
            ? ["-c:a", "libopus", "-b:a", "128k"]
            : ["-c:a", "aac", "-b:a", "192k"];
    }
}
