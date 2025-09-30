using Spectre.Console;
using Xabe.FFmpeg;

namespace SlimShift;

public class Program {
	private static readonly Dictionary<string, string[]> EncoderCache = new() {
		["H.264"] = ["libx264", "h264_qsv", "h264_nvenc", "h264_amf"],
		["H.265"] = ["libx265", "hevc_qsv", "hevc_nvenc", "hevc_amf"],
		["VP9"] = ["libvpx-vp9", "vp9_qsv", "vp9_nvenc", "vp9_amf"],
		["AV1"] = ["libaom-av1", "av1_qsv", "av1_nvenc", "av1_amf"]
	};

	private static readonly Dictionary<string, byte> DefaultCrfValues = new() {
		["H.264"] = 23,
		["H.265"] = 28,
		["VP9"] = 30,
		["AV1"] = 30
	};

	static async Task Main(string[] args) {
		// Setup FFmpeg once at startup
		try {
			await FFmpegSetup.SetupFFmpeg();
		} catch (Exception ex) {
			AnsiConsole.MarkupLine($"[red]Failed to setup FFmpeg: {ex.Message}[/]");
			AnsiConsole.MarkupLine("[yellow]Press any key to exit...[/]");
			Console.ReadKey();
			return;
		}

		// Main loop
		while (true) {
			try {
				AnsiConsole.Clear();
				AnsiConsole.Write(new FigletText("SlimShift").Color(Color.Yellow));
				AnsiConsole.MarkupLine("[dim]Cross-platform video converter[/]\n");

				// Run conversion workflow
				bool success = await RunConversionWorkflow();

				if (success) {
					AnsiConsole.MarkupLine("\n[green]✓ Conversion finished successfully![/]");
				}
			} catch (Exception ex) {
				AnsiConsole.MarkupLine($"\n[red]✗ Error: {ex.Message}[/]");
			}

			// Ask user what to do next
			AnsiConsole.WriteLine();
			string choice = AnsiConsole.Prompt(
				new SelectionPrompt<string>()
					.Title("What would you like to do?")
					.AddChoices("Convert another video", "Exit"));

			if (choice == "Exit") {
				AnsiConsole.MarkupLine("[yellow]Thanks for using SlimShift! Goodbye![/]");
				break;
			}
		}
	}

	static async Task<bool> RunConversionWorkflow() {
		string inputFile = GetValidatedInputFile();

		string codecType = AnsiConsole.Prompt(
			new SelectionPrompt<string>()
				.Title("Choose an [yellow]codec type[/]:")
				.AddChoices(EncoderCache.Keys));

		string[] availableEncoders = await EncoderUtils.GetAvailableEncodersForCodec(codecType, EncoderCache);

		// Check if any encoders are available
		if (availableEncoders.Length == 0) {
			AnsiConsole.MarkupLine($"[red]✗ No available encoders found for {codecType}[/]");
			AnsiConsole.MarkupLine("[yellow]This could mean:[/]");
			AnsiConsole.MarkupLine("  • FFmpeg doesn't have {0} support compiled in", codecType);
			AnsiConsole.MarkupLine("  • Hardware encoders require specific GPU drivers");
			AnsiConsole.MarkupLine("  • Software encoders may be missing from your FFmpeg build");
			return false;
		}

		string encoder = AnsiConsole.Prompt(
			new SelectionPrompt<string>()
				.Title($"Select [yellow]{codecType}[/] encoder:")
				.AddChoices(availableEncoders));

		string[] presetChoices = EncoderUtils.GetPresetsForEncoder(encoder);
		string preset = AnsiConsole.Prompt(
			new SelectionPrompt<string>()
				.Title($"Select [yellow]{encoder}[/] preset:")
				.AddChoices(presetChoices));

		byte defaultCRF = DefaultCrfValues.GetValueOrDefault(codecType, (byte)23);
		int crf = AnsiConsole.Prompt(
			new TextPrompt<int>($"Enter [yellow]CRF[/] value (recommended {defaultCRF}):")
				.Validate(value => value is >= 0 and <= 51 ? ValidationResult.Success() :
					ValidationResult.Error("CRF must be between 0 and 51")));

		string outputFile = GetValidatedOutputFile(encoder);
		string codecArgs = EncoderUtils.BuildCodecArgument(encoder, preset, crf);

		IConversion conversion = FFmpeg.Conversions.New()
			.AddParameter($"-i \"{inputFile}\"")
			.SetOutput(outputFile)
			.SetOverwriteOutput(true)
			.AddParameter(codecArgs)
			.UseMultiThread(true);

		// Attempt conversion with detailed error handling
		try {
			await ShowProgressAndConvert(conversion);
			AnsiConsole.MarkupLine($"[blue]Output file: {outputFile}[/]");
			return true;
		} catch (Exception ex) {
			AnsiConsole.MarkupLine($"[red]✗ Conversion failed: {ex.Message}[/]");

			// Provide helpful error messages
			if (ex.Message.Contains("Unknown encoder") || ex.Message.Contains("Encoder not found")) {
				AnsiConsole.MarkupLine($"[yellow]The encoder '{encoder}' is not available on your system.[/]");
				AnsiConsole.MarkupLine("[dim]Try selecting a different encoder or install required drivers.[/]");
			} else if (ex.Message.Contains("Invalid") || ex.Message.Contains("failed")) {
				AnsiConsole.MarkupLine("[yellow]Check that your input file is a valid video file.[/]");
			}

			return false;
		}
	}

	static string GetValidatedInputFile() {
		while (true) {
			string inputFile = AnsiConsole.Ask<string>("Enter the [green]path[/] to your video file:").Trim('"');
			if (File.Exists(inputFile)) return inputFile;
			AnsiConsole.MarkupLine("[red]✗ File not found. Please try again.[/]");
		}
	}

	static string GetValidatedOutputFile(string encoder) {
		// Get user's Videos folder (works cross-platform)
		string videosFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

		// Fallback to Documents if Videos folder doesn't exist
		if (string.IsNullOrEmpty(videosFolder) || !Directory.Exists(videosFolder)) {
			videosFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
		}

		// Create SlimShift subfolder
		string outputFolder = Path.Combine(videosFolder, "SlimShift");
		Directory.CreateDirectory(outputFolder);

		AnsiConsole.MarkupLine($"[dim]Output location: {outputFolder}[/]");

		string suggestedExt = EncoderUtils.GetDefaultExtension(encoder);

		while (true) {
			string outputFileName = AnsiConsole.Ask<string>(
				$"Enter [green]output file name[/] (without extension, will add {suggestedExt}):");
			string outputFile = Path.Combine(outputFolder, outputFileName + suggestedExt);

			if (!File.Exists(outputFile)) return outputFile;

			if (AnsiConsole.Confirm($"File {outputFileName}{suggestedExt} exists. Overwrite?"))
				return outputFile;
		}
	}

	static async Task ShowProgressAndConvert(IConversion conversion) {
		await AnsiConsole.Progress().StartAsync(async ctx => {
			ProgressTask task = ctx.AddTask("Encoding...", maxValue: 100);
			conversion.OnProgress += (_, e) => task.Value = e.Percent;
			await conversion.Start();
		});
	}
}