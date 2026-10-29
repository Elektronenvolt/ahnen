using System.Diagnostics;
using Ahnen.Core.Models;
using Ahnen.Desktop.Visualization;
using Xunit.Abstractions;

namespace Ahnen.Desktop.Tests;

public class TreeLayoutTests(ITestOutputHelper output)
{
    public static FamilyTreeData Fixture() => new()
    {
        RootPersonId = "C",
        Persons = new[] { "A", "B", "C", "D", "E", "F", "G", "H", "isolated" }
            .Select(id => new Person { Id = id, Names = [new() { Given = id }] }).ToList(),
        Families =
        [
            new() { Id = "parents", PartnerIds = ["A", "B"], ChildRefs = [new() { PersonId = "C" }, new() { PersonId = "D" }],
                Events = [new() { Type = FamilyEventType.MARR }] },
            new() { Id = "first", PartnerIds = ["C", "E"], ChildRefs = [new() { PersonId = "F", Pedigree = Pedigree.ADOPTED }],
                Events = [new() { Type = FamilyEventType.DIV }] },
            new() { Id = "second", PartnerIds = ["C", "G"], ChildRefs = [new() { PersonId = "H" }] }
        ]
    };

    [Theory]
    [InlineData(TreeOrientation.Vertical)]
    [InlineData(TreeOrientation.Horizontal)]
    public void Overview_retains_every_person_family_and_typed_link_without_overlapping_cards(TreeOrientation orientation)
    {
        var data = Fixture();
        var scene = TreeLayout.Build(data, new("C", Overview: true, Orientation: orientation));

        Assert.Equal(data.Persons.Select(p => p.Id).Order(), scene.Cards.Select(c => c.Person.Id).Order());
        Assert.Equal(data.Families.Select(f => f.Id).Order(), scene.Junctions.Select(j => j.FamilyId).Order());
        Assert.Equal(data.Families.Sum(f => f.PartnerIds.Count + f.ChildRefs.Count), scene.Links.Count);
        Assert.All(scene.Cards, card => Assert.Same(card, scene.HitTest(card.Bounds.Center)));
        for (var i = 0; i < scene.Cards.Count; i++)
            for (var j = i + 1; j < scene.Cards.Count; j++)
                Assert.False(scene.Cards[i].Bounds.Intersects(scene.Cards[j].Bounds));
        Assert.Contains(scene.Links, l => l.FamilyId == "parents" && l.PersonId == "A" && l.Style == TreeLinkStyle.Married);
        Assert.Contains(scene.Links, l => l.FamilyId == "first" && l.PersonId == "C" && l.Style == TreeLinkStyle.Ended);
        Assert.Contains(scene.Links, l => l.FamilyId == "first" && l.PersonId == "F" && l.Style == TreeLinkStyle.Adopted);
        Assert.Contains(scene.Links, l => l.FamilyId == "second" && l.PersonId == "G" && l.Style == TreeLinkStyle.Unmarried);
        Assert.Single(scene.Cards, c => c.Person.Id == "C");
    }

    [Theory]
    [InlineData(TreeChart.Ancestors, "A", "F")]
    [InlineData(TreeChart.Descendants, "F", "A")]
    [InlineData(TreeChart.Hourglass, "A", "isolated")]
    [InlineData(TreeChart.Relatives, "D", "isolated")]
    public void Chart_modes_include_the_expected_branch_and_exclude_unrelated_people(TreeChart chart, string included, string excluded)
    {
        var scene = TreeLayout.Build(Fixture(), new("C", Chart: chart));

        Assert.Contains(scene.Cards, c => c.Person.Id == included);
        Assert.DoesNotContain(scene.Cards, c => c.Person.Id == excluded);
    }

    [Fact]
    public void Generation_limits_and_collapse_bound_the_visible_branch()
    {
        var data = Fixture();
        data.Persons.Add(new Person { Id = "grandchild" });
        data.Families.Add(new() { Id = "next", PartnerIds = ["H"], ChildRefs = [new() { PersonId = "grandchild" }] });
        var limited = TreeLayout.Build(data, new("C", TreeChart.Descendants, Generations: 1));
        var all = TreeLayout.Build(data, new("C", TreeChart.Descendants, Generations: 0));
        var collapsed = TreeLayout.Build(data, new("C", TreeChart.Descendants, Generations: 0, Collapsed: new HashSet<string> { "H" }));

        Assert.DoesNotContain(limited.Cards, c => c.Person.Id == "grandchild");
        Assert.Contains(all.Cards, c => c.Person.Id == "grandchild");
        Assert.DoesNotContain(collapsed.Cards, c => c.Person.Id == "grandchild");
        Assert.True(Assert.Single(collapsed.Cards, c => c.Person.Id == "H").Collapsed);
    }

    [Fact]
    public void Shorter_partnership_paths_do_not_hide_children_inside_the_generation_limit()
    {
        var data = new FamilyTreeData
        {
            RootPersonId = "R",
            Persons = new[] { "R", "S", "T", "X", "Y" }.Select(id => new Person { Id = id }).ToList(),
            Families =
            [
                new() { Id = "child", PartnerIds = ["R"], ChildRefs = [new() { PersonId = "X" }] },
                new() { Id = "rs", PartnerIds = ["R", "S"] },
                new() { Id = "st", PartnerIds = ["S", "T"] },
                new() { Id = "tx", PartnerIds = ["T", "X"] },
                new() { Id = "grandchild", PartnerIds = ["X"], ChildRefs = [new() { PersonId = "Y" }] }
            ]
        };
        var scene = TreeLayout.Build(data, new("R", TreeChart.Descendants, Generations: 1));

        Assert.Equal(5, scene.Cards.Count);
        Assert.Contains(scene.Cards, c => c.Person.Id == "Y");
    }

    [Fact]
    public void Focus_does_not_change_root_generation_labels_or_input_data()
    {
        var data = Fixture();
        var first = TreeLayout.Build(data, new("C", Overview: true));
        var second = TreeLayout.Build(data, new("A", Overview: true));

        Assert.Equal(first.Cards.Select(c => (c.Person.Id, c.Generation, c.Bounds)), second.Cards.Select(c => (c.Person.Id, c.Generation, c.Bounds)));
        Assert.Equal(0, Assert.Single(first.Cards, c => c.Person.Id == "C").Generation);
        Assert.Equal(1, Assert.Single(first.Cards, c => c.Person.Id == "A").Generation);
        Assert.Equal(-1, Assert.Single(first.Cards, c => c.Person.Id == "F").Generation);
        Assert.Equal("C", data.RootPersonId);
        Assert.Equal(9, data.Persons.Count);
    }

    [Fact]
    public void Pedigree_collapse_and_cross_generation_links_are_retained_with_a_warning()
    {
        var data = Fixture();
        data.Families.Add(new() { Id = "cross", PartnerIds = ["A", "E"], ChildRefs = [new() { PersonId = "H" }] });
        var scene = TreeLayout.Build(data, new("C", Overview: true));

        Assert.True(scene.GenerationConflicts > 0);
        Assert.Equal(9, scene.Cards.Count);
        Assert.Equal(13, scene.Links.Count);
        Assert.Contains(scene.Junctions, j => j.FamilyId == "cross");
        Assert.Equal(4, data.Families.Count);
    }

    [Fact]
    public void Viewport_index_and_hit_testing_return_only_intersecting_cards()
    {
        var scene = TreeLayout.Build(Fixture(), new("C", Overview: true));
        var card = scene.Cards[0];

        Assert.Equal([card], scene.InViewport(card.Bounds));
        Assert.Null(scene.HitTest(new(-100, -100)));
        Assert.Empty(scene.InViewport(new(-500, -500, 10, 10)));
    }

    [Fact]
    public void Empty_document_and_parentless_families_are_supported()
    {
        Assert.Empty(TreeLayout.Build(new(), new(null)).Cards);
        var data = Fixture();
        data.Families.Add(new() { Id = "parentless", ChildRefs = [new() { PersonId = "isolated" }] });
        var scene = TreeLayout.Build(data, new("C", Overview: true));

        Assert.Contains(scene.Links, l => l.FamilyId == "parentless" && l.PersonId == "isolated");
    }

    [Theory]
    [InlineData(200)]
    [InlineData(1000)]
    [InlineData(10000)]
    public void Synthetic_documents_keep_all_people_and_links_and_produce_deterministic_layouts(int count)
    {
        var data = Synthetic(count);
        var watch = Stopwatch.StartNew();
        var scene = TreeLayout.Build(data, new("P0", Overview: true));
        output.WriteLine($"{count} people: layout {watch.Elapsed.TotalMilliseconds:F1} ms; {scene.Links.Count} links.");
        var repeated = TreeLayout.Build(data, new("P0", Overview: true));

        Assert.Equal(count, scene.Cards.Count);
        Assert.Equal(count - 1, scene.Junctions.Count);
        Assert.Equal(2 * (count - 1), scene.Links.Count);
        Assert.Equal(scene.Cards.Select(c => (c.Person.Id, c.Bounds)), repeated.Cards.Select(c => (c.Person.Id, c.Bounds)));
        Assert.Same(scene.Cards[^1], scene.HitTest(scene.Cards[^1].Bounds.Center));
    }

    public static FamilyTreeData Synthetic(int count) => new()
    {
        RootPersonId = "P0",
        Persons = Enumerable.Range(0, count).Select(i => new Person { Id = "P" + i }).ToList(),
        Families = Enumerable.Range(1, count - 1).Select(i => new Family
        {
            Id = "F" + i, PartnerIds = ["P" + ((i - 1) / 2)], ChildRefs = [new() { PersonId = "P" + i }]
        }).ToList()
    };

    [Fact]
    public void Cancelled_layout_does_not_publish_a_partial_scene()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => TreeLayout.Build(Fixture(), new("C"), cancellation.Token));
    }
}
