using System.Globalization;

namespace SlimShift;

public sealed record ConversionResult(bool Success, string? Error, bool Cancelled = false);

/// <summary>Builds the ffmpeg command for a job and runs it, reporting progress as 0-100.</summary>
public static class ConversionRunner {
	/// <summary>All ffmpeg arguments for the job (everything after the executable name).</summary>
	public static List<string> BuildArguments(ConversionJob job, VideoInfo source) {
		List<string> args = ["-hide_banner", "-nostdin", "-y", "-i", job.InputPath, "-map", "0:v:0", "-map", "0:a?"];

		List<string> filters = [];
		if (job.TargetHeight is { } height) filters.Add($"scale=-2:{height}");
		if (job.TargetFps is { } fps) {
			string rate = fps.ToString("0.###", CultureInfo.InvariantCulture);
			filters.Add(job.Interpolate ? $"minterpolate=fps={rate}" : $"fps={rate}");
		}
		if (filters.Count > 0) args.AddRange(["-vf", string.Join(',', filters)]);

		args.AddRange(EncoderProfiles.BuildVideoArgs(job.Encoder, job.Preset, job.Quality));
		args.AddRange(EncoderProfiles.BuildAudioArgs(Path.GetExtension(job.OutputPath), source.AudioCodecs));
		args.AddRange(["-progress", "pipe:1", "-nostats", job.OutputPath]);
		return args;
	}

	/// <summary>Reads the encoded time (in seconds) from an <c>-progress</c> line, if it carries one.</summary>
	public static bool TryParseProgressSeconds(string line, out double seconds) {
		seconds = 0;
		// out_time_ms is (confusingly) in microseconds, same as out_time_us.
		foreach (string key in (string[])["out_time_us=", "out_time_ms="]) {
			if (line.StartsWith(key, StringComparison.Ordinal) &&
				long.TryParse(line.AsSpan(key.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out long micro) &&
				micro >= 0) {
				seconds = micro / 1_000_000.0;
				return true;
			}
		}
		return false;
	}

	public static async Task<ConversionResult> RunAsync(
		FFmpegTools tools,
		ConversionJob job,
		VideoInfo source,
		IProgress<double>? progress = null,
		CancellationToken cancellationToken = default) {
		string? outputDirectory = Path.GetDirectoryName(Path.GetFullPath(job.OutputPath));
		if (outputDirectory != null) Directory.CreateDirectory(outputDirectory);

		double totalSeconds = source.Duration?.TotalSeconds ?? 0;
		// Changing the framerate does not change the duration, so source duration stays valid.
		Action<string>? onLine = progress == null || totalSeconds <= 0 ? null : line => {
			if (TryParseProgressSeconds(line, out double done))
				progress.Report(Math.Clamp(done / totalSeconds * 100, 0, 100));
		};

		ProcessResult result;
		try {
			result = await FFmpegProcess.RunAsync(tools.FFmpegPath, BuildArguments(job, source),
				timeout: null, cancellationToken, onLine);
		} catch (OperationCanceledException) {
			TryDelete(job.OutputPath);
			return new ConversionResult(false, "Cancelled.", Cancelled: true);
		}

		if (!result.Succeeded) {
			TryDelete(job.OutputPath);
			return new ConversionResult(false, TailOf(result.StdErr));
		}

		progress?.Report(100);
		return new ConversionResult(true, null);
	}

	static string TailOf(string stderr) {
		string[] lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		return lines.Length == 0 ? "ffmpeg failed without any output." : string.Join('\n', lines.TakeLast(8));
	}

	static void TryDelete(string path) {
		try {
			if (File.Exists(path)) File.Delete(path);
		} catch (IOException) {
			// leave a partial file rather than fail the cleanup
		}
	}
}
