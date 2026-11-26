using System.Text;
using System.Text.Unicode;

namespace TextEncoding.Core;

public enum DetectionConfidence
{
    /// <summary>A guess: nothing in the data proves it.</summary>
    Low,
    /// <summary>Plausible, but other encodings would also fit.</summary>
    Medium,
    /// <summary>Strong evidence (e.g. valid non-ASCII UTF-8, clear UTF-16 NUL pattern).</summary>
    High,
    /// <summary>A byte order mark identified the encoding.</summary>
    Certain,
}

/// <param name="Encoding">The detected encoding.</param>
/// <param name="HasBom">Whether the data starts with a byte order mark for that encoding.</param>
/// <param name="BomLength">Length in bytes of that BOM (0 if none).</param>
/// <param name="Confidence">How sure the detector is.</param>
/// <param name="Reason">One-line, human-readable explanation of the verdict.</param>
public sealed record DetectionResult(
    EncodingEntry Encoding,
    bool HasBom,
    int BomLength,
    DetectionConfidence Confidence,
    string Reason)
{
    /// <summary>E.g. "Unicode (UTF-8) with BOM".</summary>
    public string Description => HasBom ? $"{Encoding.DisplayName} with BOM" : Encoding.DisplayName;
}

/// <summary>
/// Heuristic text-encoding detection: BOM → UTF-16 NUL pattern → ASCII → strict UTF-8 → legacy code page.
/// Legacy single-byte code pages can't be told apart reliably, so the last step is an explicit
/// low-confidence guess (the system ANSI code page) that callers can override.
/// </summary>
public static class EncodingDetector
{
    private const int ChunkBytes = 64 * 1024;
    private const int Utf16SampleBytes = ChunkBytes;

    /// <summary>Detects the encoding of a file, reading it in chunks (constant memory, however large).</summary>
    public static DetectionResult DetectFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1,
            FileOptions.SequentialScan);
        return Detect(stream);
    }

    /// <summary>
    /// Streaming detection: the BOM and UTF-16 checks use the first chunk, while ASCII / UTF-8 validity is
    /// verified over the whole stream with a stateful decoder (so multi-byte sequences may span chunks).
    /// Scanning stops at the first invalid UTF-8 byte. For a seekable stream the position is restored.
    /// </summary>
    public static DetectionResult Detect(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var start = stream.CanSeek ? stream.Position : -1;
        try
        {
            var buffer = new byte[ChunkBytes];
            var n = ReadFully(stream, buffer);
            if (n == 0) return Empty();

            var first = buffer.AsSpan(0, n);
            if (TryDetectBom(first, out var bom)) return bom;
            if (TryDetectUtf16WithoutBom(first, out var utf16)) return utf16;

            bool allAscii = true, hasNul = false, utf8Valid = true;
            var decoder = new UTF8Encoding(false, true).GetDecoder();
            var scratch = new char[ChunkBytes + 4];

            while (n > 0)
            {
                var chunk = buffer.AsSpan(0, n);
                if (allAscii && !System.Text.Ascii.IsValid(chunk)) allAscii = false;
                if (!hasNul && chunk.Contains((byte)0)) hasNul = true;

                try
                {
                    decoder.GetChars(chunk, scratch, flush: false);
                }
                catch (DecoderFallbackException)
                {
                    utf8Valid = false;
                    break; // not UTF-8: no need to read the rest
                }

                n = ReadFully(stream, buffer);
            }

            if (utf8Valid)
            {
                try
                {
                    decoder.GetChars([], scratch, flush: true); // a truncated sequence at the very end is invalid
                }
                catch (DecoderFallbackException)
                {
                    utf8Valid = false;
                }
            }

            return BuildResult(allAscii, utf8Valid, hasNul);
        }
        finally
        {
            if (start >= 0) stream.Position = start;
        }
    }

    private static int ReadFully(Stream stream, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0) break;
            total += read;
        }
        return total;
    }

    private static DetectionResult Empty() =>
        new(EncodingCatalog.Utf8, false, 0, DetectionConfidence.Low, "The file is empty; UTF-8 is assumed.");

    public static DetectionResult Detect(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return Empty();

        if (TryDetectBom(data, out var bomResult))
            return bomResult;

        if (TryDetectUtf16WithoutBom(data, out var utf16Result))
            return utf16Result;

        return BuildResult(System.Text.Ascii.IsValid(data), Utf8.IsValid(data), data.Contains((byte)0));
    }

    private static DetectionResult BuildResult(bool allAscii, bool utf8Valid, bool hasNul)
    {
        var nulNote = hasNul ? " It contains NUL bytes, so it may be a binary file." : "";

        if (allAscii)
            return new(EncodingCatalog.Ascii, false, 0, DetectionConfidence.High,
                "Only 7-bit ASCII characters (also valid UTF-8 and most legacy code pages)." + nulNote);

        if (utf8Valid)
            return new(EncodingCatalog.Utf8, false, 0, DetectionConfidence.High,
                "Valid UTF-8 containing multi-byte sequences; unlikely by chance in other encodings." + nulNote);

        var ansi = EncodingCatalog.SystemAnsi;
        return new(ansi, false, 0, DetectionConfidence.Low,
            $"Not valid UTF-8, so a legacy code page is assumed ({ansi.Name}). " +
            "Single-byte code pages can't be told apart reliably; choose another if the text looks wrong." + nulNote);
    }

    private static bool TryDetectBom(ReadOnlySpan<byte> d, out DetectionResult result)
    {
        // Order matters: the UTF-32 LE BOM starts with the UTF-16 LE BOM.
        var cp = 0;
        if (d.StartsWith(EncodingCatalog.GetBom(12000))) cp = 12000;
        else if (d.StartsWith(EncodingCatalog.GetBom(12001))) cp = 12001;
        else if (d.StartsWith(EncodingCatalog.GetBom(65001))) cp = 65001;
        else if (d.StartsWith(EncodingCatalog.GetBom(1200))) cp = 1200;
        else if (d.StartsWith(EncodingCatalog.GetBom(1201))) cp = 1201;

        if (cp == 0)
        {
            result = null!;
            return false;
        }

        var entry = EncodingCatalog.FromCodePage(cp)!;
        var len = EncodingCatalog.GetBom(cp).Length;
        result = new(entry, true, len, DetectionConfidence.Certain, "Identified by its byte order mark.");
        return true;
    }

    /// <summary>
    /// UTF-16 text without a BOM is mostly Latin characters whose high byte is 0x00. If NULs sit almost
    /// exclusively at odd offsets it is little-endian; almost exclusively at even offsets, big-endian.
    /// (UTF-16 of pure CJK text has no such pattern and can't be detected without a BOM.)
    /// </summary>
    private static bool TryDetectUtf16WithoutBom(ReadOnlySpan<byte> d, out DetectionResult result)
    {
        result = null!;
        var n = Math.Min(d.Length, Utf16SampleBytes) & ~1;
        if (n < 2) return false;

        int evenZeros = 0, oddZeros = 0;
        for (var i = 0; i < n; i += 2)
        {
            if (d[i] == 0) evenZeros++;
            if (d[i + 1] == 0) oddZeros++;
        }

        var pairs = n / 2;
        const double Dominant = 0.3, Rare = 0.05;

        int cp;
        if (oddZeros > pairs * Dominant && evenZeros < pairs * Rare) cp = 1200;
        else if (evenZeros > pairs * Dominant && oddZeros < pairs * Rare) cp = 1201;
        else return false;

        result = new(EncodingCatalog.FromCodePage(cp)!, false, 0, DetectionConfidence.High,
            "No BOM, but the NUL byte pattern is typical of UTF-16 text.");
        return true;
    }
}
