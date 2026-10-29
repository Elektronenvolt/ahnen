using Ahnen.Core.Models;
using Ahnen.Core.Services;

namespace Ahnen.Core.Tests;

public class GedcomValidatorTests
{
    private static FamilyTreeData ValidTree() => new()
    {
        RootPersonId = "I1",
        Persons = [new() { Id = "I1" }, new() { Id = "I2" }, new() { Id = "I3" }],
        Families = [new() { Id = "F1", PartnerIds = ["I1", "I2"], ChildRefs = [new() { PersonId = "I3" }] }]
    };

    [Fact]
    public void Empty_and_valid_trees_are_accepted()
    {
        GedcomValidator.ValidateLegal(new FamilyTreeData());
        GedcomValidator.ValidateLegal(ValidTree());
    }

    public static TheoryData<Action<FamilyTreeData>, string> InvalidTrees => new()
    {
        { d => d.Persons[0].Id = " ", "Person without ID" },
        { d => d.Persons[1].Id = "I1", "Duplicate person ID" },
        { d => d.Persons[0].Names.Clear(), "at least one NAME" },
        { d => d.Families[0].Id = "", "Family without ID" },
        { d => d.Families.Add(new() { Id = "F1" }), "Duplicate family ID" },
        { d => d.Families[0].PartnerIds.Add("I3"), "at most two partners" },
        { d => d.Families[0].PartnerIds[1] = "I1", "duplicate partner" },
        { d => d.Families[0].PartnerIds[1] = "unknown", "unknown partner" },
        { d => d.Families[0].ChildRefs[0].PersonId = "unknown", "unknown child" },
        { d => d.Families[0].ChildRefs[0].PersonId = "I1", "cannot be partner and child" },
        { d => d.Families[0].ChildRefs.Add(new() { PersonId = "I3" }), "duplicate child" },
        { d => d.RootPersonId = "unknown", "Unknown root person" },
        { d => d.Persons[0].Events.Add(new() { Type = IndividualEventType.EVEN }), "generic EVEN requires" },
        { d => d.Persons[0].Events.Add(new() { Type = IndividualEventType.BIRT, Date = "invalid" }), "not a valid GEDCOM date" },
        { d => d.Families[0].Events.Add(new() { Type = FamilyEventType.EVEN }), "generic EVEN requires" },
        { d => d.Families[0].Events.Add(new() { Type = FamilyEventType.MARR, Date = "invalid" }), "not a valid GEDCOM date" }
    };

    [Theory]
    [MemberData(nameof(InvalidTrees))]
    public void Invalid_structure_and_events_are_rejected(Action<FamilyTreeData> invalidate, string message)
    {
        var tree = ValidTree();
        invalidate(tree);
        Assert.Contains(message, Assert.Throws<InvalidOperationException>(() => GedcomValidator.ValidateLegal(tree)).Message);
    }
}
