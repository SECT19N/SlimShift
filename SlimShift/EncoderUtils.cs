using System.Text;
using Xabe.FFmpeg;

namespace SlimShift;

public class EncoderUtils {
	private static readonly string[] CpuPresets =
		["ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow"];
	private static readonly string[] GpuPresets = ["fast", "medium", "slow"];

	public static Task<string[]> GetAvailableEncodersForCodec(string codec, Dictionary<string, string[]> EncoderCache) {
		string[] allEncoders = EncoderCache.GetValueOrDefault(codec, []);
		List<string> availableEncoders = [];

		// Test encoder availability
		foreach (string encoder in allEncoders) {
			try {
				// Quick encoder availability test
				IConversion testConversion = FFmpeg.Conversions.New()
					.AddParameter($"-f lavfi -i color=black:size=1x1:duration=0.1 -c:v {encoder} -f null -");

				// Just check if we can create the conversion object
				availableEncoders.Add(encoder);
			} catch {
				// Encoder not available, skip
			}
		}

		// Prioritize hardware encoders
		string[] hardwareEncoders = availableEncoders.Where(IsHardwareEncoder).ToArray();
		string[] softwareEncoders = availableEncoders.Where(e => !IsHardwareEncoder(e)).ToArray();

		string[] result = new string[hardwareEncoders.Length + softwareEncoders.Length];
		hardwareEncoders.CopyTo(result, 0);
		softwareEncoders.CopyTo(result, hardwareEncoders.Length);

		return Task.FromResult(result.Length > 0 ? result : allEncoders);
	}

	static bool IsHardwareEncoder(string encoder) =>
		encoder.Contains("nvenc") || encoder.Contains("qsv") || encoder.Contains("amf");

	public static string[] GetPresetsForEncoder(string encoder) {
		ReadOnlySpan<char> encoderSpan = encoder.AsSpan();
		return encoderSpan.StartsWith("lib") ? CpuPresets : GpuPresets;
	}

	public static string GetDefaultExtension(string encoder) {
		ReadOnlySpan<char> encoderSpan = encoder.AsSpan();

		if (encoderSpan.Contains("x264", StringComparison.OrdinalIgnoreCase) ||
			encoderSpan.Contains("h264", StringComparison.OrdinalIgnoreCase) ||
			encoderSpan.Contains("x265", StringComparison.OrdinalIgnoreCase) ||
			encoderSpan.Contains("hevc", StringComparison.OrdinalIgnoreCase))
			return ".mp4";

		if (encoderSpan.Contains("vp9", StringComparison.OrdinalIgnoreCase))
			return ".webm";

		if (encoderSpan.Contains("av1", StringComparison.OrdinalIgnoreCase))
			return ".mkv";

		return ".mp4";
	}

	public static string BuildCodecArgument(string encoder, string preset, int crf) {
		StringBuilder args = new(128); // Pre-allocate reasonable capacity

		ReadOnlySpan<char> encoderSpan = encoder.AsSpan();

		if (encoderSpan.Contains("264", StringComparison.OrdinalIgnoreCase)) {
			args.Append($"-c:v {encoder} -preset {preset} -crf {crf}");
			if (encoderSpan.Contains("nvenc", StringComparison.OrdinalIgnoreCase))
				args.Append(" -rc vbr");
			args.Append(" -c:a copy");
		} else if (encoderSpan.Contains("265", StringComparison.OrdinalIgnoreCase) ||
				   encoderSpan.Contains("hevc", StringComparison.OrdinalIgnoreCase)) {
			args.Append($"-c:v {encoder} -preset {preset} -crf {crf}");
			if (encoderSpan.Contains("nvenc", StringComparison.OrdinalIgnoreCase))
				args.Append(" -rc vbr");
			args.Append(" -c:a copy");
		} else if (encoderSpan.Contains("vp9", StringComparison.OrdinalIgnoreCase)) {
			args.Append($"-c:v {encoder} -b:v 0 -crf {crf} -row-mt 1 -c:a libopus");
		} else if (encoderSpan.Contains("av1", StringComparison.OrdinalIgnoreCase)) {
			args.Append($"-c:v {encoder} -crf {crf} -b:v 0 -cpu-used 4 -row-mt 1 -tile-columns 2 -tile-rows 2 -c:a libopus");
		} else {
			args.Append($"-c:v {encoder} -c:a copy");
		}

		return args.ToString();
	}
}