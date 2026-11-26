using TextEncoding.Core;

namespace TextEncoding;

/// <summary>
/// Command-line mode. Deliberately free of any WinForms dependency: it only talks to the
/// two writers it is given, so it can be hosted by anything (see Program.cs).
/// </summary>
internal static class Cli
{
    public const int ExitOk = 0;
    public const int ExitError = 1;
    public const int ExitUsage = 2;

    /// <summary>
    /// No arguments, or a single file path, starts the UI (the single path is opened in it,
    /// which is what Explorer's "Open with" passes). Anything else is command-line mode.
    /// </summary>
    public static bool IsCliInvocation(string[] args) => args.Length switch
    {
        0 => false,
        1 => IsOptionLike(args[0]),
        _ => true,
    };

    private static bool IsOptionLike(string arg) => arg.StartsWith('-') || arg == "/?";

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        Parsed p;
        try
        {
            p = Parse(args);
        }
        catch (UsageException ex)
        {
            error.WriteLine($"Error: {ex.Message}");
            error.WriteLine("Run 'TextEncoding --help' for usage.");
            return ExitUsage;
        }

        if (p.Help)
        {
            output.WriteLine(HelpText);
            return ExitOk;
        }

        if (p.Version)
        {
            output.WriteLine($"TextEncoding {BuildInfo.Display}");
            return ExitOk;
        }

        if (p.List)
        {
            foreach (var e in EncodingCatalog.All)
                output.WriteLine($"{e.Name,-16} {e.DisplayName}");
            output.WriteLine();
            output.WriteLine("Aliases such as utf8, latin1, ansi and cp<number> are also accepted.");
            output.WriteLine("Append -bom or -nobom to force a BOM choice, e.g. utf-8-bom.");
            return ExitOk;
        }

        try
        {
            return p.Detect ? RunDetect(p, output, error) : RunConvert(p, output, error);
        }
        catch (UsageException ex)
        {
            error.WriteLine($"Error: {ex.Message}");
            error.WriteLine("Run 'TextEncoding --help' for usage.");
            return ExitUsage;
        }
        catch (ConversionException ex)
        {
            error.WriteLine($"Error: {ex.Message}");
            return ExitError;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            error.WriteLine($"Error: {ex.Message}");
            return ExitError;
        }
    }

    private static int RunDetect(Parsed p, TextWriter output, TextWriter error)
    {
        if (p.Positional.Count != 1)
            throw new UsageException("--detect takes exactly one file.");

        var path = p.Positional[0];
        if (!File.Exists(path))
            throw new ConversionException($"File not found: {path}");

        var d = EncodingDetector.DetectFile(path);
        output.WriteLine($"File:       {path}");
        output.WriteLine($"Encoding:   {d.Encoding.DisplayName} [{d.Encoding.Name}]");
        output.WriteLine($"BOM:        {(d.HasBom ? "yes" : "no")}");
        output.WriteLine($"Confidence: {d.Confidence}");
        output.WriteLine($"Details:    {d.Reason}");
        return ExitOk;
    }

    private static int RunConvert(Parsed p, TextWriter output, TextWriter error)
    {
        var positional = p.Positional;
        var encoding = p.Encoding;

        if (encoding is null && positional.Count == 3)
        {
            encoding = positional[2];
            positional = positional.Take(2).ToList();
        }

        if (positional.Count > 2)
            throw new UsageException($"Unexpected extra argument '{positional[2]}'.");
        if (positional.Count < 2)
            throw new UsageException("A source file and a target file are required.");
        if (encoding is null)
            throw new UsageException("A target encoding is required (positional or --encoding).");

        var source = positional[0];
        var target = positional[1];

        var result = EncodingConverter.ConvertFile(source, target, encoding, new ConvertOptions
        {
            Force = p.Force,
            SourceEncoding = p.SourceEncoding,
            WriteBom = p.Bom,
            ReplaceUnmappable = p.Replace,
        });

        var how = p.SourceEncoding is not null
            ? "specified"
            : $"detected, {result.Source.Confidence} confidence";

        output.WriteLine($"Converted '{source}' -> '{target}'");
        output.WriteLine($"  from: {result.SourceEncoding.DisplayName} [{how}]");
        output.WriteLine($"  to:   {result.Target.DisplayName}{(result.WroteBom ? " with BOM" : "")}");
        output.WriteLine($"  {result.Characters:N0} characters, {result.BytesWritten:N0} bytes written");

        if (result.HadReplacements)
            error.WriteLine("Warning: some characters could not be converted and were replaced.");

        return ExitOk;
    }

    private sealed class Parsed
    {
        public List<string> Positional { get; } = [];
        public string? Encoding { get; set; }
        public string? SourceEncoding { get; set; }
        public bool Force { get; set; }
        public bool? Bom { get; set; }
        public bool Replace { get; set; }
        public bool List { get; set; }
        public bool Detect { get; set; }
        public bool Help { get; set; }
        public bool Version { get; set; }
    }

    private static Parsed Parse(string[] args)
    {
        var p = new Parsed();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            string? inline = null;

            // --option=value
            if (arg.StartsWith("--", StringComparison.Ordinal) && arg.IndexOf('=') is var eq and > 2)
            {
                inline = arg[(eq + 1)..];
                arg = arg[..eq];
            }

            string Value()
            {
                if (inline is not null) return inline;
                if (i + 1 < args.Length) return args[++i];
                throw new UsageException($"Option '{arg}' needs a value.");
            }

            void NoValue()
            {
                if (inline is not null) throw new UsageException($"Option '{arg}' does not take a value.");
            }

            switch (arg.ToLowerInvariant())
            {
                case "-h" or "--help" or "/?": NoValue(); p.Help = true; break;
                case "--version": NoValue(); p.Version = true; break;
                case "-f" or "--force": NoValue(); p.Force = true; break;
                case "-e" or "--encoding": p.Encoding = Value(); break;
                case "-s" or "--source-encoding": p.SourceEncoding = Value(); break;
                case "--bom": NoValue(); p.Bom = true; break;
                case "--no-bom": NoValue(); p.Bom = false; break;
                case "--replace": NoValue(); p.Replace = true; break;
                case "-l" or "--list": NoValue(); p.List = true; break;
                case "-d" or "--detect": NoValue(); p.Detect = true; break;
                default:
                    if (arg.Length > 1 && arg[0] == '-')
                        throw new UsageException($"Unknown option '{arg}'.");
                    p.Positional.Add(arg);
                    break;
            }
        }

        return p;
    }

    private sealed class UsageException(string message) : Exception(message);

    public const string HelpText = """
        TextEncoding - detect and convert the encoding of text files

        Usage:
          TextEncoding                                   open the window
          TextEncoding <file>                            open <file> in the window
          TextEncoding <source> <target> <encoding> [options]
          TextEncoding <source> <target> --encoding <encoding> [options]
          TextEncoding --detect <file>                   print the detected encoding
          TextEncoding --list                            list the supported encodings

        Options:
          -e, --encoding <name>         target encoding (e.g. utf-8, utf-16, windows-1252)
          -s, --source-encoding <name>  read the source as this instead of auto-detecting
          -f, --force                   overwrite the target file if it exists
              --bom / --no-bom          write / omit a byte order mark
                                        (default: always for UTF-16/32; for UTF-8 only
                                        if the source was UTF-8 with a BOM)
              --replace                 replace unconvertible characters with '?' and
                                        undecodable source bytes with U+FFFD instead
                                        of failing
          -d, --detect                  detect only, don't convert
          -l, --list                    list supported encodings
              --version                 show the version
          -h, --help                    show this help

        Examples:
          TextEncoding notes.txt notes-utf8.txt utf-8
          TextEncoding old.csv new.csv utf-8-bom --force
          TextEncoding in.txt out.txt -e windows-1252 -s utf-8 --replace

        Exit codes: 0 success, 1 error (file, encoding or conversion problem), 2 bad arguments.

        Note: this is a Windows GUI program, so cmd.exe and PowerShell don't wait for it and don't
        set %ERRORLEVEL% / $LASTEXITCODE reliably. In scripts use:  start /wait TextEncoding ...
        or in PowerShell:  Start-Process -Wait -NoNewWindow -PassThru TextEncoding.exe -ArgumentList ...
        """;
}
