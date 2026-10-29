using Spectre.Console;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Xabe.FFmpeg;

namespace SlimShift;

public static class FFmpegSetup {
	/// <summary>Full path of the ffmpeg executable in use. Set by <see cref="SetupFFmpeg"/>.</summary>
	public static string FFmpegPath { get; private set; } = "ffmpeg";

	static (string ffmpeg, string ffprobe) GetExecutableNames() =>
		RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
			? ("ffmpeg.exe", "ffprobe.exe")
			: ("ffmpeg", "ffprobe");

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

	/// <summary>
	/// Locates ffmpeg/ffprobe (PATH first, then a per-user cache, then download) and configures Xabe.
	/// Throws if no usable ffmpeg can be found or installed.
	/// </summary>
	public static async Task SetupFFmpeg() {
		(string ffmpegExe, string ffprobeExe) = GetExecutableNames();

		// 1. A system install (package manager builds usually have the best hardware support).
		string? systemFFmpeg = FindOnPath(ffmpegExe);
		string? systemFFprobe = FindOnPath(ffprobeExe);
		if (systemFFmpeg != null && systemFFprobe != null) {
			AnsiConsole.MarkupLine($"[green]Using system FFmpeg:[/] {Markup.Escape(systemFFmpeg)}");
			Use(Path.GetDirectoryName(systemFFmpeg)!, systemFFmpeg);
			return;
		}

		// 2. A previously downloaded copy in a user-writable location.
		string ffmpegDir = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SlimShift", "ffmpeg");
		string ffmpegPath = Path.Combine(ffmpegDir, ffmpegExe);
		string ffprobePath = Path.Combine(ffmpegDir, ffprobeExe);

		if (!File.Exists(ffmpegPath) || !File.Exists(ffprobePath)) {
			if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
				throw new PlatformNotSupportedException(
					"FFmpeg was not found. Install it with Homebrew: brew install ffmpeg");
			}

			AnsiConsole.MarkupLine("[green]Downloading FFmpeg binaries...[/]");
			Directory.CreateDirectory(ffmpegDir);
			await DownloadFFmpegForPlatform(ffmpegDir);
			AnsiConsole.MarkupLine("[green]FFmpeg download completed![/]");
		}

		Use(ffmpegDir, ffmpegPath);
	}

	static void Use(string directory, string ffmpegPath) {
		FFmpegPath = ffmpegPath;
		FFmpeg.SetExecutablesPath(directory);
	}

	static async Task DownloadFFmpegForPlatform(string ffmpegDir) {
		string downloadUrl = GetFFmpegDownloadUrl();
		string downloadPath = Path.Combine(ffmpegDir, Path.GetFileName(new Uri(downloadUrl).LocalPath));

		try {
			await DownloadWithProgress(downloadUrl, downloadPath);
			await ExtractFFmpeg(downloadPath, ffmpegDir);
		} finally {
			if (File.Exists(downloadPath)) File.Delete(downloadPath);
		}
	}

	static string GetFFmpegDownloadUrl() {
		const string baseUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/";

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			return RuntimeInformation.OSArchitecture == Architecture.Arm64
				? baseUrl + "ffmpeg-master-latest-winarm64-gpl.zip"
				: baseUrl + "ffmpeg-master-latest-win64-gpl.zip";
		}
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
			return RuntimeInformation.OSArchitecture == Architecture.Arm64
				? baseUrl + "ffmpeg-master-latest-linuxarm64-gpl.tar.xz"
				: baseUrl + "ffmpeg-master-latest-linux64-gpl.tar.xz";
		}

		throw new PlatformNotSupportedException($"Platform {RuntimeInformation.OSDescription} is not supported.");
	}

	static async Task DownloadWithProgress(string url, string filePath) {
		using HttpClient httpClient = new();

		await AnsiConsole.Progress().StartAsync(async ctx => {
			ProgressTask downloadTask = ctx.AddTask("Downloading FFmpeg", maxValue: 100);

			using HttpResponseMessage response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
			response.EnsureSuccessStatusCode();

			long totalBytes = response.Content.Headers.ContentLength ?? 0;

			await using Stream contentStream = await response.Content.ReadAsStreamAsync();
			await using FileStream fileStream = new(filePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920);

			byte[] buffer = new byte[81920];
			long totalRead = 0;
			int bytesRead;

			while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0) {
				await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
				totalRead += bytesRead;

				if (totalBytes > 0) {
					downloadTask.Value = (double)totalRead / totalBytes * 100;
				}
			}
		});
	}

	static async Task ExtractFFmpeg(string archivePath, string extractDir) {
		AnsiConsole.MarkupLine("[yellow]Extracting FFmpeg...[/]");

		if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) {
			ZipFile.ExtractToDirectory(archivePath, extractDir, overwriteFiles: true);
		} else if (archivePath.EndsWith(".tar.xz", StringComparison.OrdinalIgnoreCase)) {
			await RunTool("tar", $"-xf \"{archivePath}\" -C \"{extractDir}\"");
		} else {
			throw new InvalidOperationException($"Unknown archive format: {Path.GetFileName(archivePath)}");
		}

		// Move binaries from the extracted folder up into the ffmpeg directory
		string[] extractedDirs = Directory.GetDirectories(extractDir, "ffmpeg-*");
		if (extractedDirs.Length == 0) throw new InvalidOperationException("Unexpected FFmpeg archive layout.");

		string binDir = Path.Combine(extractedDirs[0], "bin");
		(string ffmpegExe, string ffprobeExe) = GetExecutableNames();

		foreach (string file in Directory.GetFiles(binDir)) {
			string fileName = Path.GetFileName(file);
			if (fileName != ffmpegExe && fileName != ffprobeExe) continue;

			string destPath = Path.Combine(extractDir, fileName);
			File.Move(file, destPath, overwrite: true);

			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
				File.SetUnixFileMode(destPath,
					UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
					UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
					UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
			}
		}

		Directory.Delete(extractedDirs[0], recursive: true);

		if (!File.Exists(Path.Combine(extractDir, ffmpegExe)) || !File.Exists(Path.Combine(extractDir, ffprobeExe)))
			throw new InvalidOperationException("FFmpeg archive did not contain ffmpeg and ffprobe.");
	}

	static async Task RunTool(string fileName, string arguments) {
		ProcessStartInfo info = new() {
			FileName = fileName,
			Arguments = arguments,
			UseShellExecute = false,
			CreateNoWindow = true
		};

		using Process process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {fileName}.");
		await process.WaitForExitAsync();
		if (process.ExitCode != 0)
			throw new InvalidOperationException($"{fileName} exited with code {process.ExitCode}.");
	}
}
