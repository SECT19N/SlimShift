using Spectre.Console;

namespace SlimShift.Cli;

public enum EncodeOutcome { Converted, Skipped, Failed, Cancelled }

public sealed record EncodeReport(EncodeOutcome Outcome, string? OutputPath = null, string? Error = null);

/// <summary>Probe, plan, run and report one encode. Shared by the commands, the menu and the batch runner.</summary>
public static class EncodeRunner {
    public static async Task<int> RunAsync(Session session, EncodeRequest request, bool overwrite) {
        if (!File.Exists(request.Input)) {
            AnsiConsole.MarkupLine($"[red]✗ File not found: {Markup.Escape(request.Input)}[/]");
            return 1;
        }

        VideoInfo info;

        try {
            info = await VideoProbe.ProbeAsync(session.Tools, request.Input);
        } catch (Exception ex) {
            AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(ex.Message)}[/]");
            return 1;
        }

        return await ExecuteAsync(session, request, info, overwrite);
    }

    /// <summary>One file, with Ctrl+C handled. Returns a process exit code.</summary>
    public static async Task<int> ExecuteAsync(Session session, EncodeRequest request, VideoInfo info, bool overwrite) {
        using CancelScope cancel = new();
        EncodeReport report = await RunJobAsync(session, request, info, overwrite, skipExisting: false, quiet: false, cancel.Token);
        return ToExitCode(report.Outcome);
    }

    public static int ToExitCode(EncodeOutcome outcome) => outcome switch {
        EncodeOutcome.Converted or EncodeOutcome.Skipped => 0,
        EncodeOutcome.Cancelled => 130,
        _ => 1
    };

    /// <summary>
    /// Plans and runs one encode and prints the result. With <paramref name="skipExisting"/> an existing output is
    /// skipped instead of refused. With <paramref name="quiet"/> the encoder and output lines are left out.
    /// </summary>
    public static async Task<EncodeReport> RunJobAsync(
        Session session, EncodeRequest request, VideoInfo info,
        bool overwrite, bool skipExisting, bool quiet, CancellationToken cancellationToken) {
        PlanResult plan = JobPlanner.Plan(request, info, session.Encoders);

        if (plan.Job is not { } job) {
            string error = plan.Error ?? "Could not plan the conversion.";
            AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(error)}[/]");
            return new EncodeReport(EncodeOutcome.Failed, null, error);
        }

        if (File.Exists(job.OutputPath) && !overwrite) {
            if (skipExisting) {
                AnsiConsole.MarkupLine($"[yellow]↷ Skipped:[/] {Markup.Escape(job.OutputPath)} [dim]already exists[/]");
                return new EncodeReport(EncodeOutcome.Skipped, job.OutputPath);
            }

            string error = $"{job.OutputPath} already exists. Use --overwrite to replace it.";
            AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(error)}[/]");
            return new EncodeReport(EncodeOutcome.Failed, job.OutputPath, error);
        }

        if (!quiet) {
            string how = plan.Recommendation is { } rec ? $" [dim]({Markup.Escape(rec.Reason)})[/]" : "";
            AnsiConsole.MarkupLine($"Encoder: [yellow]{job.Encoder.Name}[/]{how}");
            AnsiConsole.MarkupLine($"Output:  {Markup.Escape(job.OutputPath)}");
        }

        ConversionResult result = await AnsiConsole.Progress().StartAsync(async ctx => {
            ProgressTask task = ctx.AddTask("Encoding", maxValue: 100);
            Progress<double> progress = new(value => task.Value = value);
            return await ConversionRunner.RunAsync(session.Tools, job, info, progress, cancellationToken);
        });

        if (result.Success) {
            AnsiConsole.MarkupLine($"[green]✓ Done:[/] {Markup.Escape(job.OutputPath)}");
            return new EncodeReport(EncodeOutcome.Converted, job.OutputPath);
        }

        if (result.Cancelled) {
            AnsiConsole.MarkupLine("[yellow]Cancelled.[/]");
            return new EncodeReport(EncodeOutcome.Cancelled, job.OutputPath);
        }

        string failure = result.Error ?? "unknown error";
        AnsiConsole.MarkupLine($"[red]✗ Conversion failed:[/]\n{Markup.Escape(failure)}");
        return new EncodeReport(EncodeOutcome.Failed, job.OutputPath, failure);
    }
}
