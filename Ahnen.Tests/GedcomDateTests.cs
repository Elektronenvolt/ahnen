using Ahnen.Web.Models;
using System.Globalization;

namespace Ahnen.Tests;

public class GedcomDateTests
{
    [Theory]
    [InlineData("1950", "1950")]
    [InlineData("04/1950", "APR 1950")]
    [InlineData("4.1950", "APR 1950")]
    [InlineData("12.04.1950", "12 APR 1950")]
    [InlineData("12/04/1950", "12 APR 1950")]
    [InlineData("1950-04-12", "12 APR 1950")]
    [InlineData("1950-04", "APR 1950")]
    [InlineData("~1950", "ABT 1950")]
    [InlineData("about 1950", "ABT 1950")]
    [InlineData("ca. 1950", "ABT 1950")]
    [InlineData("<1950", "BEF 1950")]
    [InlineData("vor 1950", "BEF 1950")]
    [InlineData(">1950", "AFT 1950")]
    [InlineData("nach 1950", "AFT 1950")]
    [InlineData("12 APR 1950", "12 APR 1950")]
    [InlineData("abt 12.04.1950", "ABT 12 APR 1950")]
    public void Normalize_understands_common_inputs(string input, string expected) =>
        Assert.Equal(expected, GedcomDate.Normalize(input));

    [Theory]
    [InlineData("nonsense")]
    [InlineData("32.13.1950")]
    [InlineData("13/44/1950")]
    public void Normalize_rejects_garbage(string input) =>
        Assert.Null(GedcomDate.Normalize(input));

    [Theory]
    [InlineData("12 APR 1950", true)]
    [InlineData("APR 1950", true)]
    [InlineData("1950", true)]
    [InlineData("ABT 1950", true)]
    [InlineData("BEF 12 APR 1950", true)]
    [InlineData("XXX 1950", false)]
    [InlineData("42 APR 1950", false)]
    [InlineData("", false)]
    public void IsValid_matches_canonical_format(string value, bool expected) =>
        Assert.Equal(expected, GedcomDate.IsValid(value));

    [Fact]
    public void Year_extracts_year() => Assert.Equal(1950, GedcomDate.Year("ABT 12 APR 1950"));

    [Fact]
    public void Display_formats_per_culture()
    {
        Assert.Equal("Apr 12, 1950", GedcomDate.Display("12 APR 1950", CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal("12. Apr. 1950", GedcomDate.Display("12 APR 1950", CultureInfo.GetCultureInfo("de-DE")).Replace("Apr", "Apr."));
        Assert.StartsWith("um 1950", GedcomDate.Display("ABT 1950", CultureInfo.GetCultureInfo("de-DE")));
        Assert.StartsWith("about 1950", GedcomDate.Display("ABT 1950", CultureInfo.GetCultureInfo("en-US")));
    }

    [Fact]
    public void Approximate_orders_dates()
    {
        var birth = GedcomDate.Approximate("12 APR 1950");
        var death = GedcomDate.Approximate("1949");
        Assert.True(birth > death);
    }
}
