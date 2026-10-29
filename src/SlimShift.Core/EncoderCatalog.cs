namespace SlimShift;

public enum VideoCodec { H264, H265, VP9, AV1 }

public enum EncoderVendor { Software, Nvidia, Intel, Amd, Apple }

public sealed record EncoderInfo(string Name, VideoCodec Codec, EncoderVendor Vendor) {
    public bool IsHardware => Vendor != EncoderVendor.Software;
}

/// <summary>Every encoder SlimShift knows how to drive, per codec, in catalog order.</summary>
public static class EncoderCatalog {
    public static IReadOnlyList<EncoderInfo> All { get; } = [
        new("libx264", VideoCodec.H264, EncoderVendor.Software),
        new("h264_nvenc", VideoCodec.H264, EncoderVendor.Nvidia),
        new("h264_qsv", VideoCodec.H264, EncoderVendor.Intel),
        new("h264_amf", VideoCodec.H264, EncoderVendor.Amd),
        new("h264_videotoolbox", VideoCodec.H264, EncoderVendor.Apple),

        new("libx265", VideoCodec.H265, EncoderVendor.Software),
        new("hevc_nvenc", VideoCodec.H265, EncoderVendor.Nvidia),
        new("hevc_qsv", VideoCodec.H265, EncoderVendor.Intel),
        new("hevc_amf", VideoCodec.H265, EncoderVendor.Amd),
        new("hevc_videotoolbox", VideoCodec.H265, EncoderVendor.Apple),

        new("libvpx-vp9", VideoCodec.VP9, EncoderVendor.Software),
        new("vp9_qsv", VideoCodec.VP9, EncoderVendor.Intel),

        new("libsvtav1", VideoCodec.AV1, EncoderVendor.Software),
        new("libaom-av1", VideoCodec.AV1, EncoderVendor.Software),
        new("av1_nvenc", VideoCodec.AV1, EncoderVendor.Nvidia),
        new("av1_qsv", VideoCodec.AV1, EncoderVendor.Intel),
        new("av1_amf", VideoCodec.AV1, EncoderVendor.Amd)
    ];

    public static string DisplayName(this VideoCodec codec) => codec switch {
        VideoCodec.H264 => "H.264",
        VideoCodec.H265 => "H.265",
        VideoCodec.VP9 => "VP9",
        VideoCodec.AV1 => "AV1",
        _ => codec.ToString()
    };
}
