# Third-party notices

**TextEncoding** is © 2026 Per Hejndorf and is licensed under the [MIT License](LICENSE), except where stated below. This file lists the third-party components and assets it uses, each under its own licence. Licence texts and copyright notices are kept by the upstream projects at the *Source* links; where a licence requires its notice to travel with binaries, the upstream package already carries it.

_Last reviewed: 2026-11-26. Versions are the ones resolved in this repository at that date; Dependabot keeps them current, so check the project files for the exact version of a given release._

## Libraries included in the application

| Library | Version | Licence | Source | Modified |
|---|---|---|---|---|
| Velopack | 1.3.39-g8d2844f | MIT | [NuGet](https://www.nuget.org/packages/Velopack/1.3.39-g8d2844f) · [source](https://github.com/velopack/velopack) | No |

The legacy code pages (Windows-1252, ISO-8859-x, DOS 850, Shift-JIS, …) come from `CodePagesEncodingProvider`, which is part of the .NET runtime itself; no extra package is shipped for them.

## Build, test and tooling (not distributed)

Used to build, test or draw the logo only; not part of the shipped application.

| Library | Version | Licence | Source | Modified |
|---|---|---|---|---|
| .NET Desktop Runtime (prerequisite, installed separately; not bundled) | 10.0 | MIT | [dotnet/runtime](https://github.com/dotnet/runtime) | No |
| vpk (Velopack packaging tool, used in CI) | 1.3.39-g8d2844f | MIT | [NuGet](https://www.nuget.org/packages/vpk) · [source](https://github.com/velopack/velopack) | No |
| coverlet.collector | 6.0.4 | MIT | [NuGet](https://www.nuget.org/packages/coverlet.collector/6.0.4) · [source](https://github.com/coverlet-coverage/coverlet) | No |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/17.14.1) · [source](https://github.com/microsoft/vstest) | No |
| xunit | 2.9.3 | Apache-2.0 | [NuGet](https://www.nuget.org/packages/xunit/2.9.3) · [source](https://github.com/xunit/xunit) | No |
| xunit.runner.visualstudio | 3.1.5 | Apache-2.0 | [NuGet](https://www.nuget.org/packages/xunit.runner.visualstudio/3.1.5) · [source](https://github.com/xunit/visualstudio.xunit) | No |

## Fonts, data and online services

| Item | Used for | Terms | Source |
|---|---|---|---|
| Cascadia Code (font, ships with Windows) | The outline of the ß in the logo, traced by `art/make-icon.cs`. The font itself is not distributed. | SIL Open Font License 1.1 | [microsoft/cascadia-code](https://github.com/microsoft/cascadia-code) |
| GitHub Releases | **Check for updates** reads this repository's releases | [GitHub Terms of Service](https://docs.github.com/site-policy/github-terms/github-terms-of-service) | [github.com](https://github.com) |

## Icons, images, audio and video

| Asset(s) | Origin | Licence |
|---|---|---|
| `art/icon.svg`, `art/icon.png`, `art/icon.ico` | Drawn in code by Claude (Anthropic) with `art/make-icon.cs`, under the author's direction; not made with an image-generation AI. The ß is the glyph outline from Cascadia Code. | MIT (as part of this project); the glyph outline is from a font under the SIL Open Font License 1.1, which allows its use in artwork |
| `testdata/*.txt` | Text samples written for this project by the author and Claude | MIT (as part of this project) |

The app ships no audio or video.

## Trademarks

Microsoft, Windows, .NET and other Microsoft product names are trademarks of the Microsoft group of companies. All other product names, logos and brands mentioned in this repository are the property of their respective owners. They are used only to identify the services and tools this project works with, and their use does not imply endorsement.
