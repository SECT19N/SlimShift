using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace SlimShift.Cli;

public class PresetNameSettings : CommandSettings {
    [CommandArgument(0, "<NAME>")]
    [Description("Preset name: letters, digits, '.', '_' or '-'.")]
    public string Name { get; set; } = "";
}

public sealed class PresetSaveSettings : PresetNameSettings {
    [CommandOption("--codec <CODEC>")]
    [Description("h264, h265, vp9 or av1. Leave the encoder out to let SlimShift pick the best one on each machine.")]
    public string? Codec { get; set; }

    [CommandOption("--encoder <NAME>")]
    [Description("Pin a specific encoder, e.g. libx264. Fails on machines that don't have it.")]
    public string? Encoder { get; set; }

    [CommandOption("-q|--quality <VALUE>")]
    [Description("Quality (lower is better).")]
    public int? Quality { get; set; }

    [CommandOption("--preset <NAME>")]
    [Description("Encoder speed/efficiency preset, e.g. slow.")]
    public string? Speed { get; set; }

    [CommandOption("--prefer-size")]
    [Description("Prefer software encoders (smaller files).")]
    public bool PreferSize { get; set; }

    [CommandOption("--height <PIXELS>")]
    [Description("Resize to this height.")]
    public int? Height { get; set; }

    [CommandOption("--fps <RATE>")]
    [Description("Change the framerate.")]
    public double? Fps { get; set; }

    [CommandOption("--interpolate")]
    [Description("With --fps, synthesise new frames.")]
    public bool Interpolate { get; set; }

    [CommandOption("--overwrite")]
    [Description("Replace the preset if it already exists.")]
    public bool Overwrite { get; set; }

    public override ValidationResult Validate() {
        if (!EncodeSettings.TryParseCodec(Codec, out _))
            return ValidationResult.Error($"Unknown codec '{Codec}'. Use h264, h265, vp9 or av1.");
        if (!PresetStore.IsValidName(Name))
            return ValidationResult.Error($"'{Name}' is not a valid preset name. Use letters, digits, '.', '_' or '-'.");
        return ValidationResult.Success();
    }

    public EncodePreset ToPreset() {
        _ = EncodeSettings.TryParseCodec(Codec, out VideoCodec? codec);
        return new EncodePreset(codec, Encoder, Quality, Speed, PreferSize, Height, Fps, Interpolate);
    }
}

static class PresetCommandHelper {
    public static PresetStore Store => new(PresetStore.DefaultDirectory);

    public static int Guard(Func<int> action) {
        try {
            return action();
        } catch (PresetException ex) {
            AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }
}

public sealed class PresetListSettings : CommandSettings;

public sealed class PresetListCommand : Command<PresetListSettings> {
    public override int Execute(CommandContext context, PresetListSettings settings) =>
        PresetCommandHelper.Guard(() => {
            PresetStore store = PresetCommandHelper.Store;
            IReadOnlyList<string> names = store.List();

            if (names.Count == 0) {
                AnsiConsole.MarkupLine("[dim]No presets saved yet. Create one with: SlimShift presets save <name> --codec av1 --height 720[/]");
                return 0;
            }

            Table table = new Table().Border(TableBorder.Rounded).AddColumn("Preset").AddColumn("Settings");
            foreach (string name in names) {
                string summary;
                try {
                    summary = store.Load(name).Describe();
                } catch (PresetException ex) {
                    summary = $"damaged: {ex.Message}";
                }

                table.AddRow(Markup.Escape(name), Markup.Escape(summary));
            }

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"[dim]{Markup.Escape(store.Directory)}[/]");
            return 0;
        });
}

public sealed class PresetShowCommand : Command<PresetNameSettings> {
    public override int Execute(CommandContext context, PresetNameSettings settings) =>
        PresetCommandHelper.Guard(() => {
            EncodePreset preset = PresetCommandHelper.Store.Load(settings.Name);

            Table table = new Table().Border(TableBorder.Rounded).AddColumn("Setting").AddColumn("Value");
            if (preset.Codec != null) table.AddRow("Codec", preset.Codec.Value.DisplayName());
            if (preset.Encoder != null) table.AddRow("Encoder", Markup.Escape(preset.Encoder));
            if (preset.Quality != null) table.AddRow("Quality", preset.Quality.ToString()!);
            if (preset.Speed != null) table.AddRow("Speed preset", Markup.Escape(preset.Speed));
            if (preset.Height != null) table.AddRow("Height", $"{preset.Height}p");
            if (preset.Fps != null) table.AddRow("Framerate", $"{preset.Fps:0.##} fps");
            if (preset.PreferSize) table.AddRow("Prefer size", "yes");
            if (preset.Interpolate) table.AddRow("Interpolate", "yes");

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"[dim]Use it: SlimShift convert <file or folder> --use {Markup.Escape(settings.Name)}[/]");
            return 0;
        });
}

public sealed class PresetSaveCommand : Command<PresetSaveSettings> {
    public override int Execute(CommandContext context, PresetSaveSettings settings) =>
        PresetCommandHelper.Guard(() => {
            EncodePreset preset = settings.ToPreset();
            PresetCommandHelper.Store.Save(settings.Name, preset, settings.Overwrite);
            AnsiConsole.MarkupLine($"[green]✓ Saved preset[/] {Markup.Escape(settings.Name.ToLowerInvariant())}: {Markup.Escape(preset.Describe())}");
            return 0;
        });
}

public sealed class PresetDeleteCommand : Command<PresetNameSettings> {
    public override int Execute(CommandContext context, PresetNameSettings settings) =>
        PresetCommandHelper.Guard(() => {
            PresetCommandHelper.Store.Delete(settings.Name);
            AnsiConsole.MarkupLine($"[green]✓ Deleted preset[/] {Markup.Escape(settings.Name)}");
            return 0;
        });
}
