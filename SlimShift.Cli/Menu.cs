using Spectre.Console;

namespace SlimShift.Cli;

/// <summary>The interactive menu, shown when SlimShift runs with no arguments.</summary>
public static class Menu {
    const string ChangeCodec = "Change video encoder/codec";
    const string Downscale = "Downscale video resolution";
    const string Upscale = "Upscale video resolution";
    const string Framerate = "Change video framerate";
    const string UsePreset = "Convert a file or folder with a saved preset";
    const string DeletePreset = "Delete a saved preset";
    const string ListEncoders = "Show available encoders";
    const string Exit = "Exit";

    static readonly PresetStore Presets = new(PresetStore.DefaultDirectory);

    public static async Task<int> RunAsync() {
        AnsiConsole.Write(new FigletText("SlimShift").Color(Color.FromHex("#b00b69")));
        AnsiConsole.MarkupLine("[dim]Cross-platform video converter[/]\n");

        Session? session = await Session.CreateAsync();
        if (session == null) return 1;

        while (true) {
            string choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("\nWhat would you like to do?")
                    .AddChoices(ChangeCodec, Downscale, Upscale, Framerate, UsePreset, DeletePreset, ListEncoders, Exit));

            if (choice == Exit) return 0;

            try {
                await (choice switch {
                    ChangeCodec => RunChangeCodec(session),
                    Downscale => RunScale(session, upscale: false),
                    Upscale => RunScale(session, upscale: true),
                    Framerate => RunFramerate(session),
                    UsePreset => RunPreset(session),
                    DeletePreset => Task.Run(RunDeletePreset),
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

    static async Task RunPreset(Session session) {
        IReadOnlyList<string> names = Presets.List();
        if (names.Count == 0) {
            AnsiConsole.MarkupLine("[yellow]No presets saved yet.[/] Finish any conversion and you'll be offered to save its settings.");
            return;
        }

        string name = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Choose a [yellow]preset[/]:").AddChoices(names));
        EncodePreset preset = Presets.Load(name);
        AnsiConsole.MarkupLine($"[dim]{Markup.Escape(preset.Describe())}[/]");

        while (true) {
            string path = AnsiConsole.Prompt(
                new TextPrompt<string>("Path to a [green]video file or folder[/] (blank to go back):").AllowEmpty())
                .Trim().Trim('"');
            if (path.Length == 0) return;

            if (Directory.Exists(path)) {
                bool recursive = AnsiConsole.Confirm("Include videos in subfolders?", false);
                await BatchRunner.RunAsync(session, path, new EncodeRequest("").WithPreset(preset), recursive, overwrite: false);
                return;
            }

            if (!File.Exists(path)) {
                AnsiConsole.MarkupLine("[red]✗ Not found.[/]");
                continue;
            }

            VideoInfo info = await VideoProbe.ProbeAsync(session.Tools, path);
            await Execute(session, new EncodeRequest(path).WithPreset(preset), info, offerSave: false);
            return;
        }
    }

    static void RunDeletePreset() {
        IReadOnlyList<string> names = Presets.List();
        if (names.Count == 0) {
            AnsiConsole.MarkupLine("[yellow]No presets saved.[/]");
            return;
        }

        string name = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Delete which [yellow]preset[/]?").AddChoices(names));
        if (!AnsiConsole.Confirm($"Delete '{Markup.Escape(name)}'?", false)) return;

        Presets.Delete(name);
        AnsiConsole.MarkupLine($"[green]✓ Deleted preset[/] {Markup.Escape(name)}");
    }

    /// <summary>
    /// Keeps the codec but not the encoder when the encoder is simply the recommended one, so the preset
    /// still works on a machine with different hardware.
    /// </summary>
    static EncodePreset ToPreset(Session session, EncodeRequest request) {
        EncodePreset preset = new(request.Codec, request.EncoderName, request.Quality, request.Preset,
            request.PreferSize, request.Height, request.Fps, request.Interpolate);

        if (request.EncoderName == null) return preset;

        EncoderInfo encoder = EncoderCatalog.All.First(e => e.Name == request.EncoderName);
        bool isRecommended = EncoderRecommender.Recommend(session.Encoders, encoder.Codec, request.PreferSize)?.Encoder == encoder;
        return preset with { Codec = encoder.Codec, Encoder = isRecommended ? null : encoder.Name };
    }

    static void OfferToSavePreset(Session session, EncodeRequest request) {
        if (!AnsiConsole.Confirm("Save these settings as a preset?", false)) return;

        string name = AnsiConsole.Prompt(
            new TextPrompt<string>("Preset name:")
                .Validate(n => PresetStore.IsValidName(n)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("Use letters, digits, '.', '_' or '-' (start with a letter or digit)")));

        bool overwrite = Presets.Exists(name);
        if (overwrite && !AnsiConsole.Confirm($"'{Markup.Escape(name)}' already exists. Replace it?", false)) return;

        EncodePreset preset = ToPreset(session, request);
        Presets.Save(name, preset, overwrite);
        AnsiConsole.MarkupLine($"[green]✓ Saved preset[/] {Markup.Escape(name.ToLowerInvariant())}: {Markup.Escape(preset.Describe())}");
    }

    static async Task Execute(Session session, EncodeRequest request, VideoInfo info, bool offerSave = true) {
        // Resolve the output path up front so an existing file can be confirmed before encoding starts.
        // If planning fails, ExecuteAsync reports the planner's error.
        EncodeRequest original = request;

        if (JobPlanner.Plan(request, info, session.Encoders).Job is { } job) {
            if (File.Exists(job.OutputPath)
                && !AnsiConsole.Confirm($"{Markup.Escape(Path.GetFileName(job.OutputPath))} exists. Overwrite?", false))
                return;
            request = request with { Output = job.OutputPath };
        }

        int exitCode = await EncodeRunner.ExecuteAsync(session, request, info, overwrite: true);

        // Offer the original request (before the output path was filled in), so the preset holds settings only.
        if (exitCode == 0 && offerSave) OfferToSavePreset(session, original);
    }
}
