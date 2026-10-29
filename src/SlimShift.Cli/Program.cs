using Spectre.Console;
using Spectre.Console.Cli;

namespace SlimShift.Cli;

public static class Program {
	public static async Task<int> Main(string[] args) {
		if (args.Length == 0) {
			// The interactive menu returns in a later step; for now point at the commands.
			AnsiConsole.Write(new FigletText("SlimShift").Color(Color.FromHex("#b00b69")));
			AnsiConsole.MarkupLine("[dim]The interactive menu is being rebuilt. Available commands:[/]");
			args = ["--help"];
		}

		CommandApp app = new();
		app.Configure(config => {
			config.SetApplicationName("slimshift");
			config.AddCommand<ListEncodersCommand>("list-encoders")
				.WithDescription("Show the video encoders that work on this machine.");
		});

		return await app.RunAsync(args);
	}
}
