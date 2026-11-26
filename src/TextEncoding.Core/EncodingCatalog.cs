using System.Globalization;
using System.Text;

namespace TextEncoding.Core;

/// <summary>A named text encoding that can be offered to a user or resolved from a command-line name.</summary>
/// <param name="Name">Canonical short name (what the CLI accepts and prints), e.g. "utf-8", "windows-1252".</param>
/// <param name="DisplayName">Friendly name for UI lists.</param>
/// <param name="Encoding">
/// A BOM-less encoding instance. Byte order marks are never emitted by the encoding itself;
/// <see cref="EncodingConverter"/> writes them explicitly so the caller stays in control.
/// </param>
public sealed record EncodingEntry(string Name, string DisplayName, Encoding Encoding)
{
    public int CodePage => Encoding.CodePage;

    /// <summary>True for the Unicode encodings that have a conventional byte order mark.</summary>
    public bool SupportsBom => EncodingCatalog.GetBom(CodePage).Length > 0;

    public override string ToString() => DisplayName;
}

/// <summary>An encoding plus an optional explicit BOM request, as parsed from text such as "utf-8-bom".</summary>
public sealed record EncodingSpec(EncodingEntry Entry, bool? Bom);

public static class EncodingCatalog
{
    // Must stay the first static field: legacy code pages (Windows-1252, Shift-JIS, …) need this provider
    // before any of the initializers below ask the runtime for them.
    private static readonly bool CodePagesRegistered = RegisterCodePages();

    private static bool RegisterCodePages()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return true;
    }

    private static readonly (int CodePage, string Name, string Display)[] Known =
    [
        (65001, "utf-8", "Unicode (UTF-8)"),
        (1200, "utf-16", "Unicode (UTF-16 LE)"),
        (1201, "utf-16be", "Unicode (UTF-16 BE)"),
        (12000, "utf-32", "Unicode (UTF-32 LE)"),
        (12001, "utf-32be", "Unicode (UTF-32 BE)"),
        (20127, "us-ascii", "US-ASCII"),
        (28591, "iso-8859-1", "Western European (ISO-8859-1 / Latin-1)"),
        (1252, "windows-1252", "Western European (Windows-1252)"),
        (28605, "iso-8859-15", "Western European (ISO-8859-15 / Latin-9)"),
        (28592, "iso-8859-2", "Central European (ISO-8859-2)"),
        (1250, "windows-1250", "Central European (Windows-1250)"),
        (28595, "iso-8859-5", "Cyrillic (ISO-8859-5)"),
        (1251, "windows-1251", "Cyrillic (Windows-1251)"),
        (20866, "koi8-r", "Cyrillic (KOI8-R)"),
        (21866, "koi8-u", "Cyrillic (KOI8-U)"),
        (28597, "iso-8859-7", "Greek (ISO-8859-7)"),
        (1253, "windows-1253", "Greek (Windows-1253)"),
        (28599, "iso-8859-9", "Turkish (ISO-8859-9)"),
        (1254, "windows-1254", "Turkish (Windows-1254)"),
        (1255, "windows-1255", "Hebrew (Windows-1255)"),
        (1256, "windows-1256", "Arabic (Windows-1256)"),
        (1257, "windows-1257", "Baltic (Windows-1257)"),
        (1258, "windows-1258", "Vietnamese (Windows-1258)"),
        (874, "windows-874", "Thai (Windows-874)"),
        (437, "ibm437", "OEM United States (CP437)"),
        (850, "ibm850", "OEM Western European (CP850)"),
        (932, "shift_jis", "Japanese (Shift-JIS)"),
        (51932, "euc-jp", "Japanese (EUC-JP)"),
        (936, "gb2312", "Chinese Simplified (GBK / GB2312)"),
        (54936, "gb18030", "Chinese Simplified (GB18030)"),
        (950, "big5", "Chinese Traditional (Big5)"),
        (949, "ks_c_5601-1987", "Korean (CP949)"),
        (51949, "euc-kr", "Korean (EUC-KR)"),
    ];

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["utf8"] = "utf-8",
        ["utf16"] = "utf-16", ["utf-16le"] = "utf-16", ["utf16le"] = "utf-16", ["unicode"] = "utf-16", ["ucs-2le"] = "utf-16",
        ["utf16be"] = "utf-16be", ["unicodefffe"] = "utf-16be", ["ucs-2be"] = "utf-16be",
        ["utf32"] = "utf-32", ["utf-32le"] = "utf-32", ["utf32le"] = "utf-32",
        ["utf32be"] = "utf-32be",
        ["ascii"] = "us-ascii",
        ["latin1"] = "iso-8859-1", ["latin-1"] = "iso-8859-1", ["l1"] = "iso-8859-1", ["iso8859-1"] = "iso-8859-1",
        ["latin9"] = "iso-8859-15", ["latin-9"] = "iso-8859-15",
        ["cp1252"] = "windows-1252", ["win1252"] = "windows-1252", ["ansi-1252"] = "windows-1252",
        ["sjis"] = "shift_jis", ["shift-jis"] = "shift_jis", ["shiftjis"] = "shift_jis", ["cp932"] = "shift_jis",
        ["cp936"] = "gb2312", ["gbk"] = "gb2312",
        ["cp949"] = "ks_c_5601-1987",
        ["cp950"] = "big5",
        ["cp437"] = "ibm437", ["cp850"] = "ibm850",
    };

    private static readonly Lazy<IReadOnlyList<EncodingEntry>> AllLazy = new(BuildList);

    /// <summary>All encodings offered by default, Unicode first. Entries unsupported on this machine are omitted.</summary>
    public static IReadOnlyList<EncodingEntry> All => AllLazy.Value;

    public static EncodingEntry Utf8 { get; } = Create(65001, "utf-8", "Unicode (UTF-8)")!;
    public static EncodingEntry Ascii { get; } = Create(20127, "us-ascii", "US-ASCII")!;

    /// <summary>The legacy "ANSI" code page of the current culture, falling back to Windows-1252.</summary>
    public static EncodingEntry SystemAnsi { get; } = BuildSystemAnsi();

    /// <summary>
    /// Resolves a user-supplied encoding name (canonical name, alias, "cp1252", a number, "ansi"…).
    /// A trailing "-bom" / "-sig" / "-nobom" (e.g. "utf-8-bom") also sets the BOM preference.
    /// </summary>
    /// <exception cref="ArgumentException">The name is not a known encoding.</exception>
    public static EncodingSpec Parse(string spec)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spec);
        var text = spec.Trim();
        bool? bom = null;

        foreach (var (suffix, value) in new[] { ("-nobom", false), ("-bom", true), ("-sig", true) })
        {
            if (text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && text.Length > suffix.Length)
            {
                text = text[..^suffix.Length];
                bom = value;
                break;
            }
        }

        var entry = Resolve(text) ?? throw new ArgumentException(
            $"Unknown encoding '{spec}'. Use --list to see the supported names.", nameof(spec));
        return new EncodingSpec(entry, bom);
    }

    /// <summary>Like <see cref="Parse"/> but without the BOM suffix handling; returns null when unknown.</summary>
    public static EncodingEntry? Resolve(string name)
    {
        var key = name.Trim();
        if (key.Length == 0) return null;

        if (key.Equals("ansi", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("default", StringComparison.OrdinalIgnoreCase))
            return SystemAnsi;

        if (Aliases.TryGetValue(key, out var canonical)) key = canonical;

        var known = All.FirstOrDefault(e => e.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (known is not null) return known;

        try
        {
            Encoding enc;
            if (int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var cp))
                enc = Encoding.GetEncoding(cp);
            else if (key.StartsWith("cp", StringComparison.OrdinalIgnoreCase) &&
                     int.TryParse(key.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out cp))
                enc = Encoding.GetEncoding(cp);
            else
                enc = Encoding.GetEncoding(key);

            return FromCodePage(enc.CodePage) ?? new EncodingEntry(enc.WebName, enc.EncodingName, enc);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The catalog entry for a code page, or null if the runtime doesn't support it.</summary>
    public static EncodingEntry? FromCodePage(int codePage)
    {
        foreach (var e in All)
            if (e.CodePage == codePage) return e;
        return null;
    }

    /// <summary>The conventional byte order mark for a code page (empty if the encoding has none).</summary>
    public static byte[] GetBom(int codePage) => codePage switch
    {
        65001 => [0xEF, 0xBB, 0xBF],
        1200 => [0xFF, 0xFE],
        1201 => [0xFE, 0xFF],
        12000 => [0xFF, 0xFE, 0x00, 0x00],
        12001 => [0x00, 0x00, 0xFE, 0xFF],
        _ => [],
    };

    private static IReadOnlyList<EncodingEntry> BuildList()
    {
        var list = new List<EncodingEntry>();
        foreach (var (cp, name, display) in Known)
        {
            var entry = Create(cp, name, display);
            if (entry is not null) list.Add(entry);
        }
        return list;
    }

    private static EncodingEntry? Create(int codePage, string name, string display)
    {
        try
        {
            Encoding enc = codePage switch
            {
                65001 => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                1200 => new UnicodeEncoding(bigEndian: false, byteOrderMark: false),
                1201 => new UnicodeEncoding(bigEndian: true, byteOrderMark: false),
                12000 => new UTF32Encoding(bigEndian: false, byteOrderMark: false),
                12001 => new UTF32Encoding(bigEndian: true, byteOrderMark: false),
                _ => Encoding.GetEncoding(codePage),
            };
            return new EncodingEntry(name, display, enc);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static EncodingEntry BuildSystemAnsi()
    {
        try
        {
            var cp = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
            if (cp > 0 && cp != 65001)
            {
                var known = Known.FirstOrDefault(k => k.CodePage == cp);
                if (known.Name is not null)
                {
                    var e = Create(known.CodePage, known.Name, known.Display);
                    if (e is not null) return e;
                }

                var enc = Encoding.GetEncoding(cp);
                return new EncodingEntry(enc.WebName, enc.EncodingName, enc);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            // fall through to Windows-1252
        }

        return Create(1252, "windows-1252", "Western European (Windows-1252)")!;
    }
}
