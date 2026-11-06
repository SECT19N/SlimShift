namespace SlimShift;

/// <summary>Works out which catalog encoders really work on this machine.</summary>
public static class EncoderDetector {
    static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// An encoder is returned only if the ffmpeg build lists it AND a real test encode succeeds,
    /// so hardware encoders without a matching GPU or driver are filtered out.
    /// </summary>
    public static async Task<IReadOnlyList<EncoderInfo>> DetectAsync(
        FFmpegTools tools, CancellationToken cancellationToken = default) {
        ProcessResult listing = await FFmpegProcess.RunAsync(
            tools.FFmpegPath, ["-hide_banner", "-encoders"], ProbeTimeout, cancellationToken);
        if (!listing.Succeeded)
            throw new InvalidOperationException($"ffmpeg -encoders failed: {listing.StdErr.Trim()}");

        HashSet<string> compiled = ParseEncoderNames(listing.StdOut);
        EncoderInfo[] candidates = EncoderCatalog.All.Where(e => compiled.Contains(e.Name)).ToArray();

        // A few at a time: hardware probes can contend for the same GPU.
        using SemaphoreSlim gate = new(4);
        bool[] works = await Task.WhenAll(candidates.Select(async encoder => {
            await gate.WaitAsync(cancellationToken);
            try {
                return await ProbeAsync(tools, encoder.Name, cancellationToken);
            } finally {
                gate.Release();
            }
        }));

        return candidates.Where((_, i) => works[i]).ToArray();
    }

    /// <summary>Extracts video encoder names from <c>ffmpeg -encoders</c> output.</summary>
    public static HashSet<string> ParseEncoderNames(string output) {
        HashSet<string> names = [];
        foreach (string line in output.Split('\n')) {
            // Lines look like " V....D libx264   libx264 H.264 / AVC ..."
            string[] parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[0].Length == 6 && parts[0][0] == 'V')
                names.Add(parts[1]);
        }
        return names;
    }

    static async Task<bool> ProbeAsync(FFmpegTools tools, string encoder, CancellationToken cancellationToken) {
        // 256x256: hardware encoders reject tiny frames.
        ProcessResult result = await FFmpegProcess.RunAsync(tools.FFmpegPath, [
            "-hide_banner", "-v", "error",
            "-f", "lavfi", "-i", "color=black:s=256x256:d=0.1",
            "-frames:v", "1", "-c:v", encoder, "-f", "null", "-"
        ], ProbeTimeout, cancellationToken);
        return result.Succeeded;
    }
}
