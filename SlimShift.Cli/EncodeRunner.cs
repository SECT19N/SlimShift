using Spectre.Console;

namespace SlimShift.Cli;

/// <summary>Probe, plan, run and report one encode. Shared by the commands and the menu.</summary>
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

    public static async Task<int> ExecuteAsync(Session session, EncodeRequest request, VideoInfo info, bool overwrite) {
        PlanResult plan = JobPlanner.Plan(request, info, session.Encoders);
        
        if (plan.Job is not { } job) {
            AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(plan.Error ?? "Could not plan the conversion.")}[/]");
            return 1;
        }

        if (File.Exists(job.OutputPath) && !overwrite) {
            AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(job.OutputPath)} already exists. Use --overwrite to replace it.[/]");
            return 1;
        }

        string how = plan.Recommendation is { } rec ? $" [dim]({Markup.Escape(rec.Reason)})[/]" : "";
        AnsiConsole.MarkupLine($"Encoder: [yellow]{job.Encoder.Name}[/]{how}");
        AnsiConsole.MarkupLine($"Output:  {Markup.Escape(job.OutputPath)}");

        using CancellationTokenSource cts = new();
        void onCancel(object? _, ConsoleCancelEventArgs e) { e.Cancel = true; cts.Cancel(); }
        Console.CancelKeyPress += onCancel;
        
        try {
            ConversionResult result = await AnsiConsole.Progress().StartAsync(async ctx => {
                ProgressTask task = ctx.AddTask("Encoding", maxValue: 100);
                Progress<double> progress = new(value => task.Value = value);
                return await ConversionRunner.RunAsync(session.Tools, job, info, progress, cts.Token);
            });

            if (result.Success) {
                AnsiConsole.MarkupLine($"[green]✓ Done:[/] {Markup.Escape(job.OutputPath)}");
                return 0;
            }

            AnsiConsole.MarkupLine(result.Cancelled
                ? "[yellow]Cancelled.[/]"
                : $"[red]✗ Conversion failed:[/]\n{Markup.Escape(result.Error ?? "unknown error")}");
            return result.Cancelled ? 130 : 1;
        } finally {
            Console.CancelKeyPress -= onCancel;
        }
    }
}
