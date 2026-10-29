using Spectre.Console;

namespace SlimShift.Cli;

/// <summary>FFmpeg plus the encoders that work on this machine, found once per run.</summary>
public sealed record Session(FFmpegTools Tools, IReadOnlyList<EncoderInfo> Encoders) {
	/// <summary>Prints the problem and returns null if FFmpeg or detection fails.</summary>
	public static async Task<Session?> CreateAsync(CancellationToken cancellationToken = default) {
		try {
			FFmpegTools tools = await AnsiConsole.Progress().StartAsync(async ctx => {
				ProgressTask task = ctx.AddTask("Locating FFmpeg", maxValue: 100);
				Progress<double> progress = new(value => task.Value = value);
				FFmpegTools located = await FFmpegLocator.LocateAsync(progress, cancellationToken);
				task.Value = 100;
				return located;
			});

			IReadOnlyList<EncoderInfo> encoders = await AnsiConsole.Status().StartAsync("Testing encoders...",
				_ => EncoderDetector.DetectAsync(tools, cancellationToken));

			return new Session(tools, encoders);
		} catch (Exception ex) {
			AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(ex.Message)}[/]");
			return null;
		}
	}

	public VideoCodec[] AvailableCodecs =>
		Enum.GetValues<VideoCodec>().Where(c => Encoders.Any(e => e.Codec == c)).ToArray();
}
