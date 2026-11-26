using System.Text;

namespace TextEncoding.Core;

/// <summary>One file of a test set: an encoding plus whether it gets a byte order mark.</summary>
public sealed record TestFileSpec(EncodingEntry Encoding, bool Bom)
{
    /// <summary>The part of the file name that identifies the variant, e.g. "utf-8-bom", "windows-1252".</summary>
    public string Suffix => Bom ? $"{Encoding.Name}-bom" : Encoding.Name;

    public string DisplayName => Bom ? $"{Encoding.DisplayName} with BOM" : Encoding.DisplayName;

    public override string ToString() => DisplayName;
}

public enum LineEndings
{
    /// <summary>CR LF (Windows).</summary>
    CrLf,
    /// <summary>LF (Unix, macOS).</summary>
    Lf,
}

/// <summary>What to do when the text contains characters an encoding can't represent.</summary>
public enum UnmappablePolicy
{
    /// <summary>Don't write that file (it is reported as skipped).</summary>
    Skip,
    /// <summary>Write it with '?' in place of each such character.</summary>
    Replace,
}

public enum TestFileOutcome
{
    Written,
    /// <summary>Written, but some characters became '?'.</summary>
    WrittenWithReplacements,
    /// <summary>Not written because the encoding can't hold some characters (<see cref="UnmappablePolicy.Skip"/>).</summary>
    Skipped,
}

/// <param name="Path">Full path of the file (also set for a skipped file: where it would have gone).</param>
/// <param name="Unmappable">The distinct characters the encoding can't represent, in order of appearance.</param>
public sealed record GeneratedTestFile(
    TestFileSpec Spec,
    string Path,
    TestFileOutcome Outcome,
    long Bytes,
    IReadOnlyList<string> Unmappable);

public sealed record TestFileOptions
{
    public LineEndings LineEndings { get; init; } = LineEndings.CrLf;
    public UnmappablePolicy Unmappable { get; init; } = UnmappablePolicy.Skip;

    /// <summary>Overwrite files that already exist. Without it nothing is written if any target exists.</summary>
    public bool Overwrite { get; init; }
}

/// <summary>
/// Writes the same text in many encodings, one file per <see cref="TestFileSpec"/>, named
/// <c>{baseName}-{suffix}.txt</c> (e.g. <c>sample-utf-16be-bom.txt</c>). Useful for exercising detection
/// and conversion with text of your own.
/// </summary>
public static class TestFileGenerator
{
    /// <summary>A Danish / Swedish / German sample that fits ISO-8859-1, Windows-1252 and CP850.</summary>
    public const string SampleText =
        "Dansk: Rødgrød med fløde, blåbær på Ærø. ÆØÅ æøå\r\n" +
        "Svenska: Smörgåsbord och köttbullar i Göteborg. ÅÄÖ åäö\r\n" +
        "Deutsch: Grüße aus Köln, Äpfel, Öl und Übermaß. ÄÖÜ äöüß\r\n";

    private static readonly string[] DefaultLegacy = ["windows-1252", "iso-8859-1", "iso-8859-15", "ibm850"];

    /// <summary>Every catalog encoding, Unicode ones with and without a BOM.</summary>
    public static IReadOnlyList<TestFileSpec> AllSpecs { get; } = BuildAll();

    /// <summary>
    /// A useful default selection: every Unicode variant (UTF-8 without/with BOM, UTF-16 LE/BE without/with
    /// BOM, UTF-32 LE/BE with BOM) plus the legacy Western code pages used for Nordic and German text.
    /// </summary>
    public static IReadOnlyList<TestFileSpec> DefaultSpecs { get; } = AllSpecs
        .Where(s => s.Encoding.SupportsBom
            ? s.Bom || s.Encoding.CodePage is 65001 or 1200 or 1201
            : DefaultLegacy.Contains(s.Encoding.Name))
        .ToList();

    public static string FileName(string baseName, TestFileSpec spec) => $"{baseName}-{spec.Suffix}.txt";

    /// <summary>Converts all line breaks (CR LF, CR, LF) in <paramref name="text"/> to <paramref name="endings"/>.</summary>
    public static string NormalizeLineEndings(string text, LineEndings endings)
    {
        var lf = text.Replace("\r\n", "\n").Replace('\r', '\n');
        return endings == LineEndings.CrLf ? lf.Replace("\n", "\r\n") : lf;
    }

    /// <summary>The distinct characters (whole code points) of <paramref name="text"/> that <paramref name="encoding"/> can't represent.</summary>
    public static IReadOnlyList<string> FindUnmappable(string text, EncodingEntry encoding)
    {
        var strict = WithEncoderFallback(encoding.Encoding, EncoderFallback.ExceptionFallback);
        var seen = new HashSet<Rune>();
        var missing = new List<string>();
        Span<byte> scratch = stackalloc byte[16];
        Span<char> chars = stackalloc char[2];

        foreach (var rune in text.EnumerateRunes())
        {
            if (!seen.Add(rune)) continue;
            var n = rune.EncodeToUtf16(chars);
            try
            {
                strict.GetBytes(chars[..n], scratch);
            }
            catch (EncoderFallbackException)
            {
                missing.Add(rune.ToString());
            }
        }

        return missing;
    }

    /// <summary>
    /// Writes <paramref name="text"/> to <paramref name="directory"/> (created if missing) once per spec.
    /// Line endings are normalized first. Names are checked up front: if a file exists and
    /// <see cref="TestFileOptions.Overwrite"/> is off, nothing is written.
    /// </summary>
    /// <exception cref="ConversionException">Bad base name, duplicate specs, or existing files without overwrite.</exception>
    public static IReadOnlyList<GeneratedTestFile> Generate(string directory, string baseName, string text,
        IEnumerable<TestFileSpec> specs, TestFileOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(text);
        options ??= new TestFileOptions();

        baseName = baseName.Trim();
        if (baseName.Length == 0 || baseName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ConversionException($"\"{baseName}\" is not a valid file name.");

        var list = specs.ToList();
        if (list.Count == 0) throw new ConversionException("Choose at least one encoding.");

        var dir = Path.GetFullPath(directory);
        var targets = list.Select(s => Path.Combine(dir, FileName(baseName, s))).ToList();

        var duplicate = targets.GroupBy(t => t, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new ConversionException($"Two encodings would both be written to {Path.GetFileName(duplicate.Key)}.");

        if (!options.Overwrite)
        {
            var existing = targets.Where(File.Exists).ToList();
            if (existing.Count > 0)
                throw new ConversionException(
                    $"{existing.Count} of the files already exist (e.g. {Path.GetFileName(existing[0])}). " +
                    "Allow overwriting or pick another name or folder.");
        }

        Directory.CreateDirectory(dir);
        var normalized = NormalizeLineEndings(text, options.LineEndings);

        var results = new List<GeneratedTestFile>(list.Count);
        for (var i = 0; i < list.Count; i++)
            results.Add(WriteOne(list[i], targets[i], normalized, options.Unmappable));
        return results;
    }

    private static GeneratedTestFile WriteOne(TestFileSpec spec, string path, string text, UnmappablePolicy policy)
    {
        var unmappable = FindUnmappable(text, spec.Encoding);
        if (unmappable.Count > 0 && policy == UnmappablePolicy.Skip)
            return new GeneratedTestFile(spec, path, TestFileOutcome.Skipped, 0, unmappable);

        var encoding = WithEncoderFallback(spec.Encoding.Encoding,
            unmappable.Count > 0 ? EncoderFallback.ReplacementFallback : EncoderFallback.ExceptionFallback);

        var bom = spec.Bom ? EncodingCatalog.GetBom(spec.Encoding.CodePage) : [];
        var body = encoding.GetBytes(text);

        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bom);
            stream.Write(body);
        }

        var outcome = unmappable.Count > 0 ? TestFileOutcome.WrittenWithReplacements : TestFileOutcome.Written;
        return new GeneratedTestFile(spec, path, outcome, bom.Length + body.Length, unmappable);
    }

    private static IReadOnlyList<TestFileSpec> BuildAll()
    {
        var list = new List<TestFileSpec>();
        foreach (var e in EncodingCatalog.All)
        {
            list.Add(new TestFileSpec(e, false));
            if (e.SupportsBom) list.Add(new TestFileSpec(e, true));
        }
        return list;
    }

    private static Encoding WithEncoderFallback(Encoding encoding, EncoderFallback fallback)
    {
        var clone = (Encoding)encoding.Clone();
        clone.EncoderFallback = fallback;
        return clone;
    }
}
