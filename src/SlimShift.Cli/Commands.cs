using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SlimShift.Cli;

public class EncodeSettings : CommandSettings {
	[CommandArgument(0, "<INPUT>")]
	[Description("Video file to convert.")]
	public string Input { get; set; } = "";

	[CommandOption("-o|--output <PATH>")]
	[Description("Output file. Defaults to <input>-slim.<ext> beside the input.")]
	public string? Output { get; set; }

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
		TryParseCodec(Codec, out VideoCodec? codec);
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
		Height == null ? ValidationResult.Error("--height is required.") : base.Validate();
}

public sealed class FpsSettings : EncodeSettings {
	[CommandOption("--fps <RATE>")]
	[Description("Target framerate, e.g. 30.")]
	public double? Fps { get; set; }

	[CommandOption("--interpolate")]
	[Description("Synthesise new frames (slow) instead of dropping/duplicating.")]
	public bool Interpolate { get; set; }

	public override ValidationResult Validate() =>
		Fps == null ? ValidationResult.Error("--fps is required.") : base.Validate();
}

static class CommandHelper {
	public static async Task<int> Run(EncodeSettings settings, EncodeRequest request) {
		Session? session = await Session.CreateAsync();
		return session == null ? 1 : await EncodeRunner.RunAsync(session, request, settings.Overwrite);
	}
}

public sealed class ConvertCommand : AsyncCommand<ConvertSettings> {
	public override Task<int> ExecuteAsync(CommandContext context, ConvertSettings s) =>
		CommandHelper.Run(s, s.ToRequest(s.Height, s.Fps, s.Interpolate));
}

public sealed class ScaleCommand : AsyncCommand<ScaleSettings> {
	public override Task<int> ExecuteAsync(CommandContext context, ScaleSettings s) =>
		CommandHelper.Run(s, s.ToRequest(s.Height, null, false));
}

public sealed class FpsCommand : AsyncCommand<FpsSettings> {
	public override Task<int> ExecuteAsync(CommandContext context, FpsSettings s) =>
		CommandHelper.Run(s, s.ToRequest(null, s.Fps, s.Interpolate));
}
