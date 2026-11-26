using System.Text;

namespace TextEncoding.Core;

/// <summary>A problem the user can act on: bad encoding name, undecodable bytes, target exists, …</summary>
/// <param name="isDataError">
/// True when the file's content can't be converted losslessly (invalid source bytes or characters the
/// target can't represent) — retrying with <see cref="ConvertOptions.ReplaceUnmappable"/> would succeed.
/// </param>
public sealed class ConversionException(string message, Exception? inner = null, bool isDataError = false)
    : Exception(message, inner)
{
    public bool IsDataError { get; } = isDataError;
}

public sealed record ConvertOptions
{
    /// <summary>Overwrite the target file if it already exists.</summary>
    public bool Force { get; init; }

    /// <summary>Encoding name to read the source with; null means auto-detect.</summary>
    public string? SourceEncoding { get; init; }

    /// <summary>
    /// Explicitly write (true) or omit (false) a byte order mark. Null picks a default:
    /// always for UTF-16/32, and for UTF-8 only if the source was UTF-8 with a BOM.
    /// </summary>
    public bool? WriteBom { get; init; }

    /// <summary>Replace characters the target can't represent (and undecodable source bytes) instead of failing.</summary>
    public bool ReplaceUnmappable { get; init; }
}

public sealed record ConversionResult(
    DetectionResult Source,
    EncodingEntry SourceEncoding,
    EncodingEntry Target,
    bool WroteBom,
    long Characters,
    long BytesWritten,
    bool HadReplacements);

/// <param name="Characters">UTF-16 code units copied.</param>
/// <param name="WroteBom">Whether a BOM was written to the output.</param>
/// <param name="SourceHadBom">Whether the input started with a BOM for its encoding.</param>
public readonly record struct StreamConversion(long Characters, bool WroteBom, bool SourceHadBom);

/// <summary>
/// Streaming conversion. Everything here works on <see cref="StreamReader"/>/<see cref="StreamWriter"/>
/// with fixed-size buffers, so memory use is constant regardless of file size. To turn any stream into UTF-8:
/// <code>
/// var source = EncodingCatalog.Resolve("windows-1252")!;
/// EncodingConverter.ConvertStream(input, output, source, EncodingCatalog.Utf8, writeBom: false, replace: false);
/// </code>
/// </summary>
public static class EncodingConverter
{
    private const int ChunkBytes = 64 * 1024;
    private const int ChunkChars = 32 * 1024;

    /// <summary>
    /// Turns undecodable bytes into U+FFFD (REPLACEMENT CHARACTER). <see cref="DecoderFallback.ReplacementFallback"/>
    /// would use '?', which can't be told apart from a real question mark in the text.
    /// </summary>
    private static readonly DecoderReplacementFallback InvalidBytesFallback = new("�");

    /// <summary>The BOM choice used when none is requested explicitly.</summary>
    public static bool DefaultBom(bool sourceHadBom, EncodingEntry source, EncodingEntry target) =>
        target.CodePage switch
        {
            65001 => sourceHadBom && source.CodePage == 65001,
            1200 or 1201 or 12000 or 12001 => true,
            _ => false,
        };

    /// <summary>Length of the BOM at the start of <paramref name="data"/> that belongs to <paramref name="encoding"/> (0 if none).</summary>
    public static int GetBomLength(ReadOnlySpan<byte> data, EncodingEntry encoding)
    {
        var bom = EncodingCatalog.GetBom(encoding.CodePage);
        return bom.Length > 0 && data.StartsWith(bom) ? bom.Length : 0;
    }

    /// <summary>Whether the file starts with the BOM that belongs to <paramref name="encoding"/> (reads at most 4 bytes).</summary>
    public static bool HasBom(string path, EncodingEntry encoding)
    {
        if (!encoding.SupportsBom) return false;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Span<byte> head = stackalloc byte[4];
        var n = stream.Read(head);
        return GetBomLength(head[..n], encoding) > 0;
    }

    /// <summary>
    /// A reader that decodes <paramref name="stream"/> as <paramref name="encoding"/> and skips that encoding's BOM.
    /// Invalid bytes throw <see cref="ConversionException"/> from <c>Read</c> unless
    /// <paramref name="replaceInvalid"/> is set, in which case they become U+FFFD.
    /// </summary>
    public static StreamReader OpenReader(Stream stream, EncodingEntry encoding, bool replaceInvalid,
        out bool hadBom, bool leaveOpen = false)
    {
        var decoding = WithFallbacks(encoding.Encoding, EncoderFallback.ExceptionFallback,
            replaceInvalid ? InvalidBytesFallback : DecoderFallback.ExceptionFallback);

        var reader = new StreamReader(stream, decoding, detectEncodingFromByteOrderMarks: false, ChunkBytes, leaveOpen);

        // The (BOM-less) encoding leaves a BOM in the text as U+FEFF; drop it.
        hadBom = false;
        try
        {
            if (encoding.SupportsBom && reader.Peek() == '\uFEFF')
            {
                reader.Read();
                hadBom = true;
            }
        }
        catch (DecoderFallbackException ex)
        {
            reader.Dispose();
            throw new ConversionException(
                $"The data is not valid {encoding.DisplayName} (near line 1). " +
                "Pick a different source encoding, or allow replacement of invalid bytes.", ex, isDataError: true);
        }

        return reader;
    }

    /// <summary>
    /// Converts <paramref name="input"/> (read as <paramref name="source"/>, its BOM skipped) to
    /// <paramref name="output"/> (written as <paramref name="target"/>) in fixed-size chunks, so memory use
    /// does not depend on the data size. Neither stream is closed.
    /// With <paramref name="replace"/> false, an undecodable byte or unrepresentable character throws a
    /// <see cref="ConversionException"/> (<see cref="ConversionException.IsDataError"/> set) that names the byte
    /// offset or line; output written before that point is not rolled back, so write to a temporary
    /// stream/file if that matters (<see cref="ConvertFile"/> does).
    /// </summary>
    /// <param name="writeBom">Explicit BOM choice; null uses <see cref="DefaultBom"/>. Ignored for encodings without a BOM.</param>
    public static StreamConversion ConvertStream(Stream input, Stream output, EncodingEntry source,
        EncodingEntry target, bool? writeBom, bool replace)
    {
        var transcoder = new Transcoder(output, source, target, replace);

        // Read the first chunk completely so the whole BOM is visible, then decode chunk by chunk.
        var buffer = new byte[ChunkBytes];
        var n = ReadFully(input, buffer);

        var skip = GetBomLength(buffer.AsSpan(0, n), source);
        var hadBom = skip > 0;
        var bom = writeBom ?? DefaultBom(hadBom, source, target);
        if (!target.SupportsBom) bom = false;
        if (bom) output.Write(EncodingCatalog.GetBom(target.CodePage));

        long chunkStart = 0;
        while (true)
        {
            var eof = n < buffer.Length; // ReadFully only returns a short chunk at the end of the stream
            transcoder.Process(buffer.AsSpan(skip, n - skip), chunkStart + skip, flush: eof);
            if (eof) break;

            chunkStart += n;
            skip = 0;
            n = ReadFully(input, buffer);
        }

        transcoder.Finish();
        return new StreamConversion(transcoder.Characters, bom, hadBom);
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

    /// <summary>Stateful bytes → chars → bytes pump. Decoder/Encoder keep partial sequences (UTF-8 multi-byte, surrogate pairs) across chunks.</summary>
    private sealed class Transcoder
    {
        private readonly Stream _output;
        private readonly EncodingEntry _source, _target;
        private readonly Decoder _decoder;
        private readonly Encoder _encoder;
        private readonly char[] _chars = new char[ChunkChars];
        private readonly byte[] _out = new byte[ChunkBytes];
        private long _lines;

        public long Characters { get; private set; }

        public Transcoder(Stream output, EncodingEntry source, EncodingEntry target, bool replace)
        {
            _output = output;
            _source = source;
            _target = target;
            _decoder = WithFallbacks(source.Encoding, EncoderFallback.ExceptionFallback,
                replace ? InvalidBytesFallback : DecoderFallback.ExceptionFallback).GetDecoder();
            _encoder = WithFallbacks(target.Encoding,
                replace ? EncoderFallback.ReplacementFallback : EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback).GetEncoder();
        }

        /// <param name="streamOffset">Offset in the input of <paramref name="bytes"/>[0], for error messages.</param>
        public void Process(ReadOnlySpan<byte> bytes, long streamOffset, bool flush)
        {
            var consumed = 0;
            while (true)
            {
                int used, produced;
                bool completed;
                try
                {
                    _decoder.Convert(bytes, _chars, flush, out used, out produced, out completed);
                }
                catch (DecoderFallbackException ex)
                {
                    var at = streamOffset + consumed + Math.Max(ex.Index, 0);
                    throw new ConversionException(
                        $"The data is not valid {_source.DisplayName} (at byte offset {at:N0}). " +
                        "Pick a different source encoding, or allow replacement of invalid bytes.",
                        ex, isDataError: true);
                }

                bytes = bytes[used..];
                consumed += used;
                if (produced > 0) Encode(_chars.AsSpan(0, produced), flush: false);

                if (completed) return;
                if (used == 0 && produced == 0) throw new InvalidOperationException("Decoder made no progress.");
            }
        }

        public void Finish() => Encode([], flush: true);

        private void Encode(ReadOnlySpan<char> text, bool flush)
        {
            var rest = text;
            while (true)
            {
                int charsUsed, bytesUsed;
                bool completed;
                try
                {
                    _encoder.Convert(rest, _out, flush, out charsUsed, out bytesUsed, out completed);
                }
                catch (EncoderFallbackException ex)
                {
                    var upTo = text.Length - rest.Length + Math.Clamp(ex.Index, 0, rest.Length);
                    var line = _lines + text[..upTo].Count('\n') + 1;
                    var ch = ex.CharUnknownHigh != '\0'
                        ? $"U+{char.ConvertToUtf32(ex.CharUnknownHigh, ex.CharUnknownLow):X4}"
                        : $"U+{(int)ex.CharUnknown:X4}";
                    throw new ConversionException(
                        $"The text contains a character ({ch}) that can't be represented in {_target.DisplayName} " +
                        $"(line {line:N0}). Choose a Unicode target, or allow replacement with '?'.",
                        ex, isDataError: true);
                }

                if (bytesUsed > 0) _output.Write(_out, 0, bytesUsed);
                rest = rest[charsUsed..];

                if (completed)
                {
                    _lines += text.Count('\n');
                    Characters += text.Length;
                    return;
                }

                if (charsUsed == 0 && bytesUsed == 0) throw new InvalidOperationException("Encoder made no progress.");
            }
        }
    }

    /// <summary>
    /// Reads <paramref name="sourcePath"/>, converts it and writes <paramref name="targetPath"/>, streaming
    /// throughout. The output goes to a temporary file that replaces the target only after the whole
    /// conversion succeeded, so a failure never leaves a half-written or destroyed target (this is also what
    /// makes converting a file onto itself safe).
    /// </summary>
    /// <exception cref="ConversionException">For anything the caller can fix (see message).</exception>
    public static ConversionResult ConvertFile(string sourcePath, string targetPath, string targetEncoding,
        ConvertOptions? options = null)
    {
        options ??= new ConvertOptions();

        if (!File.Exists(sourcePath))
            throw new ConversionException($"Source file not found: {sourcePath}");
        if (Directory.Exists(targetPath))
            throw new ConversionException($"Target is a directory, not a file: {targetPath}");

        var fullTarget = Path.GetFullPath(targetPath);
        var targetDir = Path.GetDirectoryName(fullTarget);
        if (targetDir is not null && !Directory.Exists(targetDir))
            throw new ConversionException($"Target folder does not exist: {targetDir}");
        if (File.Exists(fullTarget) && !options.Force)
            throw new ConversionException($"Target file already exists: {targetPath} (use --force to overwrite).");

        EncodingSpec targetSpec;
        EncodingSpec? sourceSpec = null;
        try
        {
            targetSpec = EncodingCatalog.Parse(targetEncoding);
            if (options.SourceEncoding is not null) sourceSpec = EncodingCatalog.Parse(options.SourceEncoding);
        }
        catch (ArgumentException ex)
        {
            throw new ConversionException(ex.Message, ex);
        }

        var detection = EncodingDetector.DetectFile(sourcePath);
        var sourceEntry = sourceSpec?.Entry ?? detection.Encoding;

        var temp = fullTarget + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            StreamConversion done;
            var replaced = false;

            while (true)
            {
                try
                {
                    done = ConvertPass(sourcePath, temp, sourceEntry, targetSpec, options, replace: replaced);
                    break;
                }
                catch (ConversionException ex) when (ex.IsDataError && options.ReplaceUnmappable && !replaced)
                {
                    replaced = true; // lossy mode: run again from the start with replacement fallbacks
                }
            }

            var bytes = new FileInfo(temp).Length;
            File.Move(temp, fullTarget, options.Force);

            return new ConversionResult(detection, sourceEntry, targetSpec.Entry, done.WroteBom, done.Characters, bytes, replaced);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static StreamConversion ConvertPass(string sourcePath, string tempPath,
        EncodingEntry source, EncodingSpec target, ConvertOptions options, bool replace)
    {
        using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1,
            FileOptions.SequentialScan);
        using var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1,
            FileOptions.SequentialScan);

        return ConvertStream(input, output, source, target.Entry,
            options.WriteBom ?? target.Bom, replace);
    }

    private static Encoding WithFallbacks(Encoding encoding, EncoderFallback enc, DecoderFallback dec)
    {
        var clone = (Encoding)encoding.Clone();
        clone.EncoderFallback = enc;
        clone.DecoderFallback = dec;
        return clone;
    }
}
