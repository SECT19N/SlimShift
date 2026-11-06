namespace SlimShift;

/// <summary>Finds the video files in a folder for a batch conversion.</summary>
public static class BatchDiscovery {
    public static readonly IReadOnlySet<string> VideoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        ".mp4", ".mkv", ".mov", ".m4v", ".avi", ".webm", ".wmv", ".flv", ".ts", ".mts", ".m2ts"
    };

    /// <summary>Video files in the folder, sorted by path. Files under <paramref name="excludeDirectory"/> are skipped.</summary>
    public static IReadOnlyList<string> FindVideos(string folder, bool recursive, string? excludeDirectory = null) {
        string root = Path.GetFullPath(folder);
        string? excluded = excludeDirectory == null
            ? null
            : Path.TrimEndingDirectorySeparator(Path.GetFullPath(excludeDirectory)) + Path.DirectorySeparatorChar;

        EnumerationOptions options = new() { RecurseSubdirectories = recursive, IgnoreInaccessible = true };

        return Directory.EnumerateFiles(root, "*", options)
            .Where(f => VideoExtensions.Contains(Path.GetExtension(f)))
            .Where(f => excluded == null || !f.StartsWith(excluded, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
