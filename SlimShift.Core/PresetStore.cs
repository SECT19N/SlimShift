using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SlimShift;

/// <summary>Raised for problems the user can fix: a bad name, a missing or damaged preset.</summary>
public sealed class PresetException(string message) : Exception(message);

/// <summary>Saves presets as one JSON file each. Names are case-insensitive and stored in lower case.</summary>
public sealed partial class PresetStore(string directory) {
    static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public string Directory { get; } = directory;

    /// <summary>The per-user location: %APPDATA%\SlimShift\presets on Windows, ~/.config/SlimShift/presets on Linux.</summary>
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SlimShift", "presets");

    // Starts with a letter or digit (so ".." and hidden names are out) and holds no path separators.
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$")]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"^(con|prn|aux|nul|com\d|lpt\d)$", RegexOptions.IgnoreCase)]
    private static partial Regex ReservedWindowsName();

    public static bool IsValidName(string? name) =>
        name != null && NamePattern().IsMatch(name) && !ReservedWindowsName().IsMatch(name.Split('.')[0]);

    public bool Exists(string name) => IsValidName(name) && File.Exists(PathFor(name));

    public void Save(string name, EncodePreset preset, bool overwrite = false) {
        string path = PathFor(name);

        if (preset.Validate() is { } problem) throw new PresetException(problem);
        if (!overwrite && File.Exists(path))
            throw new PresetException($"A preset named '{name}' already exists. Use --overwrite to replace it.");

        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(path, JsonSerializer.Serialize(preset, JsonOptions));
    }

    public EncodePreset Load(string name) {
        string path = PathFor(name);

        if (!File.Exists(path)) {
            IReadOnlyList<string> saved = List();
            string known = saved.Count == 0 ? "none saved yet" : string.Join(", ", saved);
            throw new PresetException($"There is no preset named '{name}' ({known}).");
        }

        try {
            EncodePreset? preset = JsonSerializer.Deserialize<EncodePreset>(File.ReadAllText(path), JsonOptions);
            if (preset == null) throw new JsonException("The file is empty.");
            return preset;
        } catch (JsonException ex) {
            throw new PresetException($"Preset '{name}' is damaged ({path}): {ex.Message}");
        }
    }

    public IReadOnlyList<string> List() =>
        !System.IO.Directory.Exists(Directory)
            ? []
            : System.IO.Directory.EnumerateFiles(Directory, "*.json")
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();

    public void Delete(string name) {
        string path = PathFor(name);
        if (!File.Exists(path)) throw new PresetException($"There is no preset named '{name}'.");
        File.Delete(path);
    }

    string PathFor(string name) {
        if (!IsValidName(name))
            throw new PresetException(
                $"'{name}' is not a valid preset name. Use letters, digits, '.', '_' or '-' (up to 64 characters, starting with a letter or digit).");

        return Path.Combine(Directory, name.ToLowerInvariant() + ".json");
    }
}
