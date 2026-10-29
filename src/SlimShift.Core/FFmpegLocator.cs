using System.IO.Compression;
using System.Runtime.InteropServices;

namespace SlimShift;

public enum FFmpegSource { System, Cached, Downloaded }

public sealed record FFmpegTools(string FFmpegPath, string FFprobePath, FFmpegSource Source);

/// <summary>Finds ffmpeg/ffprobe: system PATH first, then a per-user cache, then a download.</summary>
public static class FFmpegLocator {
	static (string ffmpeg, string ffprobe) ExecutableNames() =>
		RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
			? ("ffmpeg.exe", "ffprobe.exe")
			: ("ffmpeg", "ffprobe");

	public static string CacheDirectory { get; } = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SlimShift", "ffmpeg");

	/// <summary>Throws if no usable ffmpeg can be found or installed.</summary>
	public static async Task<FFmpegTools> LocateAsync(
		IProgress<double>? downloadProgress = null,
		CancellationToken cancellationToken = default) {
		(string ffmpegExe, string ffprobeExe) = ExecutableNames();

		// Package-manager builds usually have the best hardware support, so prefer them.
		string? systemFFmpeg = FindOnPath(ffmpegExe);
		string? systemFFprobe = FindOnPath(ffprobeExe);
		if (systemFFmpeg != null && systemFFprobe != null)
			return new FFmpegTools(systemFFmpeg, systemFFprobe, FFmpegSource.System);

		string cachedFFmpeg = Path.Combine(CacheDirectory, ffmpegExe);
		string cachedFFprobe = Path.Combine(CacheDirectory, ffprobeExe);
		if (File.Exists(cachedFFmpeg) && File.Exists(cachedFFprobe))
			return new FFmpegTools(cachedFFmpeg, cachedFFprobe, FFmpegSource.Cached);

		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
			throw new PlatformNotSupportedException(
				"FFmpeg was not found. Install it with Homebrew: brew install ffmpeg");
		}

		Directory.CreateDirectory(CacheDirectory);
		await DownloadAsync(CacheDirectory, downloadProgress, cancellationToken);
		return new FFmpegTools(cachedFFmpeg, cachedFFprobe, FFmpegSource.Downloaded);
	}

	static string? FindOnPath(string fileName) {
		string[] dirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
			.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
		foreach (string dir in dirs) {
			try {
				string candidate = Path.Combine(dir.Trim('"'), fileName);
				if (File.Exists(candidate)) return candidate;
			} catch (ArgumentException) {
				// malformed PATH entry
			}
		}
		return null;
	}

	static string DownloadUrl() {
		const string baseUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/";
		bool arm = RuntimeInformation.OSArchitecture == Architecture.Arm64;

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			return baseUrl + (arm ? "ffmpeg-master-latest-winarm64-gpl.zip" : "ffmpeg-master-latest-win64-gpl.zip");
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			return baseUrl + (arm ? "ffmpeg-master-latest-linuxarm64-gpl.tar.xz" : "ffmpeg-master-latest-linux64-gpl.tar.xz");

		throw new PlatformNotSupportedException($"Platform {RuntimeInformation.OSDescription} is not supported.");
	}

	static async Task DownloadAsync(string directory, IProgress<double>? progress, CancellationToken cancellationToken) {
		string url = DownloadUrl();
		string archivePath = Path.Combine(directory, Path.GetFileName(new Uri(url).LocalPath));

		try {
			using HttpClient http = new();
			using HttpResponseMessage response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			response.EnsureSuccessStatusCode();

			long totalBytes = response.Content.Headers.ContentLength ?? 0;
			await using (Stream content = await response.Content.ReadAsStreamAsync(cancellationToken))
			await using (FileStream file = new(archivePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920)) {
				byte[] buffer = new byte[81920];
				long totalRead = 0;
				int read;
				while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0) {
					await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
					totalRead += read;
					if (totalBytes > 0) progress?.Report((double)totalRead / totalBytes * 100);
				}
			}

			await ExtractAsync(archivePath, directory, cancellationToken);
		} finally {
			if (File.Exists(archivePath)) File.Delete(archivePath);
		}
	}

	static async Task ExtractAsync(string archivePath, string directory, CancellationToken cancellationToken) {
		if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) {
			ZipFile.ExtractToDirectory(archivePath, directory, overwriteFiles: true);
		} else if (archivePath.EndsWith(".tar.xz", StringComparison.OrdinalIgnoreCase)) {
			ProcessResult result = await FFmpegProcess.RunAsync("tar", ["-xf", archivePath, "-C", directory],
				TimeSpan.FromMinutes(2), cancellationToken);
			if (!result.Succeeded) throw new InvalidOperationException($"tar failed: {result.StdErr.Trim()}");
		} else {
			throw new InvalidOperationException($"Unknown archive format: {Path.GetFileName(archivePath)}");
		}

		// The archive holds ffmpeg-*/bin/{ffmpeg,ffprobe}; move them up into the cache directory.
		string[] extracted = Directory.GetDirectories(directory, "ffmpeg-*");
		if (extracted.Length == 0) throw new InvalidOperationException("Unexpected FFmpeg archive layout.");

		(string ffmpegExe, string ffprobeExe) = ExecutableNames();
		foreach (string file in Directory.GetFiles(Path.Combine(extracted[0], "bin"))) {
			string name = Path.GetFileName(file);
			if (name != ffmpegExe && name != ffprobeExe) continue;

			string destination = Path.Combine(directory, name);
			File.Move(file, destination, overwrite: true);
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
				File.SetUnixFileMode(destination,
					UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
					UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
					UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
			}
		}
		Directory.Delete(extracted[0], recursive: true);

		if (!File.Exists(Path.Combine(directory, ffmpegExe)) || !File.Exists(Path.Combine(directory, ffprobeExe)))
			throw new InvalidOperationException("FFmpeg archive did not contain ffmpeg and ffprobe.");
	}
}
