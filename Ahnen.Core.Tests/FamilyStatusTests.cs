using Ahnen.Core.Models;
using Ahnen.Core.Services;

namespace Ahnen.Core.Tests;

public class FamilyStatusTests
{
    [Theory]
    [InlineData("NONE", false)]
    [InlineData("ENGA", false)]
    [InlineData("MARR", true)]
    [InlineData("DIV", true)]
    [InlineData("SEP", true)]
    [InlineData("ANUL", true)]
    [InlineData("DIVF", true)]
    public void Repeated_status_transitions_preserve_unrelated_events_and_marriage_details(string status, bool married)
    {
        var marriage = new FamilyEvent { Type = FamilyEventType.MARR, Date = "1950", Place = "Town" };
        var other = new FamilyEvent { Type = FamilyEventType.EVEN, TypeDetail = "Reunion" };
        var family = new Family
        {
            Id = "F1",
            Events = [marriage, other, new() { Type = FamilyEventType.DIV }]
        };
        FamilyStatus.ApplyStatus(family, status);
        FamilyStatus.ApplyStatus(family, status);
        Assert.Equal(status, FamilyStatus.GetStatus(family));
        Assert.Contains(other, family.Events);
        Assert.Equal(married, family.Marriage is not null);
        if (married)
        {
            var retained = Assert.IsType<FamilyEvent>(family.Marriage);
            Assert.Same(marriage, retained);
            Assert.Equal("1950", retained.Date);
            Assert.Equal("Town", retained.Place);
        }
        Assert.Equal(1 + (married ? 1 : 0) + (status is "NONE" or "MARR" ? 0 : 1), family.Events.Count);
        GedcomValidator.ValidateLegal(new FamilyTreeData { Families = [family] });
    }

    [Fact]
    public void Status_precedence_is_independent_of_event_order()
    {
        var family = new Family();
        foreach (var (type, expected) in new[]
                 {
                     (FamilyEventType.ENGA, "ENGA"), (FamilyEventType.MARR, "MARR"),
                     (FamilyEventType.DIVF, "DIVF"), (FamilyEventType.ANUL, "ANUL"),
                     (FamilyEventType.DIV, "DIV")
                 })
        {
            family.Events.Insert(0, new FamilyEvent { Type = type });
            Assert.Equal(expected, FamilyStatus.GetStatus(family));
        }
        family.Events.Add(new FamilyEvent { Type = FamilyEventType.EVEN, TypeDetail = FamilyStatus.SeparationType });
        Assert.Equal("SEP", FamilyStatus.GetStatus(family));
    }
}
