# AGENTS.md

Guidance for agents and contributors working on VGN Orb.

## Project shape

- `VGNOrb.Core` — device contracts, polling, settings and mocks. No UI framework references.
- `VGNOrb.Devices.Vgn` — HidSharp adapters and the F1 SE / V98Pro V4 protocol parsers.
- `VGNOrb.Avalonia` — Avalonia 12 application, HUD, tray, hot corner and view models.
- `VGNOrb.Core.Tests` — xUnit tests, including captured-frame protocol regressions.

The app is Windows-only and targets .NET 10. The main UI is Avalonia; do not reintroduce WPF or Windows Forms.

## Build and test

Run from the repository root:

```powershell
dotnet build VGNOrb.sln -c Release
dotnet test tests/VGNOrb.Core.Tests/VGNOrb.Core.Tests.csproj -c Release
dotnet format VGNOrb.sln --verify-no-changes
```

For a complete local release build:

```powershell
.\build.cmd
.\build.cmd -NoAot
```

The script builds x64 self-contained, framework-dependent and Native AOT packages by default. Artifacts go to `artifacts/build-<version>/`. Packaging prefers Bandizip (`bz`) and falls back to `Compress-Archive`.

The `<Version>` value in `Directory.Build.props` is the single source of truth for the product version. `build.ps1` reads it, the manifest target injects it into the embedded `app.manifest`, and the assembly and artifact names follow it. Do not duplicate the version elsewhere.

Do not commit `bin/`, `obj/`, `artifacts/` or generated screenshots.

## Release assets

Use readable ASCII asset names:

- `VGNOrb-<version>-win-x64.zip` — AOT native default
- `VGNOrb-<version>-win-x64-portable.zip` — self-contained
- `VGNOrb-<version>-win-x64-runtime-required.zip` — requires .NET 10 Desktop Runtime

Do not add `.sha256` sidecar assets; GitHub displays per-asset SHA256 digests automatically. Non-ASCII asset names are normalized by the GitHub upload API and can disappear or collide.

The AOT zip is **not** a single-file executable: `VGNOrb.exe` must stay next to `libSkiaSharp.dll`, `libHarfBuzzSharp.dll` and `av_libglesv2.dll`. Keep the packaging warning in the README.

## Avalonia UI rules

- Keep the HUD docked to the configured corner of the primary work area
  (`hudCorner`, default top-right).
- The visible card is 300 × 100 for one device and 300 × 200 for two.
- The two screen-facing edges stay flush; only the corner facing into the screen
  keeps the 30 px radius. Left-side placements mirror the row.
- The hot corner is 20 × 20 logical pixels; the hint line lives inside it and
  follows the active corner.
- Both overlay windows are click-through and must not steal focus.
- Preserve the spring animation timing:
  - show: 600 ms, 68% overshoot `+7px/+5px`, 84% `-2px`
  - root fade: 80 ms
  - content fade: 110 ms delay, 220 ms duration
  - hide: 230 ms BackEase, content 90 ms, root 70 + 150 ms
- The 100% battery label must use the 30 px fallback; other values use 34 px.
- Prefer the existing Avalonia `DeviceRow`, `BatteryRing` and `DeviceHudController` rather than adding a second rendering path.

## View models and threading

- `OrbViewModel` owns discovery and the two-device limit.
- `DeviceTileViewModel` owns one device's connection and polling lifecycle.
- Use `Avalonia.Threading.Dispatcher.UIThread`; update UI state through the existing `PostToUi` path.
- Keep `VGNOrb.Core` free of Avalonia, WPF and Windows Forms references.

## Settings

- Settings live at `%APPDATA%\VGNOrb\settings.json`.
- Serialization uses the source-generated `AppSettingsJsonContext`. Do not switch back to reflection-based `JsonSerializer` overloads; that breaks Native AOT.
- Preserve the 1000–60000 ms polling clamp.
- `hudCorner` accepts `TopRight` (default), `TopLeft`, `BottomRight` and `BottomLeft`; unknown values normalize back to `TopRight`.

## Device and protocol rules

- This project is read-only. Do not add device-write commands unless they are independently verified and explicitly tested.
- Add captured request/response frames as regression tests before changing a parser.
- Never copy vendor firmware, vendor source code or private protocol implementations.
- Unknown models or PIDs must be treated as unverified, not silently assumed compatible.
- A failed or sleeping device must not affect the other device's polling or UI.

## Native AOT

AOT is supported and is the default build:

```powershell
dotnet publish src/VGNOrb.Avalonia/VGNOrb.Avalonia.csproj -c Release -r win-x64 `
  -p:PublishAot=true -p:NuGetAudit=false `
  -p:SuppressTrimAnalysisWarnings=true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

HidSharp currently produces trim warnings; the warning suppression is intentional for this build. Re-test real mouse and keyboard connections after any AOT/toolchain change.

## Fonts and licensing

- Code and documentation are MIT licensed.
- GeoSansLight is not redistributed. The app uses a locally installed font and falls back to Segoe UI.
- Do not add GeoSansLight TTF/OTF files to the repository or release packages without permission from the font author.
- Keep third-party notices up to date; Avalonia, SkiaSharp, HarfBuzzSharp, ANGLE and HidSharp are covered in `THIRD-PARTY-NOTICES.md`.

## Commit style

Use Conventional Commits, for example:

```text
feat(ui): add top-right hover HUD
fix(build): use ASCII release asset names
docs: refresh README
```

Keep commits focused and run the Release build, tests and `dotnet format` before committing.
