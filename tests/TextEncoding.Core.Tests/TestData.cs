using System.Text;

namespace TextEncoding.Core.Tests;

/// <summary>The samples in /testdata (see testdata/README.md) and helpers around them.</summary>
internal static class TestData
{
    /// <summary>
    /// Content of utf8-nordic.txt, iso-8859-1.txt and ibm850.txt: Danish, Swedish and German letters only, which
    /// ISO-8859-1, Windows-1252 and DOS code page 850 can all hold.
    /// </summary>
    public const string Nordic =
        "Dansk: Rødgrød med fløde, blåbær på Ærø. ÆØÅ æøå\r\n" +
        "Svenska: Smörgåsbord och köttbullar i Göteborg. ÅÄÖ åäö\r\n" +
        "Deutsch: Grüße aus Köln, Äpfel, Öl und Übermaß. ÄÖÜ äöüß\r\n";

    /// <summary>Content of utf8-latin.txt and windows-1252.txt: adds punctuation only Windows-1252 has.</summary>
    public const string Latin =
        "Hello, world!\r\n" +
        Nordic +
        "Tegn: “citat” ‘enkelt’ – 5 € · naïve café\r\n";

    /// <summary>
    /// Content of the utf8/utf16/utf32 samples: line 6 adds ẞ (U+1E9E), Ω and 😀 (U+1F600, a surrogate pair),
    /// none of which Windows-1252 can hold.
    /// </summary>
    public const string Unicode = Latin + UnicodeOnlyLine;

    public const string UnicodeOnlyLine = "Unicode: ẞ Ω 😀\r\n";

    /// <summary><see cref="UnicodeOnlyLine"/> after encoding to Windows-1252 with '?' replacement (one per UTF-16 unit).</summary>
    public const string UnicodeOnlyLineReplaced = "Unicode: ? ? ??\r\n";

    public const string Ascii = "Plain ASCII text.\r\nLine two.\r\n";

    /// <summary>The files whose content is <see cref="Unicode"/>, with the encoding each one is written in.</summary>
    public static TheoryData<string, string> UnicodeFiles => new()
    {
        { "utf8.txt", "utf-8" },
        { "utf8-bom.txt", "utf-8" },
        { "utf16le-bom.txt", "utf-16" },
        { "utf16be-bom.txt", "utf-16be" },
        { "utf16le-nobom.txt", "utf-16" },
        { "utf16be-nobom.txt", "utf-16be" },
        { "utf32le-bom.txt", "utf-32" },
        { "utf32be-bom.txt", "utf-32be" },
    };

    public static string PathOf(string name) => Path.Combine(AppContext.BaseDirectory, "testdata", name);

    public static byte[] Bytes(string name) => File.ReadAllBytes(PathOf(name));

    public static EncodingEntry Enc(string name) =>
        EncodingCatalog.Resolve(name) ?? throw new ArgumentException($"Unknown encoding {name}", nameof(name));

    /// <summary>Runs <see cref="EncodingConverter.ConvertStream"/> over in-memory data.</summary>
    public static byte[] Convert(byte[] input, string source, string target, bool? writeBom, bool replace,
        out StreamConversion result)
    {
        using var output = new MemoryStream();
        result = EncodingConverter.ConvertStream(new MemoryStream(input), output, Enc(source), Enc(target), writeBom, replace);
        return output.ToArray();
    }

    public static byte[] Convert(byte[] input, string source, string target, bool? writeBom = false, bool replace = false) =>
        Convert(input, source, target, writeBom, replace, out _);

    /// <summary>BOM-less bytes of <paramref name="text"/> in a catalog encoding.</summary>
    public static byte[] Encode(string text, string encoding) => Enc(encoding).Encoding.GetBytes(text);

    public static string Decode(byte[] bytes, string encoding) => Enc(encoding).Encoding.GetString(bytes);
}

/// <summary>A per-test scratch folder, deleted on dispose.</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TextEncoding.Core.Tests",
        Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string File(string name) => System.IO.Path.Combine(Path, name);

    /// <summary>Copies a testdata sample into the folder and returns its new path.</summary>
    public string Copy(string sample, string? name = null)
    {
        var target = File(name ?? sample);
        System.IO.File.Copy(TestData.PathOf(sample), target);
        return target;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>A non-seekable stream that returns at most a few bytes per Read, like a slow pipe or socket.</summary>
internal sealed class TrickleStream(byte[] data, int maxRead = 7) : Stream
{
    private readonly MemoryStream _inner = new(data);

    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, Math.Min(count, maxRead));
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
