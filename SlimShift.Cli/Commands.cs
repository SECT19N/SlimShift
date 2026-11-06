using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace SlimShift.Cli;

public class EncodeSettings : CommandSettings {
    [CommandArgument(0, "<INPUT>")]
    [Description("Video file to convert, or a folder to convert every video in it.")]
    public string Input { get; set; } = "";

    [CommandOption("-o|--output <PATH>")]
    [Description("Output file (or output folder for a folder input). Defaults to a name describing what changed (e.g. movie-av1-720p.mp4) beside the input, or a 'converted' folder inside the input folder.")]
    public string? Output { get; set; }

    [CommandOption("--use <PRESET>")]
    [Description("Apply a saved preset (see 'presets list'). Options given here override the preset.")]
    public string? Use { get; set; }

    [CommandOption("--recursive")]
    [Description("With a folder input, include videos in subfolders.")]
    public bool Recursive { get; set; }

    [CommandOption("--codec <CODEC>")]
    [Description("h264, h265, vp9 or av1. Defaults to the source codec.")]
    public string? Codec { get; set; }

    [CommandOption("--encoder <NAME>")]
    [Description("Specific encoder, e.g. libx264 or hevc_nvenc. Must work on this machine.")]
    public string? Encoder { get; set; }

    [CommandOption("-q|--quality <VALUE>")]
    [Description("Quality (CRF-style; lower is better). Range depends on the encoder.")]
    public int? Quality { get; set; }

    [CommandOption("--preset <NAME>")]
    [Description("Speed/efficiency preset, where the encoder supports one.")]
    public string? Preset { get; set; }

    [CommandOption("--prefer-size")]
    [Description("Pick software encoders (slower, smaller files) over hardware.")]
    public bool PreferSize { get; set; }

    [CommandOption("--overwrite")]
    [Description("Replace the output file if it exists.")]
    public bool Overwrite { get; set; }

    public static bool TryParseCodec(string? text, out VideoCodec? codec) {
        codec = null;
        
        if (text == null) return true;
        
        switch (text.ToLowerInvariant().Replace(".", "").Replace("-", "")) {
            case "h264" or "x264" or "avc": codec = VideoCodec.H264; return true;
            case "h265" or "x265" or "hevc": codec = VideoCodec.H265; return true;
            case "vp9": codec = VideoCodec.VP9; return true;
            case "av1": codec = VideoCodec.AV1; return true;
            default: return false;
        }
    }

    public override ValidationResult Validate() =>
        TryParseCodec(Codec, out _) ? ValidationResult.Success()
            : ValidationResult.Error($"Unknown codec '{Codec}'. Use h264, h265, vp9 or av1.");

    public EncodeRequest ToRequest(int? height, double? fps, bool interpolate) {
        _ = TryParseCodec(Codec, out VideoCodec? codec);

        return new EncodeRequest(Input.Trim('"'), Output, codec, Encoder, Quality, Preset, PreferSize, height, fps, interpolate);
    }
}

public sealed class ConvertSettings : EncodeSettings {
    [CommandOption("--height <PIXELS>")]
    [Description("Optionally resize to this height (width follows).")]
    public int? Height { get; set; }

    [CommandOption("--fps <RATE>")]
    [Description("Optionally change the framerate.")]
    public double? Fps { get; set; }

    [CommandOption("--interpolate")]
    [Description("With --fps, synthesise new frames (slow) instead of dropping/duplicating.")]
    public bool Interpolate { get; set; }
}

public sealed class ScaleSettings : EncodeSettings {
    [CommandOption("--height <PIXELS>")]
    [Description("Target height in pixels, e.g. 720 (width follows, aspect ratio kept).")]
    public int? Height { get; set; }

    public override ValidationResult Validate() =>
        Height == null && Use == null ? ValidationResult.Error("--height is required (or --use a preset that has one).") : base.Validate();
}

public sealed class FpsSettings : EncodeSettings {
    [CommandOption("--fps <RATE>")]
    [Description("Target framerate, e.g. 30.")]
    public double? Fps { get; set; }

    [CommandOption("--interpolate")]
    [Description("Synthesise new frames (slow) instead of dropping/duplicating.")]
    public bool Interpolate { get; set; }

    public override ValidationResult Validate() =>
        Fps == null && Use == null ? ValidationResult.Error("--fps is required (or --use a preset that has one).") : base.Validate();
}

static class CommandHelper {
    /// <summary>Applies the preset, checks the input, then converts one file or a whole folder.</summary>
    public static async Task<int> Run(
        EncodeSettings settings, int? height, double? fps, bool interpolate, bool needHeight = false, bool needFps = false) {
        EncodeRequest request = settings.ToRequest(height, fps, interpolate);

        if (settings.Use != null) {
            try {
                request = request.WithPreset(new PresetStore(PresetStore.DefaultDirectory).Load(settings.Use));
            } catch (PresetException ex) {
                return Fail(ex.Message);
            }
        }

        if (needHeight && request.Height == null) return Fail($"Preset '{settings.Use}' has no height; add --height.");
        if (needFps && request.Fps == null) return Fail($"Preset '{settings.Use}' has no framerate; add --fps.");

        bool isFolder = Directory.Exists(request.Input);

        if (isFolder && request.Output != null) {
            if (File.Exists(request.Output) || Path.HasExtension(request.Output))
                return Fail("For a folder input, -o must be an output folder, not a file.");
            request = request with { Output = null, OutputDirectory = request.Output };
        } else if (!isFolder && settings.Recursive) {
            return Fail("--recursive only applies to a folder input.");
        }

        Session? session = await Session.CreateAsync();
        if (session == null) return 1;

        return isFolder
            ? await BatchRunner.RunAsync(session, request.Input, request, settings.Recursive, settings.Overwrite)
            : await EncodeRunner.RunAsync(session, request, settings.Overwrite);
    }

    public static int Fail(string message) {
        AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(message)}[/]");
        return 1;
    }
}

public sealed class ConvertCommand : AsyncCommand<ConvertSettings> {
    public override Task<int> ExecuteAsync(CommandContext context, ConvertSettings s) =>
        CommandHelper.Run(s, s.Height, s.Fps, s.Interpolate);
}

public sealed class ScaleCommand : AsyncCommand<ScaleSettings> {
    public override Task<int> ExecuteAsync(CommandContext context, ScaleSettings s) =>
        CommandHelper.Run(s, s.Height, null, false, needHeight: true);
}

public sealed class FpsCommand : AsyncCommand<FpsSettings> {
    public override Task<int> ExecuteAsync(CommandContext context, FpsSettings s) =>
        CommandHelper.Run(s, null, s.Fps, s.Interpolate, needFps: true);
}
