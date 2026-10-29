using Spectre.Console;
using Spectre.Console.Cli;

namespace SlimShift.Cli;

public sealed class ListEncodersCommand : AsyncCommand {
	public override async Task<int> ExecuteAsync(CommandContext context) {
		CancellationToken cancellationToken = CancellationToken.None;

		FFmpegTools tools;
		try {
			tools = await AnsiConsole.Progress().StartAsync(async ctx => {
				ProgressTask task = ctx.AddTask("Locating FFmpeg", maxValue: 100);
				Progress<double> progress = new(value => task.Value = value);
				FFmpegTools located = await FFmpegLocator.LocateAsync(progress, cancellationToken);
				task.Value = 100;
				return located;
			});
		} catch (Exception ex) {
			AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(ex.Message)}[/]");
			return 1;
		}

		AnsiConsole.MarkupLine($"[dim]FFmpeg ({tools.Source}): {Markup.Escape(tools.FFmpegPath)}[/]");

		IReadOnlyList<EncoderInfo> available;
		try {
			available = await AnsiConsole.Status().StartAsync("Testing encoders...",
				_ => EncoderDetector.DetectAsync(tools, cancellationToken));
		} catch (Exception ex) {
			AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(ex.Message)}[/]");
			return 1;
		}

		Table table = new Table().AddColumn("Codec").AddColumn("Encoder").AddColumn("Type");
		foreach (EncoderInfo encoder in available) {
			string type = encoder.IsHardware ? $"hardware ({encoder.Vendor})" : "software";
			table.AddRow(encoder.Codec.DisplayName(), Markup.Escape(encoder.Name), type);
		}
		AnsiConsole.Write(table);

		string[] missing = Enum.GetValues<VideoCodec>()
			.Where(codec => available.All(e => e.Codec != codec))
			.Select(codec => codec.DisplayName()).ToArray();
		if (missing.Length > 0)
			AnsiConsole.MarkupLine($"[yellow]No working encoder for: {string.Join(", ", missing)}[/]");

		return 0;
	}
}
