using Ahnen.Web.Models;

namespace Ahnen.Web.Helpers;

public static class FamilyExtensions
{
    /// <summary>Returns the family's marriage event, inserting an empty one as the first event when missing.</summary>
    public static FamilyEvent GetOrAddMarriage(this Family family)
    {
        var marriage = family.Marriage;
        if (marriage is null)
        {
            marriage = new FamilyEvent { Type = FamilyEventType.MARR };
            family.Events.Insert(0, marriage);
        }
        return marriage;
    }
}
