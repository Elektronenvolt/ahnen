using Ahnen.Web.Helpers;
using Ahnen.Web.Models;

namespace Ahnen.Web.Services;

/// <summary>
/// Maps between the friendly "family status" dropdown and GEDCOM family events.
/// The status is never stored directly — only standard events are (binding
/// GEDCOM rule); "Separated" uses the generic EVEN + TYPE per GEDCOM 7.
/// </summary>
public static class FamilyStatus
{
    public const string SeparationType = "Separation";

    /// <summary>Status codes offered in the editor, in display order. Resource key: Fam_{code}.</summary>
    public static readonly string[] Options = ["NONE", "MARR", "ENGA", "DIV", "SEP", "ANUL", "DIVF"];

    public static string GetStatus(Family family)
    {
        bool Has(FamilyEventType t) => family.Events.Any(e => e.Type == t);
        if (family.Events.Any(e => e.Type == FamilyEventType.EVEN && e.TypeDetail == SeparationType)) return "SEP";
        if (Has(FamilyEventType.DIV)) return "DIV";
        if (Has(FamilyEventType.ANUL)) return "ANUL";
        if (Has(FamilyEventType.DIVF)) return "DIVF";
        if (Has(FamilyEventType.MARR)) return "MARR";
        if (Has(FamilyEventType.ENGA)) return "ENGA";
        return "NONE";
    }

    /// <summary>Rewrites the family's status events to represent the selected status.</summary>
    public static void ApplyStatus(Family family, string status)
    {
        family.Events.RemoveAll(e =>
            e.Type is FamilyEventType.DIV or FamilyEventType.ANUL or FamilyEventType.DIVF or FamilyEventType.ENGA
            || (e.Type == FamilyEventType.EVEN && e.TypeDetail == SeparationType));

        switch (status)
        {
            case "NONE":
                family.Events.RemoveAll(e => e.Type == FamilyEventType.MARR);
                break;
            case "MARR":
                family.GetOrAddMarriage();
                break;
            case "ENGA":
                family.Events.RemoveAll(e => e.Type == FamilyEventType.MARR);
                family.Events.Add(new FamilyEvent { Type = FamilyEventType.ENGA });
                break;
            case "DIV":
                family.GetOrAddMarriage();
                family.Events.Add(new FamilyEvent { Type = FamilyEventType.DIV });
                break;
            case "SEP":
                family.GetOrAddMarriage();
                family.Events.Add(new FamilyEvent { Type = FamilyEventType.EVEN, TypeDetail = SeparationType });
                break;
            case "ANUL":
                family.GetOrAddMarriage();
                family.Events.Add(new FamilyEvent { Type = FamilyEventType.ANUL });
                break;
            case "DIVF":
                family.GetOrAddMarriage();
                family.Events.Add(new FamilyEvent { Type = FamilyEventType.DIVF });
                break;
        }
    }
}
