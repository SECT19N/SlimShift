namespace SlimShift;

/// <summary>A named set of encode settings that can be saved and applied to any input. Paths are never part of it.</summary>
/// <param name="Speed">The encoder's speed/efficiency preset (e.g. "slow"), not to be confused with the saved preset itself.</param>
public sealed record EncodePreset(
    VideoCodec? Codec = null,
    string? Encoder = null,
    int? Quality = null,
    string? Speed = null,
    bool PreferSize = false,
    int? Height = null,
    double? Fps = null,
    bool Interpolate = false) {

    /// <summary>Returns a problem with the values themselves, or null. Hardware availability is checked when the preset is used.</summary>
    public string? Validate() {
        if (Encoder != null) {
            EncoderInfo? known = EncoderCatalog.All.FirstOrDefault(e => e.Name == Encoder);
            if (known == null) return $"'{Encoder}' is not an encoder SlimShift supports.";
            if (Codec != null && Codec != known.Codec)
                return $"Encoder '{Encoder}' produces {known.Codec.DisplayName()}, not {Codec.Value.DisplayName()}.";
        }

        if (Quality is < 0) return "Quality cannot be negative.";
        if (Height is { } h && (h <= 0 || h % 2 != 0)) return "Height must be a positive, even number of pixels.";
        if (Fps is <= 0) return "Framerate must be greater than zero.";
        return null;
    }

    /// <summary>A short summary such as "av1, 360p, 30fps, q28".</summary>
    public string Describe() {
        List<string> parts = [];
        if (Encoder != null) parts.Add(Encoder);
        else if (Codec != null) parts.Add(Codec.Value.DisplayName());
        if (Height is { } h) parts.Add($"{h}p");
        if (Fps is { } f) parts.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{f:0.##}fps"));
        if (Quality is { } q) parts.Add($"q{q}");
        if (Speed != null) parts.Add(Speed);
        if (PreferSize) parts.Add("prefer size");
        if (Interpolate) parts.Add("interpolate");
        return parts.Count == 0 ? "(no settings)" : string.Join(", ", parts);
    }
}

public static class EncodePresetExtensions {
    /// <summary>
    /// Fills the gaps in a request from a preset. Anything the request sets explicitly wins. Codec and encoder
    /// travel together: if the request names either, the preset's codec and encoder are both ignored.
    /// </summary>
    public static EncodeRequest WithPreset(this EncodeRequest request, EncodePreset preset) {
        bool chooseEncoder = request.Codec != null || request.EncoderName != null;

        return request with {
            Codec = chooseEncoder ? request.Codec : preset.Codec,
            EncoderName = chooseEncoder ? request.EncoderName : preset.Encoder,
            Quality = request.Quality ?? preset.Quality,
            Preset = request.Preset ?? preset.Speed,
            PreferSize = request.PreferSize || preset.PreferSize,
            Height = request.Height ?? preset.Height,
            Fps = request.Fps ?? preset.Fps,
            Interpolate = request.Interpolate || preset.Interpolate
        };
    }
}
