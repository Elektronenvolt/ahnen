using Ahnen.Web.Models;

namespace Ahnen.Web.Helpers;

/// <summary>Structural queries and rules on the raw family tree data, shared by the store and the validator.</summary>
public static class FamilyTreeExtensions
{
    /// <summary>All ancestor IDs of a person. Contains the person itself only when the data has an ancestry cycle.</summary>
    public static HashSet<string> AncestorsOf(this FamilyTreeData data, string personId) =>
        Ancestors(FamiliesByChild(data), personId);

    /// <summary>The first person who is their own ancestor, or null when the ancestry is cycle-free.</summary>
    public static string? FindPersonInOwnAncestry(this FamilyTreeData data)
    {
        var familiesByChild = FamiliesByChild(data);
        return data.Persons.FirstOrDefault(p => Ancestors(familiesByChild, p.Id).Contains(p.Id))?.Id;
    }

    private static ILookup<string, Family> FamiliesByChild(FamilyTreeData data) =>
        data.Families
            .SelectMany(f => f.ChildRefs.Select(c => (c.PersonId, Family: f)))
            .ToLookup(x => x.PersonId, x => x.Family);

    private static HashSet<string> Ancestors(ILookup<string, Family> familiesByChild, string personId)
    {
        var result = new HashSet<string>();
        var queue = new Queue<string>([personId]);
        while (queue.Count > 0)
        {
            foreach (var parent in familiesByChild[queue.Dequeue()].SelectMany(f => f.PartnerIds))
                if (result.Add(parent))
                    queue.Enqueue(parent);
        }
        return result;
    }

    /// <summary>Removes a family that has lost its meaning: no children and at most one partner.</summary>
    public static void PruneIfObsolete(this FamilyTreeData data, Family family)
    {
        if (family.ChildRefs.Count == 0 && family.PartnerIds.Count <= 1)
            data.Families.Remove(family);
    }
}
