namespace TextEncoding.Core.Tests;

public class EncodingCatalogTests
{
    [Theory]
    [InlineData("utf-8", "utf-8", null)]
    [InlineData("UTF8", "utf-8", null)]
    [InlineData("  utf-8  ", "utf-8", null)]
    [InlineData("utf-8-bom", "utf-8", true)]
    [InlineData("utf-8-SIG", "utf-8", true)]
    [InlineData("utf-16-nobom", "utf-16", false)]
    [InlineData("unicode", "utf-16", null)]
    [InlineData("utf-16le", "utf-16", null)]
    [InlineData("ucs-2be", "utf-16be", null)]
    [InlineData("utf32", "utf-32", null)]
    [InlineData("ascii", "us-ascii", null)]
    [InlineData("latin1", "iso-8859-1", null)]
    [InlineData("cp1252", "windows-1252", null)]
    [InlineData("1252", "windows-1252", null)]
    [InlineData("cp1251", "windows-1251", null)]
    [InlineData("sjis", "shift_jis", null)]
    [InlineData("gbk", "gb2312", null)]
    public void Parse_resolves_names_aliases_and_bom_suffixes(string spec, string expectedName, bool? expectedBom)
    {
        var parsed = EncodingCatalog.Parse(spec);

        Assert.Equal(expectedName, parsed.Entry.Name);
        Assert.Equal(expectedBom, parsed.Bom);
    }

    [Theory]
    [InlineData("ansi")]
    [InlineData("DEFAULT")]
    public void Ansi_resolves_to_the_system_code_page(string name) =>
        Assert.Same(EncodingCatalog.SystemAnsi, EncodingCatalog.Resolve(name));

    [Theory]
    [InlineData("klingon")]
    [InlineData("-bom")]
    [InlineData("cp99999")]
    public void Parse_rejects_unknown_names(string spec) =>
        Assert.Throws<ArgumentException>(() => EncodingCatalog.Parse(spec));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("klingon")]
    public void Resolve_returns_null_for_unknown_names(string name) => Assert.Null(EncodingCatalog.Resolve(name));

    [Fact]
    public void Resolve_accepts_code_pages_that_are_not_in_the_list()
    {
        var entry = EncodingCatalog.Resolve("cp855"); // OEM Cyrillic, not one of the offered entries

        Assert.NotNull(entry);
        Assert.Equal(855, entry.CodePage);
    }

    [Theory]
    [InlineData(65001, new byte[] { 0xEF, 0xBB, 0xBF })]
    [InlineData(1200, new byte[] { 0xFF, 0xFE })]
    [InlineData(1201, new byte[] { 0xFE, 0xFF })]
    [InlineData(12000, new byte[] { 0xFF, 0xFE, 0x00, 0x00 })]
    [InlineData(12001, new byte[] { 0x00, 0x00, 0xFE, 0xFF })]
    [InlineData(1252, new byte[0])]
    [InlineData(20127, new byte[0])]
    public void GetBom_returns_the_conventional_byte_order_mark(int codePage, byte[] expected) =>
        Assert.Equal(expected, EncodingCatalog.GetBom(codePage));

    [Fact]
    public void Entries_never_emit_a_bom_themselves()
    {
        foreach (var entry in EncodingCatalog.All)
            Assert.Empty(entry.Encoding.GetPreamble());
    }

    [Fact]
    public void All_lists_unicode_first_with_unique_names()
    {
        var names = EncodingCatalog.All.Select(e => e.Name).ToList();

        Assert.Equal(["utf-8", "utf-16", "utf-16be", "utf-32", "utf-32be"], names.Take(5));
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("windows-1252", names);
    }

    [Theory]
    [InlineData("utf-8", true)]
    [InlineData("utf-16be", true)]
    [InlineData("utf-32", true)]
    [InlineData("us-ascii", false)]
    [InlineData("windows-1252", false)]
    public void SupportsBom_only_for_unicode(string name, bool expected) =>
        Assert.Equal(expected, TestData.Enc(name).SupportsBom);
}
