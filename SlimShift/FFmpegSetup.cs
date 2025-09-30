using Spectre.Console;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Xabe.FFmpeg;

namespace SlimShift;

public static class FFmpegSetup {
	private static readonly HttpClient httpClient = new();

	public static async Task SetupFFmpeg() {
		try {
			AnsiConsole.MarkupLine("[yellow]Checking FFmpeg installation...[/]");

			string appDir = AppContext.BaseDirectory;
			string ffmpegDir = Path.Combine(appDir, "ffmpeg");

			(string ffmpegExe, string ffprobeExe) = GetExecutableNames();
			string ffmpegPath = Path.Combine(ffmpegDir, ffmpegExe);
			string ffprobePath = Path.Combine(ffmpegDir, ffprobeExe);

			if (!File.Exists(ffmpegPath) || !File.Exists(ffprobePath)) {
				AnsiConsole.MarkupLine("[green]Downloading FFmpeg binaries...[/]");
				Directory.CreateDirectory(ffmpegDir);
				await DownloadFFmpegForPlatform(ffmpegDir);
				AnsiConsole.MarkupLine("[green]FFmpeg download completed![/]");
			} else {
				AnsiConsole.MarkupLine("[green]FFmpeg binaries found locally.[/]");
			}

			FFmpeg.SetExecutablesPath(ffmpegDir);
		} catch (Exception ex) {
			AnsiConsole.MarkupLine($"[red]Error setting up FFmpeg: {ex.Message}[/]");
			AnsiConsole.MarkupLine("[yellow]Press any key to continue...[/]");
			Console.ReadKey();
		} finally {
			httpClient.Dispose();
		}

		static (string ffmpeg, string ffprobe) GetExecutableNames() {
			return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
				? ("ffmpeg.exe", "ffprobe.exe")
				: ("ffmpeg", "ffprobe");
		}

		static async Task DownloadFFmpegForPlatform(string ffmpegDir) {
			string downloadUrl = GetFFmpegDownloadUrl();
			string fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
			string downloadPath = Path.Combine(ffmpegDir, fileName);

			await DownloadWithProgress(downloadUrl, downloadPath);
			await ExtractFFmpeg(downloadPath, ffmpegDir);

			File.Delete(downloadPath); // Clean up archive
		}

		static string GetFFmpegDownloadUrl() {
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
				return "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";
			} else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
				return RuntimeInformation.OSArchitecture == Architecture.X64
					? "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-linux64-gpl.tar.xz"
					: "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-linuxarm64-gpl.tar.xz";
			} else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
				return RuntimeInformation.OSArchitecture == Architecture.Arm64
					? "https://evermeet.cx/ffmpeg/getrelease/zip" // FFmpeg for Apple Silicon
					: "https://evermeet.cx/ffmpeg/getrelease/zip"; // FFmpeg for Intel Mac
			}

			throw new PlatformNotSupportedException($"Platform {RuntimeInformation.OSDescription} is not supported.");
		}

		static async Task DownloadWithProgress(string url, string filePath) {
			await AnsiConsole.Progress().StartAsync(async ctx => {
				ProgressTask downloadTask = ctx.AddTask("Downloading FFmpeg", maxValue: 100);

				using HttpResponseMessage response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
				response.EnsureSuccessStatusCode();

				long totalBytes = response.Content.Headers.ContentLength ?? 0;

				await using Stream contentStream = await response.Content.ReadAsStreamAsync();
				await using FileStream fileStream = new(filePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920);

				byte[] buffer = new byte[81920]; // 80KB buffer for better I/O performance
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
				// For Linux tar.xz files, we need to use external tar command or a library
				await ExtractTarXz(archivePath, extractDir);
			}

			// Move binaries from extracted folder to ffmpeg directory
			string[] extractedDirs = Directory.GetDirectories(extractDir, "ffmpeg-*");
			if (extractedDirs.Length > 0) {
				string binDir = Path.Combine(extractedDirs[0], "bin");
				if (Directory.Exists(binDir)) {
					(string ffmpegExe, string ffprobeExe) = GetExecutableNames();

					foreach (string file in Directory.GetFiles(binDir)) {
						string fileName = Path.GetFileName(file);
						if (fileName == ffmpegExe || fileName == ffprobeExe) {
							string destPath = Path.Combine(extractDir, fileName);
							File.Move(file, destPath, overwrite: true);

							// Set execute permissions on Unix systems
							if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
								SetExecutePermission(destPath);
							}
						}
					}
				}
				Directory.Delete(extractedDirs[0], recursive: true);
			}
		}

		static async Task ExtractTarXz(string archivePath, string extractDir) {
			// Simple tar.xz extraction using system tar command
			ProcessStartInfo processStartInfo = new() {
				FileName = "tar",
				Arguments = $"-xf \"{archivePath}\" -C \"{extractDir}\"",
				UseShellExecute = false,
				CreateNoWindow = true
			};

			using Process? process = Process.Start(processStartInfo);
			if (process != null) {
				await process.WaitForExitAsync();
			}
		}

		static void SetExecutePermission(string filePath) {
			// Use chmod to set execute permissions on Unix systems
			ProcessStartInfo processStartInfo = new() {
				FileName = "chmod",
				Arguments = $"+x \"{filePath}\"",
				UseShellExecute = false,
				CreateNoWindow = true
			};

			using Process? process = Process.Start(processStartInfo);
			process?.WaitForExit();
		}
	}
}