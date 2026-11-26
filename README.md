# TextEncoding

[![Release](https://img.shields.io/github/v/release/kreaton/TextEncoding)](https://github.com/kreaton/TextEncoding/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](LICENSE)
![Windows](https://img.shields.io/badge/platform-Windows-lightgrey)
![.NET 10](https://img.shields.io/badge/.NET-10-lightgrey)
![WinForms](https://img.shields.io/badge/UI-WinForms-lightgrey)
[![GitHub stars](https://img.shields.io/github/stars/kreaton/TextEncoding?style=flat&logo=github&color=yellow)](https://github.com/kreaton/TextEncoding/stargazers)

⭐ If TextEncoding earns a place on your machine, [star it on GitHub](https://github.com/kreaton/TextEncoding) — it's free, made by one developer, and stars are how other people find it.

<img src="art/icon.png" alt="TextEncoding logo: a text file with a German sharp s" width="128">

**Find out what encoding a text file is in, and convert it to another one.**

Æ, ø, å, ä, ö, ü and ß showing up as `Ã¦`, `Ã¸` or `�`? Open the file, see what it really is, read it the right way and save it as UTF-8, or the other way round. A window for one file at a time, and a command line for scripts.

## Install

**[Download the installer](https://github.com/kreaton/TextEncoding/releases/latest/download/TextEncoding-win-Setup.exe)** and run it. If your PC doesn't have the .NET 10 Desktop Runtime yet, the installer adds it.

The installer isn't code-signed, so Windows may warn you about it. To continue, choose **More info → Run anyway**.

To get a newer version later, click the ⓘ button and choose **Check for updates**. To remove the app, use **Settings → Apps → Installed apps** in Windows.

## Using it

Browse to a text file, drag one onto the window, or start the app with a file (`TextEncoding.exe file.txt`, which is also what **Open with** does).

- **Detected** shows the encoding, whether the file starts with a byte order mark (BOM), how sure the guess is, and why.
- **Read as** decodes the preview again with another encoding, for when the guess is wrong. Old 8-bit code pages such as Windows-1252, ISO-8859-1 and DOS 850 can't be told apart reliably from the bytes alone.
- **Save as** and **Write BOM** choose the encoding to write. **Save** overwrites the open file, after asking. **Save As…** writes a new one, with the source file's extension (or `.txt`) as the default.
- If some text can't be converted without loss, you're asked whether to write `?` for characters the target can't hold and `�` for bytes that aren't valid in the source.

The preview shows the first 2 million characters. Saving always converts the whole file, and a failed save never leaves a half-written file behind.

## Test files

**Test files…** writes text you type as a set of files, one per encoding: UTF-8, UTF-16 and UTF-32 with and without a BOM, Windows-1252, ISO-8859-1/15, DOS 850 and any other in the list. It starts with a Danish, Swedish and German sample.

The **Can't hold** column shows which characters each encoding is missing while you type. Those files are skipped, or written with `?`, as you choose. Double-click a created file to open it in the main window and see what the detector makes of it.

## Command line

```
TextEncoding <source> <target> <encoding> [options]
TextEncoding <source> <target> --encoding <encoding> [options]
TextEncoding --detect <file>
TextEncoding --list

  -e, --encoding <name>         target encoding (utf-8, utf-16, windows-1252, … see --list)
  -s, --source-encoding <name>  read the source as this instead of auto-detecting
  -f, --force                   overwrite the target if it exists
      --bom / --no-bom          write / omit a byte order mark
      --replace                 write '?' and '�' instead of failing on lossy text
  -d, --detect                  print the detected encoding
  -l, --list                    list the encodings
      --version                 show the version
```

Add `-bom` to an encoding name to force a BOM, e.g. `utf-8-bom`. By default UTF-16 and UTF-32 get a BOM, and UTF-8 only gets one if the source was UTF-8 with a BOM. The exit code is 0 for success, 1 for an error and 2 for bad arguments.

The installer puts the program in `%LOCALAPPDATA%\TextEncoding\current\TextEncoding.exe`. It's a Windows program rather than a console one, so cmd and PowerShell don't wait for it. In scripts, use `start /wait TextEncoding.exe …` or `Start-Process -Wait -NoNewWindow -PassThru TextEncoding.exe -ArgumentList …`.

## Good to know

- **How detection works:** a BOM settles it. Without one, the NUL-byte pattern of UTF-16, then plain ASCII, then strictly valid UTF-8. Anything else is assumed to be the PC's ANSI code page (Windows-1252 on Danish, Swedish and German Windows) and shown with *Low* confidence.
- **Big files are fine.** Nothing reads a whole file into memory, so a 236 MB file converts in about 40 MB.
- **Converting a file onto itself is safe.** The result goes to a temporary file first and replaces the target only when the whole conversion succeeded.
- **Nothing leaves the PC**, except when you choose **Check for updates**, which asks GitHub for the latest release.

## Credits

Made by Per Hejndorf, with Claude (Anthropic). The same credits are on the About card, behind the ⓘ button at the top right.

- [Velopack](https://velopack.io), for installs and updates
- The ß in the logo is from [Cascadia Code](https://github.com/microsoft/cascadia-code) by Microsoft (SIL Open Font License 1.1)

© 2026 Per Hejndorf

---

## For developers

| Project | Description |
| --- | --- |
| `src/TextEncoding.Core` | Detection, streaming conversion and the test-file generator, with no UI (`net10.0`) |
| `src/TextEncoding` | The WinForms app and command line (`net10.0-windows`) |
| `tests/TextEncoding.Core.Tests` | xUnit tests of the core |
| `testdata` | Byte-exact encoding samples the tests use; see [testdata/README.md](testdata/README.md) |
| `art` | The logo, drawn by `make-icon.cs` |

### Build and run

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project src/TextEncoding
```

```bash
dotnet test
```

To redraw the logo (`art/icon.svg`, `icon.png` and `icon.ico`): `dotnet run art/make-icon.cs`.

### Using the library

Detection, conversion and the preview all work on fixed-size buffers. The decoder and encoder keep their state between chunks, so a UTF-8 sequence or a surrogate pair split across a chunk boundary comes through intact.

```csharp
using TextEncoding.Core;

// Detect without reading the whole file (stops at the first invalid UTF-8 byte).
DetectionResult d = EncodingDetector.DetectFile("in.txt");

// Convert anything to UTF-8, streaming (any Stream in, any Stream out).
using var input  = File.OpenRead("in.txt");
using var output = File.Create("out.txt");
EncodingConverter.ConvertStream(input, output, d.Encoding, EncodingCatalog.Utf8, writeBom: false, replace: false);

// Whole-file helper: temp file + atomic move, so a failure never damages the target (also safe in place).
EncodingConverter.ConvertFile("in.txt", "out.txt", "utf-8", new ConvertOptions { Force = true });

// Read text lazily as a normal StreamReader (BOM skipped; invalid bytes become U+FFFD with replaceInvalid: true).
using var reader = EncodingConverter.OpenReader(File.OpenRead("in.txt"), d.Encoding, replaceInvalid: false, out _);
while (reader.ReadLine() is { } line) { /* ... */ }

// Write the same text in many encodings.
TestFileGenerator.Generate("samples", "sample", TestFileGenerator.SampleText, TestFileGenerator.DefaultSpecs);
```

### Releases

Every push to `main` runs the tests, builds the app on GitHub Actions and publishes a new release, one patch version higher than the last (`.github/workflows/release.yml`). To push without releasing, add `[skip ci]` to the commit message. You can also start a release by hand from the repository's Actions tab, or run `src\TextEncoding\BuildRelease.ps1` locally. Add `-LocalOnly` to build without uploading.

The repository must stay public, because **Check for updates** reads its releases without logging in.

## License & Authorship

This project is distributed under the terms of the [MIT License](LICENSE).

**AI authorship note:** Most of the source code in this repository was generated with AI coding agents (Claude Code), under my direction and review. Purely AI-generated material may not be eligible for copyright protection in some jurisdictions. To the extent any part of this project is protected by copyright, it is licensed under the MIT License, and the license's warranty and liability disclaimers apply to the project as a whole.

<!-- notices:start -->
### Independent development

TextEncoding is an independent, personal project developed by Per Hejndorf. It is not affiliated with, endorsed by or sponsored by Microsoft or any other rights holder. Product names and logos are trademarks of their respective owners and are used only to identify the services and tools the project works with.

### Third-party notices and asset provenance

[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) lists every third-party library, font and online service the project uses, with version, licence, source and whether it was modified. It also records where the icons, images, audio and video came from and how they are licensed.
<!-- notices:end -->
