namespace TextEncoding.Core.Tests;

public sealed class TestFileGeneratorTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private static TestFileSpec Spec(string encoding, bool bom = false) => new(TestData.Enc(encoding), bom);

    [Fact]
    public void Writes_the_same_files_as_the_checked_in_samples()
    {
        TestFileSpec[] specs =
        [
            Spec("utf-8"), Spec("utf-8", bom: true),
            Spec("utf-16", bom: true), Spec("utf-16be", bom: true), Spec("utf-16"), Spec("utf-16be"),
            Spec("utf-32", bom: true), Spec("utf-32be", bom: true),
        ];

        var results = TestFileGenerator.Generate(_dir.Path, "s", TestData.Unicode, specs);

        string[] samples =
        [
            "utf8.txt", "utf8-bom.txt", "utf16le-bom.txt", "utf16be-bom.txt", "utf16le-nobom.txt", "utf16be-nobom.txt",
            "utf32le-bom.txt", "utf32be-bom.txt",
        ];
        Assert.Equal(samples.Length, results.Count);
        for (var i = 0; i < samples.Length; i++)
        {
            Assert.Equal(TestFileOutcome.Written, results[i].Outcome);
            Assert.Equal(TestData.Bytes(samples[i]), File.ReadAllBytes(results[i].Path));
            Assert.Equal(new FileInfo(results[i].Path).Length, results[i].Bytes);
        }
    }

    [Theory]
    [InlineData("windows-1252", "windows-1252.txt", "Latin")]
    [InlineData("iso-8859-1", "iso-8859-1.txt", "Nordic")]
    [InlineData("ibm850", "ibm850.txt", "Nordic")]
    public void Writes_legacy_code_pages_like_the_samples(string encoding, string sample, string text)
    {
        var content = text == "Latin" ? TestData.Latin : TestData.Nordic;

        var result = Assert.Single(TestFileGenerator.Generate(_dir.Path, "s", content, [Spec(encoding)]));

        Assert.Equal(TestData.Bytes(sample), File.ReadAllBytes(result.Path));
    }

    [Fact]
    public void Generated_files_are_detected_as_what_they_are()
    {
        var results = TestFileGenerator.Generate(_dir.Path, "s", TestFileGenerator.SampleText,
            TestFileGenerator.DefaultSpecs);

        foreach (var r in results.Where(r => r.Spec.Encoding.SupportsBom))
        {
            var detected = EncodingDetector.DetectFile(r.Path);
            Assert.Equal(r.Spec.Encoding.Name, detected.Encoding.Name);
            Assert.Equal(r.Spec.Bom, detected.HasBom);
        }
    }

    [Fact]
    public void File_names_use_the_encoding_name_and_bom_suffix()
    {
        Assert.Equal("sample-utf-8.txt", TestFileGenerator.FileName("sample", Spec("utf-8")));
        Assert.Equal("sample-utf-16be-bom.txt", TestFileGenerator.FileName("sample", Spec("utf-16be", bom: true)));
        Assert.Equal("x-windows-1252.txt", TestFileGenerator.FileName("x", Spec("windows-1252")));
    }

    [Fact]
    public void Sample_text_has_danish_swedish_and_german_letters_and_fits_western_code_pages()
    {
        foreach (var c in "ÆØÅæøåÄÖäöÜüß")
            Assert.Contains(c, TestFileGenerator.SampleText);

        foreach (var encoding in new[] { "windows-1252", "iso-8859-1", "iso-8859-15", "ibm850" })
            Assert.Empty(TestFileGenerator.FindUnmappable(TestFileGenerator.SampleText, TestData.Enc(encoding)));
    }

    [Fact]
    public void FindUnmappable_lists_distinct_code_points_in_order()
    {
        var missing = TestFileGenerator.FindUnmappable("aẞbΩ😀ẞ😀ø", TestData.Enc("windows-1252"));

        Assert.Equal(["ẞ", "Ω", "😀"], missing);
        Assert.Equal(["ø"], TestFileGenerator.FindUnmappable("Rødgrød", TestData.Enc("us-ascii")));
        Assert.Empty(TestFileGenerator.FindUnmappable(TestData.Unicode, TestData.Enc("utf-32be")));
    }

    [Fact]
    public void Unmappable_text_is_skipped_by_default()
    {
        var results = TestFileGenerator.Generate(_dir.Path, "s", TestData.Unicode,
            [Spec("utf-8"), Spec("windows-1252"), Spec("us-ascii")]);

        Assert.Equal(TestFileOutcome.Written, results[0].Outcome);
        Assert.Equal(TestFileOutcome.Skipped, results[1].Outcome);
        Assert.Equal(["ẞ", "Ω", "😀"], results[1].Unmappable);
        Assert.Equal(TestFileOutcome.Skipped, results[2].Outcome);
        Assert.False(File.Exists(results[1].Path));
        Assert.Single(Directory.GetFiles(_dir.Path));
    }

    [Fact]
    public void Unmappable_text_can_be_replaced()
    {
        var result = Assert.Single(TestFileGenerator.Generate(_dir.Path, "s", TestData.Unicode, [Spec("windows-1252")],
            new TestFileOptions { Unmappable = UnmappablePolicy.Replace }));

        Assert.Equal(TestFileOutcome.WrittenWithReplacements, result.Outcome);
        Assert.Equal(TestData.Latin + TestData.UnicodeOnlyLineReplaced,
            TestData.Decode(File.ReadAllBytes(result.Path), "windows-1252"));
    }

    [Fact]
    public void Bom_is_ignored_for_encodings_without_one()
    {
        var result = Assert.Single(TestFileGenerator.Generate(_dir.Path, "s", TestData.Latin, [Spec("windows-1252", bom: true)]));

        Assert.Equal(TestData.Bytes("windows-1252.txt"), File.ReadAllBytes(result.Path));
    }

    [Theory]
    [InlineData("a\r\nb\nc\rd", LineEndings.CrLf, "a\r\nb\r\nc\r\nd")]
    [InlineData("a\r\nb\nc\rd", LineEndings.Lf, "a\nb\nc\nd")]
    [InlineData("no breaks", LineEndings.CrLf, "no breaks")]
    public void Line_endings_are_normalized(string text, LineEndings endings, string expected)
    {
        Assert.Equal(expected, TestFileGenerator.NormalizeLineEndings(text, endings));

        var result = Assert.Single(TestFileGenerator.Generate(_dir.Path, "s", text, [Spec("utf-8")],
            new TestFileOptions { LineEndings = endings }));
        Assert.Equal(expected, File.ReadAllText(result.Path));
    }

    [Fact]
    public void Creates_the_folder()
    {
        var folder = _dir.File(Path.Combine("new", "nested"));

        TestFileGenerator.Generate(folder, "s", "x", [Spec("utf-8")]);

        Assert.True(File.Exists(Path.Combine(folder, "s-utf-8.txt")));
    }

    [Fact]
    public void Existing_files_block_the_whole_set_unless_overwrite()
    {
        var existing = _dir.File("s-utf-16.txt");
        File.WriteAllText(existing, "original");

        var ex = Assert.Throws<ConversionException>(() =>
            TestFileGenerator.Generate(_dir.Path, "s", "new", [Spec("utf-8"), Spec("utf-16")]));
        Assert.Contains("s-utf-16.txt", ex.Message);
        Assert.Equal([existing], Directory.GetFiles(_dir.Path)); // utf-8 wasn't written either
        Assert.Equal("original", File.ReadAllText(existing));

        TestFileGenerator.Generate(_dir.Path, "s", "new", [Spec("utf-8"), Spec("utf-16")],
            new TestFileOptions { Overwrite = true });
        Assert.Equal("new", File.ReadAllText(existing, System.Text.Encoding.Unicode));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad/name")]
    [InlineData("bad:name")]
    public void Rejects_invalid_base_names(string baseName) =>
        Assert.Throws<ConversionException>(() => TestFileGenerator.Generate(_dir.Path, baseName, "x", [Spec("utf-8")]));

    [Fact]
    public void Rejects_an_empty_or_duplicate_selection()
    {
        Assert.Throws<ConversionException>(() => TestFileGenerator.Generate(_dir.Path, "s", "x", []));
        Assert.Throws<ConversionException>(() => TestFileGenerator.Generate(_dir.Path, "s", "x", [Spec("utf-8"), Spec("utf8")]));
        Assert.Empty(Directory.GetFiles(_dir.Path));
    }

    [Fact]
    public void Spec_lists_cover_every_encoding_with_bom_variants_for_unicode()
    {
        var all = TestFileGenerator.AllSpecs;

        Assert.Equal(EncodingCatalog.All.Count + EncodingCatalog.All.Count(e => e.SupportsBom), all.Count);
        Assert.Equal(all.Count, all.Select(s => s.Suffix).Distinct().Count());
        Assert.All(TestFileGenerator.DefaultSpecs, s => Assert.Contains(s, all));
        Assert.Contains(Spec("utf-8"), TestFileGenerator.DefaultSpecs);
        Assert.Contains(Spec("utf-8", bom: true), TestFileGenerator.DefaultSpecs);
        Assert.Contains(Spec("ibm850"), TestFileGenerator.DefaultSpecs);
        Assert.DoesNotContain(Spec("us-ascii"), TestFileGenerator.DefaultSpecs);
    }
}
