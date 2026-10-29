using System.Diagnostics;
using System.Text;

namespace SlimShift;

public class EncoderUtils {
	private static readonly string[] CpuPresets =
		["ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow"];
	private static readonly string[] SimplePresets = ["fast", "medium", "slow"];

	/// <summary>Encoders SlimShift knows how to drive, per codec, in preference order.</summary>
	public static readonly Dictionary<string, string[]> Catalog = new() {
		["H.264"] = ["libx264", "h264_nvenc", "h264_qsv", "h264_amf", "h264_videotoolbox"],
		["H.265"] = ["libx265", "hevc_nvenc", "hevc_qsv", "hevc_amf", "hevc_videotoolbox"],
		["VP9"] = ["libvpx-vp9", "vp9_qsv"],
		["AV1"] = ["libsvtav1", "libaom-av1", "av1_nvenc", "av1_qsv", "av1_amf"]
	};

	/// <summary>Runs ffmpeg and returns its exit code and combined output.</summary>
	static async Task<(int ExitCode, string Output)> RunFFmpeg(string arguments) {
		ProcessStartInfo info = new() {
			FileName = FFmpegSetup.FFmpegPath,
			Arguments = arguments,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};

		using Process process = Process.Start(info) ?? throw new InvalidOperationException("Could not start ffmpeg.");
		Task<string> stdout = process.StandardOutput.ReadToEndAsync();
		Task<string> stderr = process.StandardError.ReadToEndAsync();

		using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
		try {
			await process.WaitForExitAsync(timeout.Token);
		} catch (OperationCanceledException) {
			process.Kill(entireProcessTree: true);
			return (-1, "timed out");
		}

		return (process.ExitCode, await stdout + await stderr);
	}

	/// <summary>Names of every video encoder compiled into the ffmpeg build.</summary>
	static async Task<HashSet<string>> ListCompiledEncoders() {
		(int exitCode, string output) = await RunFFmpeg("-hide_banner -encoders");
		HashSet<string> names = [];
		if (exitCode != 0) return names;

		foreach (string line in output.Split('\n')) {
			// Lines look like " V....D libx264   libx264 H.264 / AVC ..."
			string[] parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length >= 2 && parts[0].Length == 6 && parts[0][0] == 'V')
				names.Add(parts[1]);
		}
		return names;
	}

	/// <summary>True if ffmpeg can actually encode a frame with this encoder on this machine.</summary>
	static async Task<bool> ProbeEncoder(string encoder) {
		// 256x256: hardware encoders reject tiny frames.
		(int exitCode, _) = await RunFFmpeg(
			$"-hide_banner -v error -f lavfi -i color=black:s=256x256:d=0.1 -frames:v 1 -c:v {encoder} -f null -");
		return exitCode == 0;
	}

	public static async Task<string[]> GetAvailableEncodersForCodec(string codec, Dictionary<string, string[]> encoderCache) {
		string[] candidates = encoderCache.GetValueOrDefault(codec, []);
		HashSet<string> compiled = await ListCompiledEncoders();

		string[] listed = candidates.Where(compiled.Contains).ToArray();
		bool[] works = await Task.WhenAll(listed.Select(ProbeEncoder));
		string[] available = listed.Where((_, i) => works[i]).ToArray();

		// Hardware encoders first, software after (each group keeps catalog order).
		return available.Where(IsHardwareEncoder).Concat(available.Where(e => !IsHardwareEncoder(e))).ToArray();
	}

	public static bool IsHardwareEncoder(string encoder) => !encoder.StartsWith("lib");

	public static string[] GetPresetsForEncoder(string encoder) => encoder switch {
		"libvpx-vp9" or "libaom-av1" => [], // preset is not a meaningful option for these
		"h264_videotoolbox" or "hevc_videotoolbox" => [],
		_ when encoder.StartsWith("libx26") => CpuPresets,
		_ => SimplePresets
	};

	/// <summary>Valid range and recommended value for the quality knob of an encoder.</summary>
	public static (int Min, int Max) GetQualityRange(string encoder) =>
		encoder is "libvpx-vp9" or "libaom-av1" or "libsvtav1" ? (0, 63) : (0, 51);

	public static string GetDefaultExtension(string encoder) {
		if (encoder.Contains("264") || encoder.Contains("265") || encoder.Contains("hevc"))
			return ".mp4";
		if (encoder.Contains("vp9"))
			return ".webm";
		if (encoder.Contains("av1"))
			return ".mkv";
		return ".mp4";
	}

	public static string BuildCodecArgument(string encoder, string preset, int quality) {
		StringBuilder args = new(128);
		args.Append($"-c:v {encoder} ");

		switch (encoder) {
			case "libx264":
				args.Append($"-preset {preset} -crf {quality} -c:a copy");
				break;
			case "libx265":
				args.Append($"-preset {preset} -crf {quality} -tag:v hvc1 -c:a copy");
				break;
			case "libvpx-vp9":
				args.Append($"-b:v 0 -crf {quality} -row-mt 1 -c:a libopus");
				break;
			case "libaom-av1":
				args.Append($"-b:v 0 -crf {quality} -cpu-used 4 -row-mt 1 -tile-columns 2 -tile-rows 2 -c:a libopus");
				break;
			case "libsvtav1": {
				int svtPreset = preset switch { "fast" => 10, "slow" => 4, _ => 6 };
				args.Append($"-crf {quality} -preset {svtPreset} -c:a libopus");
				break;
			}
			case var e when e.EndsWith("_nvenc"):
				args.Append($"-preset {preset} -rc vbr -cq {quality} -b:v 0 -c:a copy");
				if (e.StartsWith("hevc")) args.Append(" -tag:v hvc1");
				break;
			case var e when e.EndsWith("_qsv"):
				args.Append($"-preset {preset} -global_quality {quality}");
				args.Append(e.StartsWith("vp9") ? " -c:a libopus" : " -c:a copy");
				if (e.StartsWith("hevc")) args.Append(" -tag:v hvc1");
				break;
			case var e when e.EndsWith("_amf"): {
				string amfQuality = preset switch { "fast" => "speed", "slow" => "quality", _ => "balanced" };
				args.Append($"-quality {amfQuality} -rc cqp -qp_i {quality} -qp_p {quality} -c:a copy");
				if (e.StartsWith("hevc")) args.Append(" -tag:v hvc1");
				break;
			}
			case var e when e.EndsWith("_videotoolbox"): {
				// -q:v is 1-100 where higher is better; invert the CRF-style scale.
				int q = Math.Clamp(100 - quality * 2, 1, 100);
				args.Append($"-q:v {q} -c:a copy");
				if (e.StartsWith("hevc")) args.Append(" -tag:v hvc1");
				break;
			}
			default:
				args.Append("-c:a copy");
				break;
		}

		return args.ToString();
	}
}
