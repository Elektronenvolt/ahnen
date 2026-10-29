using Ahnen.Web.Helpers;
using Ahnen.Web.Models;

namespace Ahnen.Tests;

public class HelpersTests
{
    private static FamilyTreeData Tree() => new()
    {
        Persons = new[] { "gp", "p", "c", "other" }.Select(id => new Person { Id = id }).ToList(),
        Families =
        [
            new Family { Id = "F1", PartnerIds = ["gp"], ChildRefs = [new ChildRef { PersonId = "p" }] },
            new Family { Id = "F2", PartnerIds = ["p", "other"], ChildRefs = [new ChildRef { PersonId = "c" }] }
        ]
    };

    [Fact]
    public void AncestorsOf_returns_all_ancestors_but_not_the_person()
    {
        var data = Tree();
        Assert.Equal(new[] { "gp", "other", "p" }, data.AncestorsOf("c").Order());
        Assert.Equal(new[] { "gp" }, data.AncestorsOf("p"));
        Assert.Empty(data.AncestorsOf("gp"));
        Assert.Empty(data.AncestorsOf("unknown"));
    }

    [Fact]
    public void AncestorsOf_contains_the_person_when_ancestry_is_cyclic()
    {
        var data = Tree();
        data.Families[0].PartnerIds.Add("c"); // c becomes parent of p, who is c's parent
        Assert.Contains("c", data.AncestorsOf("c"));
    }

    [Fact]
    public void FindPersonInOwnAncestry_reports_a_cyclic_person_only_for_cycles()
    {
        var data = Tree();
        Assert.Null(data.FindPersonInOwnAncestry());

        data.Families[0].PartnerIds.Add("c");
        Assert.NotNull(data.FindPersonInOwnAncestry());
    }

    [Fact]
    public void PruneIfObsolete_removes_only_childless_families_with_at_most_one_partner()
    {
        var data = Tree();
        var empty = new Family { Id = "F3", PartnerIds = ["gp"] };
        var couple = new Family { Id = "F4", PartnerIds = ["gp", "p"] };
        data.Families.AddRange([empty, couple]);

        data.PruneIfObsolete(empty);
        data.PruneIfObsolete(couple);
        data.PruneIfObsolete(data.Families[0]); // has a child

        Assert.DoesNotContain(empty, data.Families);
        Assert.Contains(couple, data.Families);
        Assert.Equal(3, data.Families.Count);
    }

    [Fact]
    public void GetOrAddMarriage_inserts_first_once_and_reuses_existing()
    {
        var family = new Family { Id = "F", Events = [new FamilyEvent { Type = FamilyEventType.DIV }] };

        var created = family.GetOrAddMarriage();
        Assert.Same(created, family.Events[0]);
        Assert.Equal(FamilyEventType.MARR, created.Type);

        created.Date = "1950";
        Assert.Same(created, family.GetOrAddMarriage());
        Assert.Equal(2, family.Events.Count);
    }

    [Fact]
    public void Validator_rejects_ancestry_cycles_and_accepts_valid_trees()
    {
        var data = Tree();
        Ahnen.Web.Services.GedcomValidator.ValidateLegal(data);

        data.Families[0].PartnerIds.Add("c");
        var error = Assert.Throws<InvalidOperationException>(() => Ahnen.Web.Services.GedcomValidator.ValidateLegal(data));
        Assert.Contains("own ancestor", error.Message);
    }

    [Fact]
    public void JoinNonBlank_skips_null_empty_and_whitespace_parts()
    {
        Assert.Equal("a b", new string?[] { "a", null, "", "  ", "b" }.JoinNonBlank());
        Assert.Equal("a · b", new string?[] { "a", null, "b" }.JoinNonBlank(" · "));
        Assert.Equal("", new string?[] { null, " " }.JoinNonBlank());
    }

    [Theory]
    [InlineData("photos/I1_abc.webp", "photos/I1_abc_thumb.webp")]
    [InlineData("I1_abc.webp", "I1_abc_thumb.webp")]
    public void ThumbPath_follows_the_photo_naming_convention(string main, string thumb) =>
        Assert.Equal(thumb, PhotoPaths.ThumbPath(main));
}
