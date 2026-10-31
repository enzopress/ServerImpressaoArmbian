using PrintServer.Web.Cups;

namespace PrintServer.Tests;

public class LpArgumentsTests
{
    [Fact]
    public void Builds_basic_arguments()
    {
        var args = LpArguments.Build("HP135a", "/tmp/a.pdf", new PrintOptions("contrato.pdf", Copies: 2));

        Assert.Equal(["-d", "HP135a", "-n", "2", "-t", "contrato.pdf", "/tmp/a.pdf"], args);
    }

    [Fact]
    public void Adds_page_ranges_and_fit_to_page()
    {
        var args = LpArguments.Build("HP135a", "/tmp/a.png", new PrintOptions("foto", PageRanges: "1-3, 5", FitToPage: true));

        Assert.Equal(["-d", "HP135a", "-n", "1", "-t", "foto", "-P", "1-3,5", "-o", "fit-to-page", "/tmp/a.png"], args);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("1-3")]
    [InlineData("1,3,5-7")]
    [InlineData(" 2 - 4 ")]
    [InlineData(null)]
    [InlineData("")]
    public void Valid_page_ranges(string? value) => Assert.True(LpArguments.IsValidPageRanges(value));

    [Theory]
    [InlineData("1-3,a")]
    [InlineData("; rm -rf /")]
    [InlineData("-o raw")]
    [InlineData("1,,2")]
    [InlineData("12345")]
    public void Invalid_page_ranges_are_rejected(string value)
    {
        Assert.False(LpArguments.IsValidPageRanges(value));
        Assert.Throws<ArgumentException>(() =>
            LpArguments.Build("HP135a", "/tmp/a.pdf", new PrintOptions("x", PageRanges: value)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void Copies_out_of_range_are_rejected(int copies)
    {
        Assert.Throws<ArgumentException>(() =>
            LpArguments.Build("HP135a", "/tmp/a.pdf", new PrintOptions("x", Copies: copies)));
    }

    [Fact]
    public void Title_drops_control_characters_and_is_truncated()
    {
        Assert.Equal("ab", LpArguments.SanitizeTitle("a\nb\u0007"));
        Assert.Equal("Documento", LpArguments.SanitizeTitle("  \t "));
        Assert.Equal(100, LpArguments.SanitizeTitle(new string('x', 300)).Length);
    }
}
