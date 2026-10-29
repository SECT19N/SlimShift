using Spectre.Console;

namespace SlimShift.Cli;

/// <summary>The interactive menu, shown when SlimShift runs with no arguments.</summary>
public static class Menu {
    const string ChangeCodec = "Change video encoder/codec";
    const string Downscale = "Downscale video resolution";
    const string Upscale = "Upscale video resolution";
    const string Framerate = "Change video framerate";
    const string ListEncoders = "Show available encoders";
    const string Exit = "Exit";

    public static async Task<int> RunAsync() {
        AnsiConsole.Write(new FigletText("SlimShift").Color(Color.FromHex("#b00b69")));
        AnsiConsole.MarkupLine("[dim]Cross-platform video converter[/]\n");

        Session? session = await Session.CreateAsync();
        if (session == null) return 1;

        while (true) {
            string choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("\nWhat would you like to do?")
                    .AddChoices(ChangeCodec, Downscale, Upscale, Framerate, ListEncoders, Exit));

            if (choice == Exit) return 0;

            try {
                await (choice switch {
                    ChangeCodec => RunChangeCodec(session),
                    Downscale => RunScale(session, upscale: false),
                    Upscale => RunScale(session, upscale: true),
                    Framerate => RunFramerate(session),
                    _ => Task.Run(() => ListEncodersCommand.PrintEncoders(session))
                });
            } catch (Exception ex) {
                AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(ex.Message)}[/]");
            }
        }
    }

    static async Task<(string Path, VideoInfo Info)?> AskForVideo(Session session) {
        while (true) {
            string path = AnsiConsole.Prompt(
                new TextPrompt<string>("Path to your [green]video file[/] (blank to go back):").AllowEmpty())
                .Trim().Trim('"');
            if (path.Length == 0) return null;

            if (!File.Exists(path)) {
                AnsiConsole.MarkupLine("[red]✗ File not found.[/]");
                continue;
            }

            try {
                VideoInfo info = await VideoProbe.ProbeAsync(session.Tools, path);
                string duration = info.Duration is { } d ? $", {d:hh\\:mm\\:ss}" : "";
                AnsiConsole.MarkupLine(
                    $"[dim]{info.Width}x{info.Height} @ {info.Fps:0.##} fps, {Markup.Escape(info.VideoCodecName)}{duration}[/]");
                return (path, info);
            } catch (Exception ex) {
                AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(ex.Message)}[/]");
            }
        }
    }

    static async Task RunChangeCodec(Session session) {
        if ((await AskForVideo(session)) is not var (input, info)) return;

        VideoCodec codec = AnsiConsole.Prompt(
            new SelectionPrompt<VideoCodec>()
                .Title("Choose a [yellow]codec[/]:")
                .UseConverter(c => c.DisplayName())
                .AddChoices(session.AvailableCodecs));

        // Only encoders that work on this machine are offered, best first.
        IReadOnlyList<EncoderInfo> ranked = EncoderRecommender.Rank(session.Encoders, codec);
        Recommendation? rec = EncoderRecommender.Recommend(session.Encoders, codec);
        
        if (rec != null)
            AnsiConsole.MarkupLine($"[green]Recommended:[/] {rec.Encoder.Name} [dim]({Markup.Escape(rec.Reason)})[/]");

        EncoderInfo encoder = AnsiConsole.Prompt(
            new SelectionPrompt<EncoderInfo>()
                .Title($"Select [yellow]{codec.DisplayName()}[/] encoder:")
                .UseConverter(e => e == rec?.Encoder ? $"{e.Name} (recommended)" : e.Name)
                .AddChoices(ranked));

        IReadOnlyList<string> presets = EncoderProfiles.GetPresets(encoder);
        string? preset = presets.Count == 0 ? null : AnsiConsole.Prompt(
            new SelectionPrompt<string>().Title($"Select [yellow]{encoder.Name}[/] preset:").AddChoices(presets));

        QualityRange range = EncoderProfiles.GetQualityRange(encoder);
        int quality = AnsiConsole.Prompt(
            new TextPrompt<int>($"Quality [yellow]{range.Min}-{range.Max}[/] (lower is better):")
                .DefaultValue(range.Default)
                .Validate(v => v >= range.Min && v <= range.Max
                    ? ValidationResult.Success()
                    : ValidationResult.Error($"Must be between {range.Min} and {range.Max}")));

        EncodeRequest request = new(input, EncoderName: encoder.Name, Quality: quality, Preset: preset);
        await Execute(session, request, info);
    }

    static async Task RunScale(Session session, bool upscale) {
        if ((await AskForVideo(session)) is not var (input, info)) return;

        int[] standard = [2160, 1440, 1080, 720, 480, 360];
        int[] heights = standard.Where(h => upscale ? h > info.Height : h < info.Height).ToArray();
        const string custom = "Custom height";

        List<string> choices = heights.Select(h => $"{h}p").Append(custom).ToList();
        string pick = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"Target [yellow]{(upscale ? "upscaled" : "downscaled")}[/] resolution (source is {info.Height}p):")
                .AddChoices(choices));

        int height = pick == custom
            ? AnsiConsole.Prompt(new TextPrompt<int>("Height in pixels (even number):")
                .Validate(v => v > 0 && v % 2 == 0 && (upscale ? v > info.Height : v < info.Height)
                    ? ValidationResult.Success()
                    : ValidationResult.Error($"Must be an even number {(upscale ? "above" : "below")} {info.Height}")))
            : int.Parse(pick.TrimEnd('p'));

        if (upscale)
            AnsiConsole.MarkupLine("[dim]Note: upscaling enlarges the picture but cannot add detail.[/]");

        await Execute(session, new EncodeRequest(input, Height: height), info);
    }

    static async Task RunFramerate(Session session) {
        if ((await AskForVideo(session)) is not var (input, info)) return;

        double[] rates = [24, 25, 30, 50, 60, 120];
        double fps = AnsiConsole.Prompt(
            new SelectionPrompt<double>()
                .Title($"Target [yellow]framerate[/] (source is {info.Fps:0.##} fps):")
                .UseConverter(r => $"{r:0.##} fps")
                .AddChoices(rates.Where(r => Math.Abs(r - info.Fps) > 0.01)));

        bool interpolate = fps > info.Fps && AnsiConsole.Confirm(
            "Synthesise new frames with motion interpolation? (much slower; otherwise frames are duplicated)", false);

        await Execute(session, new EncodeRequest(input, Fps: fps, Interpolate: interpolate), info);
    }

    static async Task Execute(Session session, EncodeRequest request, VideoInfo info) {
        // Resolve the output path up front so an existing file can be confirmed before encoding starts.
        VideoCodec codec = request.EncoderName != null
            ? EncoderCatalog.All.First(e => e.Name == request.EncoderName).Codec
            : request.Codec ?? info.Codec ?? VideoCodec.H264;
        string output = request.Output ?? JobPlanner.DefaultOutputPath(request.Input, codec);

        bool overwrite = true;
        
        if (File.Exists(output))
            overwrite = AnsiConsole.Confirm($"{Markup.Escape(Path.GetFileName(output))} exists. Overwrite?", false);
        if (!overwrite) return;

        await EncodeRunner.ExecuteAsync(session, request with { Output = output }, info, overwrite: true);
    }
}
