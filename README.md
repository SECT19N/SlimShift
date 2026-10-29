# SlimShift

A cross-platform video converter built on FFmpeg, with an interactive menu and a scriptable command line. SlimShift detects which video encoders actually work on your machine (including GPU encoders), only offers those, and recommends the best one for your hardware.

## Install

Download the file for your platform from the [Releases](../../releases) page. Builds are self-contained, so no .NET install is needed.

| Platform | File |
|---|---|
| Windows x64 / ARM64 | `slimshift-win-x64.zip`, `slimshift-win-arm64.zip` |
| Linux x64 / ARM64 | `slimshift-linux-x64.tar.gz`, `slimshift-linux-arm64.tar.gz` |
| macOS Intel / Apple Silicon | `slimshift-osx-x64.tar.gz`, `slimshift-osx-arm64.tar.gz` |

**Linux:** extract and run `./SlimShift`.

**macOS:** builds are unsigned. After extracting, run:

```
xattr -d com.apple.quarantine SlimShift
chmod +x SlimShift
```

### FFmpeg

SlimShift looks for FFmpeg and FFprobe in this order:

1. On your `PATH`. This is preferred, because package-manager builds usually have the best hardware support.
2. A per-user cache (`%LOCALAPPDATA%\SlimShift\ffmpeg` on Windows, `~/.local/share/SlimShift/ffmpeg` on Linux).
3. Downloaded automatically on first run (Windows and Linux, from [BtbN's builds](https://github.com/BtbN/FFmpeg-Builds)).

On macOS there is no automatic download: run `brew install ffmpeg`.

## Usage

Run with no arguments for the interactive menu:

```
SlimShift
```

Or use commands directly:

```
SlimShift list-encoders
SlimShift convert movie.mkv --codec av1
SlimShift convert movie.mp4 --encoder libx265 -q 26 --preset slow
SlimShift scale movie.mp4 --height 720
SlimShift fps movie.mp4 --fps 30 --interpolate
```

| Command | Purpose |
|---|---|
| `list-encoders` | Show the encoders that work on this machine and which is recommended per codec |
| `convert <input>` | Re-encode to another codec/encoder; can also resize (`--height`) and change framerate (`--fps`) |
| `scale <input> --height N` | Change resolution, down or up. Width follows, aspect ratio is kept |
| `fps <input> --fps N` | Change framerate. `--interpolate` synthesises frames (slow) instead of dropping/duplicating |

Common options for `convert`, `scale` and `fps`:

| Option | Meaning |
|---|---|
| `-o, --output <path>` | Output file. Default: `<input>-slim.<ext>` beside the input |
| `--codec <h264\|h265\|vp9\|av1>` | Target codec. Default: the source codec |
| `--encoder <name>` | A specific encoder. Rejected if it doesn't work on this machine |
| `-q, --quality <n>` | Quality, lower is better. Range depends on the encoder (e.g. 0-51 for x264, 0-63 for AV1) |
| `--preset <name>` | Speed/efficiency preset, where the encoder has one |
| `--prefer-size` | Prefer software encoders (slower, smaller files) over hardware |
| `--overwrite` | Replace an existing output file |

Without `--encoder`, SlimShift picks the recommended one: hardware first for speed, or software first with `--prefer-size`. Exit code is 0 on success and non-zero on failure, so it is safe to script. Ctrl+C cancels and removes the partial output.

## How detection works

An encoder is offered only if the FFmpeg build lists it **and** a real test encode succeeds. A GPU encoder for hardware you don't have, or without a working driver, never appears, and can't be selected.

| Codec | Software | Hardware |
|---|---|---|
| H.264 | libx264 | NVENC, QSV, AMF, VideoToolbox |
| H.265 | libx265 | NVENC, QSV, AMF, VideoToolbox |
| VP9 | libvpx-vp9 | QSV |
| AV1 | libsvtav1, libaom-av1 | NVENC, QSV, AMF |

Each encoder family gets its own quality and preset options. Linux VAAPI is not supported yet.

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet build SlimShift.sln
dotnet test SlimShift.sln
dotnet run --project src/SlimShift.Cli -- list-encoders
```

```
src/SlimShift.Core/          UI-free library: FFmpeg locator, encoder detection, profiles,
                             recommender, job planner, probe and conversion runner
src/SlimShift.Cli/           Executable: Spectre.Console.Cli commands and the interactive menu
tests/SlimShift.Core.Tests/  Unit tests (no FFmpeg needed)
```

Core has no console code, so a future GUI (Avalonia) can reuse it directly. Pushing a `v*` tag runs the tests on Windows, Linux and macOS and publishes the six release builds via GitHub Actions.

## Status

Tested on Windows. Linux and macOS builds are produced and unit-tested in CI but have not been run by hand yet. VideoToolbox (macOS) settings, Ctrl+C cancellation and the interactive menu's prompts have had little or no hands-on testing.

## Roadmap

- Avalonia GUI
- Linux VAAPI support
- Batch folders and presets
- macOS code signing

## License

GPL-3.0. See [LICENSE.txt](LICENSE.txt).
