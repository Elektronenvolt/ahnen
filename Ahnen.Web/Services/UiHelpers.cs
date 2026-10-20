using Ahnen.Web.Models;

namespace Ahnen.Web.Services;

/// <summary>Small display helpers shared by the pages.</summary>
public static class UiHelpers
{
    public static string Initials(Person p)
    {
        var n = p.PrimaryName;
        var initials = $"{n.Given.FirstOrDefault()}{n.Surname.FirstOrDefault()}".Trim('\0');
        return initials.Length > 0 ? initials.ToUpperInvariant() : "?";
    }
}
