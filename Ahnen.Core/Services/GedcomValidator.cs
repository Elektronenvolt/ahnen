using Ahnen.Core.Helpers;
using Ahnen.Core.Models;

namespace Ahnen.Core.Services;

/// <summary>
/// GEDCOM compliance gate: asserts that only GEDCOM 7-legal values are ever
/// persisted (binding project rule — GEDCOM compatibility overrules).
/// </summary>
public static class GedcomValidator
{
    public static void ValidateLegal(FamilyTreeData data)
    {
        var personIds = new HashSet<string>();
        foreach (var p in data.Persons)
        {
            Require(!string.IsNullOrWhiteSpace(p.Id), "Person without ID");
            Require(personIds.Add(p.Id), $"Duplicate person ID {p.Id}");
            Require(p.Names.Count > 0, $"{p.Id}: a person needs at least one NAME");
            foreach (var e in p.Events)
            {
                Require(e.Type != IndividualEventType.EVEN || !string.IsNullOrWhiteSpace(e.Value),
                    $"{p.Id}: generic EVEN requires a TYPE text");
                Require(e.Date is null || GedcomDate.IsValid(e.Date),
                    $"{p.Id}: '{e.Date}' is not a valid GEDCOM date");
            }
        }

        var familyIds = new HashSet<string>();
        foreach (var f in data.Families)
        {
            Require(!string.IsNullOrWhiteSpace(f.Id), "Family without ID");
            Require(familyIds.Add(f.Id), $"Duplicate family ID {f.Id}");
            Require(f.PartnerIds.Count <= 2, $"{f.Id}: a family has at most two partners (GEDCOM HUSB/WIFE)");
            Require(f.PartnerIds.Distinct().Count() == f.PartnerIds.Count, $"{f.Id}: duplicate partner");
            foreach (var id in f.PartnerIds)
                Require(personIds.Contains(id), $"{f.Id}: unknown partner {id}");
            foreach (var c in f.ChildRefs)
            {
                Require(personIds.Contains(c.PersonId), $"{f.Id}: unknown child {c.PersonId}");
                Require(!f.PartnerIds.Contains(c.PersonId), $"{f.Id}: {c.PersonId} cannot be partner and child of the same family");
            }
            Require(f.ChildRefs.Select(c => c.PersonId).Distinct().Count() == f.ChildRefs.Count,
                $"{f.Id}: duplicate child");
            foreach (var e in f.Events)
            {
                Require(e.Type != FamilyEventType.EVEN || !string.IsNullOrWhiteSpace(e.TypeDetail),
                    $"{f.Id}: generic EVEN requires a TYPE text");
                Require(e.Date is null || GedcomDate.IsValid(e.Date),
                    $"{f.Id}: '{e.Date}' is not a valid GEDCOM date");
            }
        }

        Require(data.RootPersonId is null || personIds.Contains(data.RootPersonId),
            $"Unknown root person {data.RootPersonId}");

        // No ancestry cycles.
        var cyclic = data.FindPersonInOwnAncestry();
        Require(cyclic is null, $"{cyclic} would be their own ancestor");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"GEDCOM validation failed: {message}");
    }
}
