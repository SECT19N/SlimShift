using Spectre.Console;
using Spectre.Console.Cli;

namespace SlimShift.Cli;

public sealed class ListEncodersCommand : AsyncCommand {
    public override async Task<int> ExecuteAsync(CommandContext context) {
        Session? session = await Session.CreateAsync();
        if (session == null) return 1;

        AnsiConsole.MarkupLine($"[dim]FFmpeg ({session.Tools.Source}): {Markup.Escape(session.Tools.FFmpegPath)}[/]");
        PrintEncoders(session);
        return 0;
    }

    public static void PrintEncoders(Session session) {
        Table table = new Table().AddColumn("Codec").AddColumn("Encoder").AddColumn("Type").AddColumn("");
        
        foreach (VideoCodec codec in session.AvailableCodecs) {
            EncoderInfo? best = EncoderRecommender.Recommend(session.Encoders, codec)?.Encoder;
            
            foreach (EncoderInfo encoder in EncoderRecommender.Rank(session.Encoders, codec)) {
                string type = encoder.IsHardware ? $"hardware ({encoder.Vendor})" : "software";
                table.AddRow(codec.DisplayName(), Markup.Escape(encoder.Name), type,
                    encoder == best ? "[green]recommended[/]" : "");
            }
        }

        AnsiConsole.Write(table);

        string[] missing = [.. Enum.GetValues<VideoCodec>()
            .Where(c => session.Encoders.All(e => e.Codec != c))
            .Select(c => c.DisplayName())];
        if (missing.Length > 0)
            AnsiConsole.MarkupLine($"[yellow]No working encoder for: {string.Join(", ", missing)}[/]");
    }
}
