using System.Globalization;
using System.Resources;
using Ahnen.Web;
using Ahnen.Web.Services;

namespace Ahnen.Tests;

public class ResourceKeyTests
{
    private static readonly ResourceManager Resources =
        new("Ahnen.Web.Resources.SharedResources", typeof(SharedResources).Assembly);

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void Every_family_status_option_has_a_label(string culture)
    {
        foreach (var option in FamilyStatus.Options)
            Assert.False(string.IsNullOrEmpty(Resources.GetString($"Fam_{option}", CultureInfo.GetCultureInfo(culture))),
                $"Missing resource Fam_{option} ({culture})");
    }
}
