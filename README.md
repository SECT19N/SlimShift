# SlimShift

A cross-platform command-line video converter built on FFmpeg. SlimShift detects which video encoders actually work on your machine (including GPU encoders), only offers those, and recommends the best one for your hardware.

> **Status: under active revival.** Encoder detection works. Conversion, scaling, framerate changes, the interactive menu and published release builds are still being rebuilt. See the [roadmap](#roadmap).

## What works today

```
slimshift list-encoders
```

Locates FFmpeg, test-encodes with every encoder SlimShift knows about, and prints only the ones that really work on this machine:

```
┌───────┬────────────┬──────────────────┐
│ Codec │ Encoder    │ Type             │
├───────┼────────────┼──────────────────┤
│ H.264 │ libx264    │ software         │
│ H.264 │ h264_qsv   │ hardware (Intel) │
│ AV1   │ libsvtav1  │ software         │
└───────┴────────────┴──────────────────┘
```

A hardware encoder is listed only if the FFmpeg build includes it **and** a real test encode succeeds, so a missing GPU or driver means the encoder doesn't appear.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build from source (released builds will be self-contained).
- FFmpeg and FFprobe. SlimShift finds them in this order:
  1. On your `PATH` (preferred; package-manager builds usually have the best hardware support).
  2. A per-user cache: `%LOCALAPPDATA%\SlimShift\ffmpeg` (Windows) or `~/.local/share/SlimShift/ffmpeg` (Linux).
  3. Downloaded automatically on first run (Windows and Linux, from [BtbN's FFmpeg builds](https://github.com/BtbN/FFmpeg-Builds)).

  On macOS there is no automatic download. Install FFmpeg with `brew install ffmpeg`.

## Build and run

```
dotnet build SlimShift.sln
dotnet run --project src/SlimShift.Cli -- list-encoders
```

## Project layout

```
src/SlimShift.Core/   UI-free library: FFmpeg locator, process runner, encoder catalog and detection
src/SlimShift.Cli/    Executable (SlimShift): Spectre.Console.Cli commands
```

Core has no console code, so the planned menu, the CLI and a future Avalonia GUI can all share it.

## Supported encoders

| Codec | Software | Hardware |
|---|---|---|
| H.264 | libx264 | NVENC, QSV, AMF, VideoToolbox |
| H.265 | libx265 | NVENC, QSV, AMF, VideoToolbox |
| VP9 | libvpx-vp9 | QSV |
| AV1 | libsvtav1, libaom-av1 | NVENC, QSV, AMF |

Linux VAAPI is not supported yet.

## Roadmap

1. ~~Core library, FFmpeg locator, real encoder detection, `list-encoders`~~ (done)
2. Per-encoder quality and preset settings, plus a recommender that suggests the best working encoder
3. Conversion engine with progress, and downscale, upscale and framerate operations
4. `convert`, `scale` and `fps` commands, and the interactive menu (run with no arguments)
5. Unit tests
6. GitHub Actions builds for win-x64, win-arm64, linux-x64, linux-arm64, osx-x64 and osx-arm64
7. Avalonia GUI

## Platform notes

- **Windows and Linux:** primary targets. Linux has not been tested yet.
- **macOS:** builds will be unsigned. After downloading, run `xattr -d com.apple.quarantine SlimShift` and `chmod +x SlimShift`.

## License

GPL-3.0. See [LICENSE.txt](LICENSE.txt).
