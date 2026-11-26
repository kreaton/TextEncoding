using System.Text;

namespace TextEncoding.Core.Tests;

public class EncodingDetectorTests
{
    [Theory]
    [InlineData("ascii.txt", "us-ascii", false, 0, DetectionConfidence.High)]
    [InlineData("utf8.txt", "utf-8", false, 0, DetectionConfidence.High)]
    [InlineData("utf8-latin.txt", "utf-8", false, 0, DetectionConfidence.High)]
    [InlineData("utf8-nordic.txt", "utf-8", false, 0, DetectionConfidence.High)]
    [InlineData("utf8-bom.txt", "utf-8", true, 3, DetectionConfidence.Certain)]
    [InlineData("utf16le-bom.txt", "utf-16", true, 2, DetectionConfidence.Certain)]
    [InlineData("utf16be-bom.txt", "utf-16be", true, 2, DetectionConfidence.Certain)]
    [InlineData("utf16le-nobom.txt", "utf-16", false, 0, DetectionConfidence.High)]
    [InlineData("utf16be-nobom.txt", "utf-16be", false, 0, DetectionConfidence.High)]
    [InlineData("utf32le-bom.txt", "utf-32", true, 4, DetectionConfidence.Certain)]
    [InlineData("utf32be-bom.txt", "utf-32be", true, 4, DetectionConfidence.Certain)]
    [InlineData("empty.txt", "utf-8", false, 0, DetectionConfidence.Low)]
    [InlineData("windows-1252-c1.utf8.txt", "utf-8", false, 0, DetectionConfidence.High)]
    public void DetectFile_identifies_the_samples(string file, string encoding, bool hasBom, int bomLength,
        DetectionConfidence confidence)
    {
        var result = EncodingDetector.DetectFile(TestData.PathOf(file));

        Assert.Equal(encoding, result.Encoding.Name);
        Assert.Equal(hasBom, result.HasBom);
        Assert.Equal(bomLength, result.BomLength);
        Assert.Equal(confidence, result.Confidence);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    [Theory]
    [InlineData("windows-1252.txt")]
    [InlineData("windows-1252-c1.txt")]
    [InlineData("iso-8859-1.txt")]
    [InlineData("ibm850.txt")]
    [InlineData("utf8-truncated.txt")] // a sequence cut off at EOF is invalid, even though every byte so far was fine
    public void Invalid_utf8_falls_back_to_the_ansi_code_page_with_low_confidence(string file)
    {
        var result = EncodingDetector.DetectFile(TestData.PathOf(file));

        Assert.Same(EncodingCatalog.SystemAnsi, result.Encoding);
        Assert.False(result.HasBom);
        Assert.Equal(DetectionConfidence.Low, result.Confidence);
    }

    public static TheoryData<string> AllSamples => new(
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "testdata"), "*.txt").Select(Path.GetFileName)!);

    [Theory]
    [MemberData(nameof(AllSamples))]
    public void Span_stream_and_trickling_stream_detection_agree(string file)
    {
        var bytes = TestData.Bytes(file);

        var fromSpan = EncodingDetector.Detect(bytes.AsSpan());
        var fromStream = EncodingDetector.Detect(new MemoryStream(bytes));
        var fromTrickle = EncodingDetector.Detect(new TrickleStream(bytes));

        Assert.Equal(fromSpan, fromStream);
        Assert.Equal(fromSpan, fromTrickle);
    }

    [Fact]
    public void Detect_restores_the_position_of_a_seekable_stream()
    {
        var prefix = "junk"u8.ToArray();
        var stream = new MemoryStream([.. prefix, .. TestData.Bytes("utf16be-bom.txt")]);
        stream.Position = prefix.Length;

        var result = EncodingDetector.Detect(stream);

        Assert.Equal("utf-16be", result.Encoding.Name); // detection starts at the current position
        Assert.Equal(prefix.Length, stream.Position);
    }

    [Fact]
    public void Nul_bytes_in_ascii_are_reported_as_possibly_binary()
    {
        // Long enough that one NUL doesn't look like a UTF-16 pattern (in "abc\0def" it would: 1 of 3 pairs).
        var result = EncodingDetector.Detect(Encoding.ASCII.GetBytes("Some text\0 with a NUL in the middle."));

        Assert.Equal("us-ascii", result.Encoding.Name);
        Assert.Contains("NUL", result.Reason);
    }

    [Fact]
    public void Utf8_found_only_after_the_first_chunk_is_still_detected()
    {
        var text = new string('a', 200_000) + "æøå";
        var result = EncodingDetector.Detect(new MemoryStream(Encoding.UTF8.GetBytes(text)));

        Assert.Equal("utf-8", result.Encoding.Name);
        Assert.Equal(DetectionConfidence.High, result.Confidence);
    }

    [Fact]
    public void Invalid_byte_after_the_first_chunk_is_found()
    {
        byte[] data = [.. Encoding.UTF8.GetBytes(new string('a', 200_000) + "æ"), 0xFF, (byte)'z'];

        var result = EncodingDetector.Detect(new MemoryStream(data));

        Assert.Same(EncodingCatalog.SystemAnsi, result.Encoding);
    }

    [Theory]
    [InlineData(65535)] // "€" (3 bytes) starts 1 byte before the 64 KiB chunk boundary
    [InlineData(65534)]
    [InlineData(65533)] // ends exactly on the boundary
    public void Multibyte_sequence_across_a_chunk_boundary_is_valid_utf8(int prefix)
    {
        var data = Encoding.UTF8.GetBytes(new string('a', prefix) + "€ and more");

        var result = EncodingDetector.Detect(new MemoryStream(data));

        Assert.Equal("utf-8", result.Encoding.Name);
    }

    [Fact]
    public void Description_mentions_the_bom()
    {
        Assert.Equal("Unicode (UTF-8) with BOM", EncodingDetector.DetectFile(TestData.PathOf("utf8-bom.txt")).Description);
        Assert.Equal("Unicode (UTF-8)", EncodingDetector.DetectFile(TestData.PathOf("utf8.txt")).Description);
    }
}
