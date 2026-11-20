using Ahnen.Core.Models;
using System.Globalization;

namespace Ahnen.Core.Tests;

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
    [InlineData("um 04.1950", "ABT APR 1950")]
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

    [Theory]
    [InlineData("12 APR 1950", "12.04.1950")]
    [InlineData("1 JAN 42", "01.01.0042")]
    [InlineData("APR 1950", "04.1950")]
    [InlineData("JAN 42", "01.0042")]
    [InlineData("1 JAN 0", "01.01.0000")]
    [InlineData("JAN 0", "01.0000")]
    [InlineData("1950", "1950")]
    [InlineData("42", "0042")]
    public void Display_formats_numeric_dates_and_round_trips(string canonical, string expected)
    {
        foreach (var culture in new[] { "en-US", "de-DE" })
        {
            var display = GedcomDate.Display(canonical, CultureInfo.GetCultureInfo(culture));
            Assert.Equal(expected, display);
            Assert.Equal(display, GedcomDate.Display(GedcomDate.Normalize(display), CultureInfo.GetCultureInfo(culture)));
        }
    }

    [Theory]
    [InlineData("ABT", "about", "um")]
    [InlineData("BEF", "before", "vor")]
    [InlineData("AFT", "after", "nach")]
    public void Display_qualifiers_round_trip_at_each_precision(string qualifier, string english, string german)
    {
        foreach (var date in new[] { "12 APR 1950", "APR 1950", "1950" })
        {
            foreach (var (culture, prefix) in new[] { ("en-US", english), ("de-DE", german) })
            {
                var canonical = $"{qualifier} {date}";
                var display = GedcomDate.Display(canonical, CultureInfo.GetCultureInfo(culture));
                Assert.StartsWith(prefix + " ", display);
                Assert.Equal(canonical, GedcomDate.Normalize(display));
            }
        }
    }

    [Fact]
    public void Approximate_orders_dates()
    {
        var birth = GedcomDate.Approximate("12 APR 1950");
        var death = GedcomDate.Approximate("1949");
        Assert.True(birth > death);
    }
}
