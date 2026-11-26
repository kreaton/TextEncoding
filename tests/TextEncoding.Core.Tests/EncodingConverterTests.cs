using System.Text;

namespace TextEncoding.Core.Tests;

public class ConvertStreamTests
{
    [Theory]
    [MemberData(nameof(TestData.UnicodeFiles), MemberType = typeof(TestData))]
    public void Every_unicode_sample_converts_to_the_same_utf8(string file, string encoding)
    {
        var output = TestData.Convert(TestData.Bytes(file), encoding, "utf-8", writeBom: false, replace: false,
            out var result);

        Assert.Equal(TestData.Bytes("utf8.txt"), output);
        Assert.Equal(TestData.Unicode.Length, result.Characters);
        Assert.Equal(file.Contains("-bom"), result.SourceHadBom);
        Assert.False(result.WroteBom);
    }

    [Theory]
    [InlineData("utf-8", true, "utf8-bom.txt")]
    [InlineData("utf-16", true, "utf16le-bom.txt")]
    [InlineData("utf-16be", true, "utf16be-bom.txt")]
    [InlineData("utf-16", false, "utf16le-nobom.txt")]
    [InlineData("utf-16be", false, "utf16be-nobom.txt")]
    [InlineData("utf-32", true, "utf32le-bom.txt")]
    [InlineData("utf-32be", true, "utf32be-bom.txt")]
    public void Utf8_converts_to_each_unicode_sample(string target, bool bom, string expectedFile)
    {
        var output = TestData.Convert(TestData.Bytes("utf8.txt"), "utf-8", target, bom);

        Assert.Equal(TestData.Bytes(expectedFile), output);
    }

    [Fact]
    public void Windows1252_to_utf8_and_back()
    {
        var utf8 = TestData.Convert(TestData.Bytes("windows-1252.txt"), "windows-1252", "utf-8");
        Assert.Equal(TestData.Bytes("utf8-latin.txt"), utf8);

        var cp1252 = TestData.Convert(utf8, "utf-8", "windows-1252");
        Assert.Equal(TestData.Bytes("windows-1252.txt"), cp1252);
    }

    [Theory]
    [InlineData("iso-8859-1.txt", "iso-8859-1")]
    [InlineData("ibm850.txt", "ibm850")]
    [InlineData("windows-1252.txt", "windows-1252")] // only the Nordic lines are compared below
    public void Danish_swedish_and_german_letters_convert_from_legacy_code_pages(string file, string encoding)
    {
        var text = TestData.Decode(TestData.Convert(TestData.Bytes(file), encoding, "utf-8"), "utf-8");

        Assert.Contains(TestData.Nordic, text);
        Assert.Contains("ÆØÅ æøå", text);
        Assert.Contains("ÅÄÖ åäö", text);
        Assert.Contains("ÄÖÜ äöüß", text);
    }

    [Theory]
    [InlineData("iso-8859-1", "iso-8859-1.txt")]
    [InlineData("ibm850", "ibm850.txt")]
    public void Nordic_utf8_converts_to_legacy_code_pages(string target, string expectedFile)
    {
        var output = TestData.Convert(TestData.Bytes("utf8-nordic.txt"), "utf-8", target);

        Assert.Equal(TestData.Bytes(expectedFile), output);
    }

    [Theory]
    [InlineData("iso-8859-1", "U+201C")] // ISO-8859-1 has no curly quotes ...
    [InlineData("ibm850", "U+201C")]
    [InlineData("us-ascii", "U+00F8")]   // ... and ASCII stops at the "ø" of "Rødgrød"
    public void Windows1252_punctuation_does_not_fit_smaller_code_pages(string target, string firstMissing)
    {
        var ex = Assert.Throws<ConversionException>(() => TestData.Convert(TestData.Bytes("utf8-latin.txt"), "utf-8", target));

        Assert.Contains(firstMissing, ex.Message);
    }

    [Fact]
    public void Undefined_windows1252_bytes_map_to_c1_controls_and_round_trip()
    {
        var utf8 = TestData.Convert(TestData.Bytes("windows-1252-c1.txt"), "windows-1252", "utf-8");
        Assert.Equal(TestData.Bytes("windows-1252-c1.utf8.txt"), utf8);
        Assert.Contains('\u008F', TestData.Decode(utf8, "utf-8"));
        Assert.Contains('\u009D', TestData.Decode(utf8, "utf-8"));

        Assert.Equal(TestData.Bytes("windows-1252-c1.txt"), TestData.Convert(utf8, "utf-8", "windows-1252"));
    }

    [Fact]
    public void Empty_input_gives_empty_output_or_just_a_bom()
    {
        Assert.Empty(TestData.Convert([], "utf-8", "utf-16", writeBom: false));
        Assert.Equal(EncodingCatalog.GetBom(1200), TestData.Convert([], "utf-8", "utf-16", writeBom: true));
    }

    // ---- BOM handling ----

    [Theory]
    [InlineData("utf8-bom.txt", "utf-8", "utf-8", true)]      // UTF-8 keeps a BOM it had
    [InlineData("utf8.txt", "utf-8", "utf-8", false)]         // ... and doesn't add one
    [InlineData("utf16le-bom.txt", "utf-16", "utf-8", false)] // a UTF-16 BOM doesn't carry over to UTF-8
    [InlineData("utf8.txt", "utf-8", "utf-16", true)]         // UTF-16/32 always get one
    [InlineData("utf8.txt", "utf-8", "utf-32be", true)]
    [InlineData("utf8-latin.txt", "utf-8", "windows-1252", false)]
    public void Default_bom(string file, string source, string target, bool expectBom)
    {
        var output = TestData.Convert(TestData.Bytes(file), source, target, writeBom: null, replace: true, out var result);

        var bom = EncodingCatalog.GetBom(TestData.Enc(target).CodePage);
        Assert.Equal(expectBom, result.WroteBom);
        if (expectBom) Assert.Equal(bom, output[..bom.Length]);
        else Assert.False(bom.Length > 0 && output.AsSpan().StartsWith(bom));
    }

    [Fact]
    public void Bom_is_never_written_for_encodings_without_one()
    {
        var output = TestData.Convert(TestData.Bytes("utf8-latin.txt"), "utf-8", "windows-1252", writeBom: true,
            replace: false, out var result);

        Assert.False(result.WroteBom);
        Assert.Equal(TestData.Bytes("windows-1252.txt"), output);
    }

    [Fact]
    public void Explicit_no_bom_overrides_the_default()
    {
        var output = TestData.Convert(TestData.Bytes("utf8.txt"), "utf-8", "utf-16", writeBom: false);

        Assert.Equal(TestData.Bytes("utf16le-nobom.txt"), output);
    }

    [Fact]
    public void Only_the_bom_of_the_source_encoding_is_skipped()
    {
        // Read as Windows-1252, a UTF-8 BOM is just three characters ("ï»¿") and must be kept.
        var output = TestData.Convert(TestData.Bytes("utf8-bom.txt"), "windows-1252", "utf-8", replace: true);

        Assert.StartsWith("ï»¿Hello", TestData.Decode(output, "utf-8"));
    }

    // ---- lossy data ----

    [Fact]
    public void Unrepresentable_character_fails_with_its_code_point_and_line()
    {
        var ex = Assert.Throws<ConversionException>(() =>
            TestData.Convert(TestData.Bytes("utf8.txt"), "utf-8", "windows-1252"));

        Assert.True(ex.IsDataError);
        Assert.Contains("U+1E9E", ex.Message); // ẞ, the first character on line 6 that Windows-1252 lacks
        Assert.Contains("line 6", ex.Message);
        Assert.IsType<EncoderFallbackException>(ex.InnerException);
    }

    [Fact]
    public void Unrepresentable_characters_become_question_marks_with_replace()
    {
        var output = TestData.Convert(TestData.Bytes("utf8.txt"), "utf-8", "windows-1252", replace: true);

        Assert.Equal(TestData.Latin + TestData.UnicodeOnlyLineReplaced, TestData.Decode(output, "windows-1252"));
    }

    [Fact]
    public void Invalid_source_bytes_fail_with_their_offset()
    {
        // windows-1252.txt read as UTF-8: the first non-ASCII byte (0xF8, "ø" in "Rødgrød") is never valid UTF-8.
        var offset = TestData.Latin.IndexOf('ø'); // single-byte encoding: char index == byte offset
        var ex = Assert.Throws<ConversionException>(() =>
            TestData.Convert(TestData.Bytes("windows-1252.txt"), "utf-8", "utf-16"));

        Assert.True(ex.IsDataError);
        Assert.Contains($"byte offset {offset}", ex.Message);
        Assert.IsType<DecoderFallbackException>(ex.InnerException);
    }

    [Fact]
    public void Invalid_source_bytes_beyond_the_first_chunk_report_the_absolute_offset()
    {
        byte[] data = [.. Encoding.ASCII.GetBytes(new string('a', 100_000)), 0xFF, (byte)'b'];

        var ex = Assert.Throws<ConversionException>(() => TestData.Convert(data, "utf-8", "utf-16"));

        Assert.Contains($"byte offset {100_000:N0}", ex.Message); // the message formats with the current culture
    }

    [Fact]
    public void Invalid_source_bytes_become_replacement_characters_with_replace()
    {
        var output = TestData.Convert(TestData.Bytes("windows-1252.txt"), "utf-8", "utf-8", replace: true);
        var text = TestData.Decode(output, "utf-8");

        Assert.StartsWith("Hello, world!\r\nDansk: R�dgr�d", text);
        Assert.DoesNotContain("ø", text);
    }

    [Fact]
    public void Sequence_truncated_at_end_of_input_is_invalid()
    {
        var ex = Assert.Throws<ConversionException>(() =>
            TestData.Convert(TestData.Bytes("utf8-truncated.txt"), "utf-8", "utf-16"));
        Assert.True(ex.IsDataError);

        var replaced = TestData.Convert(TestData.Bytes("utf8-truncated.txt"), "utf-8", "utf-8", replace: true);
        Assert.Equal("Truncated: �", TestData.Decode(replaced, "utf-8"));
    }

    // ---- chunking (64 KiB byte chunks, 32 Ki char chunks) ----

    public static TheoryData<string, int> ChunkBoundaries()
    {
        var data = new TheoryData<string, int>();
        foreach (var encoding in new[] { "utf-8", "utf-16", "utf-16be", "utf-32" })
        foreach (var prefix in new[] { 16382, 16383, 16384, 32766, 32767, 32768, 65533, 65534, 65535, 65536 })
            data.Add(encoding, prefix);
        return data;
    }

    /// <summary>
    /// Puts multi-byte characters and a surrogate pair right at every chunk boundary that a source encoding can
    /// produce, then round-trips through each Unicode encoding. A lost or doubled byte would break equality.
    /// </summary>
    [Theory]
    [MemberData(nameof(ChunkBoundaries))]
    public void Characters_split_across_chunks_survive(string source, int prefix)
    {
        var text = new string('a', prefix) + "€😀Ωé\r\n" + new string('b', 70_000) + "😀end";
        var input = TestData.Encode(text, source);

        foreach (var target in new[] { "utf-8", "utf-16be", "utf-32" })
        {
            var output = TestData.Convert(input, source, target, writeBom: false, replace: false, out var result);

            Assert.Equal(TestData.Encode(text, target), output);
            Assert.Equal(text.Length, result.Characters);
        }
    }

    [Fact]
    public void Input_that_trickles_in_converts_the_same()
    {
        var input = TestData.Bytes("utf16be-bom.txt");
        using var output = new MemoryStream();

        var result = EncodingConverter.ConvertStream(new TrickleStream(input, maxRead: 3), output,
            TestData.Enc("utf-16be"), EncodingCatalog.Utf8, writeBom: false, replace: false);

        Assert.True(result.SourceHadBom); // the BOM is split over two reads
        Assert.Equal(TestData.Bytes("utf8.txt"), output.ToArray());
    }

    [Fact]
    public void Large_input_converts_in_constant_chunks()
    {
        var line = TestData.Unicode;
        var text = string.Concat(Enumerable.Repeat(line, 20_000)); // ~2 MB of UTF-8
        var input = Encoding.UTF8.GetBytes(text);

        var output = TestData.Convert(input, "utf-8", "utf-16", writeBom: false, replace: false, out var result);

        Assert.Equal(Encoding.Unicode.GetBytes(text), output);
        Assert.Equal(text.Length, result.Characters);
    }

    [Fact]
    public void Error_line_is_counted_across_chunks()
    {
        var text = string.Concat(Enumerable.Repeat("line\n", 50_000)) + "Ω";

        var ex = Assert.Throws<ConversionException>(() =>
            TestData.Convert(Encoding.UTF8.GetBytes(text), "utf-8", "us-ascii"));

        Assert.Contains("U+03A9", ex.Message);
        Assert.Contains($"line {50_001:N0})", ex.Message);
    }

    // ---- helpers ----

    [Theory]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, 0x41 }, "utf-8", 3)]
    [InlineData(new byte[] { 0xEF, 0xBB }, "utf-8", 0)]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x41, 0x00 }, "utf-16", 2)]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x41, 0x00 }, "utf-16be", 0)]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x00, 0x00 }, "utf-32", 4)]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF }, "windows-1252", 0)]
    public void GetBomLength(byte[] data, string encoding, int expected) =>
        Assert.Equal(expected, EncodingConverter.GetBomLength(data, TestData.Enc(encoding)));

    [Theory]
    [InlineData("utf8-bom.txt", "utf-8", true)]
    [InlineData("utf8.txt", "utf-8", false)]
    [InlineData("utf16be-bom.txt", "utf-16be", true)]
    [InlineData("utf16be-bom.txt", "utf-16", false)]
    [InlineData("empty.txt", "utf-8", false)]
    [InlineData("utf8-bom.txt", "windows-1252", false)]
    public void HasBom(string file, string encoding, bool expected) =>
        Assert.Equal(expected, EncodingConverter.HasBom(TestData.PathOf(file), TestData.Enc(encoding)));
}

public class OpenReaderTests
{
    [Theory]
    [MemberData(nameof(TestData.UnicodeFiles), MemberType = typeof(TestData))]
    public void Reads_the_text_without_the_bom(string file, string encoding)
    {
        using var reader = EncodingConverter.OpenReader(File.OpenRead(TestData.PathOf(file)), TestData.Enc(encoding),
            replaceInvalid: false, out var hadBom);

        Assert.Equal(file.Contains("-bom"), hadBom);
        Assert.Equal(TestData.Unicode, reader.ReadToEnd());
    }

    [Fact]
    public void Reads_lines()
    {
        using var reader = EncodingConverter.OpenReader(File.OpenRead(TestData.PathOf("windows-1252.txt")),
            TestData.Enc("windows-1252"), replaceInvalid: false, out _);

        var lines = new List<string>();
        while (reader.ReadLine() is { } line) lines.Add(line);

        Assert.Equal(TestData.Latin.Split("\r\n")[..^1], lines);
        Assert.Contains("Svenska: Smörgåsbord och köttbullar i Göteborg. ÅÄÖ åäö", lines);
    }

    [Fact]
    public void Invalid_bytes_in_the_first_buffer_throw_a_conversion_exception()
    {
        var ex = Assert.Throws<ConversionException>(() => EncodingConverter.OpenReader(
            File.OpenRead(TestData.PathOf("windows-1252.txt")), EncodingCatalog.Utf8, replaceInvalid: false, out _));

        Assert.True(ex.IsDataError);
    }

    [Fact]
    public void Invalid_bytes_become_replacement_characters_when_allowed()
    {
        using var reader = EncodingConverter.OpenReader(File.OpenRead(TestData.PathOf("windows-1252.txt")),
            EncodingCatalog.Utf8, replaceInvalid: true, out var hadBom);

        Assert.False(hadBom);
        Assert.Contains('�', reader.ReadToEnd());
    }

    [Fact]
    public void LeaveOpen_keeps_the_stream_usable()
    {
        var stream = new MemoryStream(TestData.Bytes("utf8.txt"));

        using (EncodingConverter.OpenReader(stream, EncodingCatalog.Utf8, false, out _, leaveOpen: true)) { }

        Assert.True(stream.CanRead);
    }
}

public sealed class ConvertFileTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Detects_the_source_and_converts()
    {
        var target = _dir.File("out.txt");

        var result = EncodingConverter.ConvertFile(TestData.PathOf("utf16le-bom.txt"), target, "utf-8");

        Assert.Equal(TestData.Bytes("utf8.txt"), File.ReadAllBytes(target));
        Assert.Equal("utf-16", result.Source.Encoding.Name);
        Assert.True(result.Source.HasBom);
        Assert.Same(result.Source.Encoding, result.SourceEncoding);
        Assert.Equal("utf-8", result.Target.Name);
        Assert.False(result.WroteBom);
        Assert.False(result.HadReplacements);
        Assert.Equal(TestData.Unicode.Length, result.Characters);
        Assert.Equal(new FileInfo(target).Length, result.BytesWritten);
    }

    [Fact]
    public void Bom_suffix_on_the_target_name_writes_a_bom()
    {
        var target = _dir.File("out.txt");

        var result = EncodingConverter.ConvertFile(TestData.PathOf("utf32be-bom.txt"), target, "utf-8-bom");

        Assert.True(result.WroteBom);
        Assert.Equal(TestData.Bytes("utf8-bom.txt"), File.ReadAllBytes(target));
    }

    [Fact]
    public void WriteBom_option_wins_over_the_name_suffix()
    {
        var target = _dir.File("out.txt");

        EncodingConverter.ConvertFile(TestData.PathOf("utf8.txt"), target, "utf-16-bom",
            new ConvertOptions { WriteBom = false });

        Assert.Equal(TestData.Bytes("utf16le-nobom.txt"), File.ReadAllBytes(target));
    }

    [Fact]
    public void Source_encoding_option_overrides_detection()
    {
        var target = _dir.File("out.txt");

        var result = EncodingConverter.ConvertFile(TestData.PathOf("windows-1252.txt"), target, "utf-8",
            new ConvertOptions { SourceEncoding = "cp1252" });

        Assert.Equal("windows-1252", result.SourceEncoding.Name);
        Assert.Equal(DetectionConfidence.Low, result.Source.Confidence); // detection still reported
        Assert.Equal(TestData.Bytes("utf8-latin.txt"), File.ReadAllBytes(target));
    }

    [Fact]
    public void Existing_target_is_not_overwritten_without_force()
    {
        var target = _dir.File("out.txt");
        File.WriteAllText(target, "original");

        var ex = Assert.Throws<ConversionException>(() =>
            EncodingConverter.ConvertFile(TestData.PathOf("utf8.txt"), target, "utf-16"));

        Assert.False(ex.IsDataError);
        Assert.Equal("original", File.ReadAllText(target));
    }

    [Fact]
    public void Force_overwrites_the_target()
    {
        var target = _dir.File("out.txt");
        File.WriteAllText(target, "original");

        EncodingConverter.ConvertFile(TestData.PathOf("utf8.txt"), target, "utf-16be-bom", new ConvertOptions { Force = true });

        Assert.Equal(TestData.Bytes("utf16be-bom.txt"), File.ReadAllBytes(target));
    }

    [Fact]
    public void Converts_a_file_onto_itself()
    {
        var file = _dir.Copy("utf32le-bom.txt");

        EncodingConverter.ConvertFile(file, file, "utf-8", new ConvertOptions { Force = true });

        Assert.Equal(TestData.Bytes("utf8.txt"), File.ReadAllBytes(file));
        Assert.Single(Directory.GetFiles(_dir.Path));
    }

    [Fact]
    public void Failed_conversion_leaves_the_target_untouched_and_no_temp_file()
    {
        var target = _dir.File("out.txt");
        File.WriteAllText(target, "original");

        var ex = Assert.Throws<ConversionException>(() => EncodingConverter.ConvertFile(
            TestData.PathOf("utf8.txt"), target, "windows-1252", new ConvertOptions { Force = true }));

        Assert.True(ex.IsDataError);
        Assert.Equal("original", File.ReadAllText(target));
        Assert.Equal([target], Directory.GetFiles(_dir.Path));
    }

    [Fact]
    public void Failed_in_place_conversion_keeps_the_source()
    {
        var file = _dir.Copy("utf8.txt");

        Assert.Throws<ConversionException>(() =>
            EncodingConverter.ConvertFile(file, file, "us-ascii", new ConvertOptions { Force = true }));

        Assert.Equal(TestData.Bytes("utf8.txt"), File.ReadAllBytes(file));
        Assert.Single(Directory.GetFiles(_dir.Path));
    }

    [Fact]
    public void ReplaceUnmappable_retries_lossily_and_reports_it()
    {
        var target = _dir.File("out.txt");

        var result = EncodingConverter.ConvertFile(TestData.PathOf("utf8.txt"), target, "windows-1252",
            new ConvertOptions { ReplaceUnmappable = true });

        Assert.True(result.HadReplacements);
        Assert.Equal(TestData.Latin + TestData.UnicodeOnlyLineReplaced, TestData.Decode(File.ReadAllBytes(target), "windows-1252"));
    }

    [Fact]
    public void ReplaceUnmappable_is_not_reported_when_nothing_was_replaced()
    {
        var result = EncodingConverter.ConvertFile(TestData.PathOf("utf8-latin.txt"), _dir.File("out.txt"),
            "windows-1252", new ConvertOptions { ReplaceUnmappable = true });

        Assert.False(result.HadReplacements);
    }

    [Fact]
    public void Empty_file_converts_to_empty_or_bom()
    {
        EncodingConverter.ConvertFile(TestData.PathOf("empty.txt"), _dir.File("a.txt"), "utf-8");
        EncodingConverter.ConvertFile(TestData.PathOf("empty.txt"), _dir.File("b.txt"), "utf-16");

        Assert.Empty(File.ReadAllBytes(_dir.File("a.txt")));
        Assert.Equal(EncodingCatalog.GetBom(1200), File.ReadAllBytes(_dir.File("b.txt")));
    }

    [Fact]
    public void Unknown_encoding_is_a_conversion_exception()
    {
        var ex = Assert.Throws<ConversionException>(() =>
            EncodingConverter.ConvertFile(TestData.PathOf("utf8.txt"), _dir.File("out.txt"), "klingon"));
        Assert.Contains("klingon", ex.Message);

        Assert.Throws<ConversionException>(() => EncodingConverter.ConvertFile(TestData.PathOf("utf8.txt"),
            _dir.File("out.txt"), "utf-8", new ConvertOptions { SourceEncoding = "klingon" }));

        Assert.Empty(Directory.GetFiles(_dir.Path));
    }

    [Fact]
    public void Missing_source_is_a_conversion_exception() =>
        Assert.Throws<ConversionException>(() =>
            EncodingConverter.ConvertFile(_dir.File("nope.txt"), _dir.File("out.txt"), "utf-8"));

    [Fact]
    public void Missing_target_folder_is_a_conversion_exception() =>
        Assert.Throws<ConversionException>(() =>
            EncodingConverter.ConvertFile(TestData.PathOf("utf8.txt"), _dir.File(Path.Combine("sub", "out.txt")), "utf-8"));

    [Fact]
    public void Directory_target_is_a_conversion_exception() =>
        Assert.Throws<ConversionException>(() =>
            EncodingConverter.ConvertFile(TestData.PathOf("utf8.txt"), _dir.Path, "utf-8", new ConvertOptions { Force = true }));
}

public class DefaultBomTests
{
    [Theory]
    [InlineData(true, "utf-8", "utf-8", true)]
    [InlineData(false, "utf-8", "utf-8", false)]
    [InlineData(true, "utf-16", "utf-8", false)]
    [InlineData(false, "windows-1252", "utf-16", true)]
    [InlineData(false, "utf-8", "utf-32be", true)]
    [InlineData(true, "utf-8", "windows-1252", false)]
    public void DefaultBom(bool sourceHadBom, string source, string target, bool expected) =>
        Assert.Equal(expected, EncodingConverter.DefaultBom(sourceHadBom, TestData.Enc(source), TestData.Enc(target)));
}
