using Spectre.Console.Cli;

namespace SlimShift.Cli;

public static class Program {
	public static async Task<int> Main(string[] args) {
		// No arguments: interactive menu. Otherwise: command line.
		if (args.Length == 0) return await Menu.RunAsync();

		CommandApp app = new();
		app.Configure(config => {
			config.SetApplicationName("slimshift");
			config.AddCommand<ListEncodersCommand>("list-encoders")
				.WithDescription("Show the video encoders that work on this machine.");
			config.AddCommand<ConvertCommand>("convert")
				.WithDescription("Re-encode a video to another codec/encoder.")
				.WithExample("convert", "movie.mkv", "--codec", "av1")
				.WithExample("convert", "movie.mp4", "--encoder", "libx265", "-q", "26");
			config.AddCommand<ScaleCommand>("scale")
				.WithDescription("Change the resolution (down or up).")
				.WithExample("scale", "movie.mp4", "--height", "720");
			config.AddCommand<FpsCommand>("fps")
				.WithDescription("Change the framerate.")
				.WithExample("fps", "movie.mp4", "--fps", "30");
		});

		return await app.RunAsync(args);
	}
}
