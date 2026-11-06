namespace SlimShift.Cli;

/// <summary>Turns Ctrl+C into a cancelled token for as long as it is alive, instead of killing the process.</summary>
public sealed class CancelScope : IDisposable {
    readonly CancellationTokenSource cts = new();

    public CancelScope() => Console.CancelKeyPress += OnCancel;

    public CancellationToken Token => cts.Token;

    void OnCancel(object? sender, ConsoleCancelEventArgs e) {
        e.Cancel = true;

        try {
            cts.Cancel();
        } catch (ObjectDisposedException) {
            // Ctrl+C arrived while the scope was closing; nothing is left to cancel.
        }
    }

    public void Dispose() {
        Console.CancelKeyPress -= OnCancel;
        cts.Dispose();
    }
}
