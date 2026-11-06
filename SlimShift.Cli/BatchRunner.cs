using Spectre.Console;

namespace SlimShift.Cli;

/// <summary>Converts every video in a folder with the same settings.</summary>
public static class BatchRunner {
    /// <summary>
    /// Outputs go to <c>template.OutputDirectory</c>, or a "converted" folder inside the input folder.
    /// The template's Input is ignored; each file replaces it.
    /// </summary>
    public static async Task<int> RunAsync(Session session, string folder, EncodeRequest template, bool recursive, bool overwrite) {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        string outputRoot = Path.GetFullPath(template.OutputDirectory ?? Path.Combine(root, "converted"));

        IReadOnlyList<string> files = BatchDiscovery.FindVideos(root, recursive, outputRoot);
        if (files.Count == 0) {
            AnsiConsole.MarkupLine($"[red]✗ No video files found in {Markup.Escape(root)}{(recursive ? "" : " (use --recursive to include subfolders)")}.[/]");
            return 1;
        }

        AnsiConsole.MarkupLine($"Found [yellow]{files.Count}[/] video(s) in {Markup.Escape(root)}");
        AnsiConsole.MarkupLine($"Outputs go to {Markup.Escape(outputRoot)}");

        List<(string Input, string? Error)> failed = [];
        int converted = 0, skipped = 0;
        long bytesBefore = 0, bytesAfter = 0;
        bool cancelled = false;

        using CancelScope cancel = new();

        for (int i = 0; i < files.Count && !cancelled; i++) {
            string file = files[i];
            AnsiConsole.MarkupLine($"\n[bold]File {i + 1}/{files.Count}:[/] {Markup.Escape(Path.GetRelativePath(root, file))}");

            string relativeDirectory = Path.GetRelativePath(root, Path.GetDirectoryName(file) ?? root);
            string outputDirectory = relativeDirectory == "." ? outputRoot : Path.Combine(outputRoot, relativeDirectory);
            EncodeRequest request = template with { Input = file, Output = null, OutputDirectory = outputDirectory };

            VideoInfo info;
            try {
                info = await VideoProbe.ProbeAsync(session.Tools, file, cancel.Token);
            } catch (OperationCanceledException) {
                cancelled = true;
                break;
            } catch (Exception ex) {
                AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(ex.Message)}[/]");
                failed.Add((file, ex.Message));
                continue;
            }

            EncodeReport report = await EncodeRunner.RunJobAsync(
                session, request, info, overwrite, skipExisting: true, quiet: i > 0, cancel.Token);

            switch (report.Outcome) {
                case EncodeOutcome.Converted:
                    converted++;
                    bytesBefore += new FileInfo(file).Length;
                    bytesAfter += new FileInfo(report.OutputPath!).Length;
                    break;
                case EncodeOutcome.Skipped:
                    skipped++;
                    break;
                case EncodeOutcome.Cancelled:
                    cancelled = true;
                    break;
                default:
                    failed.Add((file, report.Error));
                    break;
            }
        }

        PrintSummary(converted, skipped, failed, cancelled, files.Count, bytesBefore, bytesAfter);

        if (cancelled) return 130;
        return failed.Count > 0 ? 1 : 0;
    }

    static void PrintSummary(
        int converted, int skipped, List<(string Input, string? Error)> failed,
        bool cancelled, int total, long bytesBefore, long bytesAfter) {
        Table table = new Table().Border(TableBorder.Rounded).AddColumn("Result").AddColumn("Files");
        table.AddRow("[green]Converted[/]", converted.ToString());
        table.AddRow("[yellow]Skipped (already exist)[/]", skipped.ToString());
        table.AddRow("[red]Failed[/]", failed.Count.ToString());

        if (cancelled) table.AddRow("[dim]Not started (cancelled)[/]", (total - converted - skipped - failed.Count).ToString());

        AnsiConsole.WriteLine();
        AnsiConsole.Write(table);

        if (converted > 0)
            AnsiConsole.MarkupLine($"Size of converted files: {FormatSize(bytesBefore)} -> {FormatSize(bytesAfter)}");

        foreach ((string input, string? error) in failed) {
            string firstLine = (error ?? "unknown error").Split('\n')[0];
            AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(Path.GetFileName(input))}: {Markup.Escape(firstLine)}");
        }
    }

    static string FormatSize(long bytes) => bytes switch {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.##} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.##} MB",
        _ => $"{bytes / 1024.0:0.##} KB"
    };
}
