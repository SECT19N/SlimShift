using System.Diagnostics;

namespace SlimShift;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr, bool TimedOut) {
	public bool Succeeded => ExitCode == 0 && !TimedOut;
}

/// <summary>Runs ffmpeg/ffprobe (or any tool) and captures its output.</summary>
public static class FFmpegProcess {
	public static async Task<ProcessResult> RunAsync(
		string executable,
		IEnumerable<string> arguments,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default) {
		ProcessStartInfo info = new() {
			FileName = executable,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
		foreach (string argument in arguments) info.ArgumentList.Add(argument);

		using Process process = Process.Start(info)
			?? throw new InvalidOperationException($"Could not start {executable}.");

		// Read both streams concurrently so a full pipe buffer cannot block the process.
		Task<string> stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
		Task<string> stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);

		using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		if (timeout is { } limit) cts.CancelAfter(limit);

		try {
			await process.WaitForExitAsync(cts.Token);
		} catch (OperationCanceledException) {
			process.Kill(entireProcessTree: true);
			if (cancellationToken.IsCancellationRequested) throw;
			return new ProcessResult(-1, await stdout, await stderr, TimedOut: true);
		}

		return new ProcessResult(process.ExitCode, await stdout, await stderr, TimedOut: false);
	}
}
