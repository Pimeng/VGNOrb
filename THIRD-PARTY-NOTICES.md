# Third-party notices

VGN Orb includes or depends on the following third-party components. Their
licenses remain with their respective copyright holders.

## Runtime dependencies

| Component | Version | License |
| --- | --- | --- |
| .NET 10 | 10 | MIT |
| Avalonia / Avalonia.Desktop / Avalonia.Themes.Fluent | 12.1.6 | MIT |
| SkiaSharp | 3.119.4 | MIT |
| HarfBuzzSharp | bundled with Avalonia | MIT |
| ANGLE (av_libglesv2) | bundled native renderer | BSD-3-Clause |
| System.Text.Json | .NET 10 | MIT |
| CommunityToolkit.Mvvm | 8.4.2 | MIT |
| HidSharp | 2.6.4 | MIT |

## Test dependencies

These are used only for the test project and are not part of the app binary.

| Component | Version | License |
| --- | --- | --- |
| xunit | 2.9.3 | Apache-2.0 |
| xunit.runner.visualstudio | 3.1.4 | Apache-2.0 |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT |
| coverlet.collector | 6.0.4 | MIT |

No third-party source code has been copied into this repository. Package
license files are available from the corresponding NuGet packages.

## GeoSansLight

The HUD prefers a locally installed GeoSansLight font and falls back to
Segoe UI. The font file is **not distributed with this repository** or its
release artifacts.

GeoSansLight was created by Manfred Klein. A reproduced copy of the terms is
available in the [Makespace/Branding license](https://github.com/Makespace/Branding/blob/master/LICENSE.md):

> Manfred’s fonts are free for private and charity use. They are even free for
> commercial use – but if there’s any profit, pls make a donation to
> organizations like Doctors Without Borders.
>
> These fonts can NOT be included in any compilation CDs, disks or products,
> either commercial or shareware unless prior permission granted.

Do not bundle the font files in a release without prior permission. See the
font-license note in [README.md](README.md#fonts).
