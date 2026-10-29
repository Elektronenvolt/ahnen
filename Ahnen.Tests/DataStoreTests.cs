using Ahnen.Core.Models;
using Ahnen.Core.Services;

namespace Ahnen.Tests;

public class DataStoreTests : IDisposable
{
    private readonly TestStore _ts = new();
    private DataStore Store => _ts.Store;

    public void Dispose() => _ts.Dispose();

    [Fact]
    public void Failed_save_rolls_back_published_state_and_keeps_disk_unchanged()
    {
        var kept = Store.AddPerson("Kept", "Person", Sex.M);
        var file = Path.Combine(_ts.ContentRootPath, "data", "familytree.json");
        var onDisk = File.ReadAllText(file);
        var changed = 0;
        Store.Changed += () => changed++;
        Directory.CreateDirectory(file + ".tmp"); // makes the temporary-file write fail

        Assert.ThrowsAny<Exception>(() => Store.AddPerson("Lost", "Person"));
        Assert.ThrowsAny<Exception>(() => Store.SetRootPerson(kept.Id));
        Assert.ThrowsAny<Exception>(() => Store.UpdateNames(kept.Id, [new PersonName { Given = "Lost" }]));

        Assert.Equal(["Kept"], Store.Persons.Select(p => p.PrimaryName.Given));
        Assert.Equal(onDisk, File.ReadAllText(file));
        Assert.Equal(0, changed);

        Directory.Delete(file + ".tmp");
        Store.AddPerson("Later", "Person");
        Assert.Equal(["Kept", "Later"], Store.Persons.Select(p => p.PrimaryName.Given));
    }

    [Fact]
    public void Failed_save_does_not_consume_the_undo_step()
    {
        Store.AddPerson("First", "Person");
        var second = Store.AddPerson("Second", "Person");
        var file = Path.Combine(_ts.ContentRootPath, "data", "familytree.json");
        Directory.CreateDirectory(file + ".tmp");

        Assert.ThrowsAny<Exception>(() => Store.AddPerson("Lost", "Person"));
        Assert.ThrowsAny<Exception>(() => Store.Undo());
        Directory.Delete(file + ".tmp");

        Assert.True(Store.Undo());
        Assert.Null(Store.GetPerson(second.Id));
        Assert.Single(Store.Persons);
    }

    [Fact]
    public void Failing_mutation_does_not_change_state()
    {
        var person = Store.AddPerson("Valid", "Person", Sex.M);

        Assert.ThrowsAny<Exception>(() => Store.UpdateFamily(new Family { Id = "F404" }));
        Assert.Single(Store.Persons);
        Assert.Equal(person.Id, Store.RootPersonId);
        Assert.Empty(Store.Families);
    }

    [Fact]
    public void AddPerson_assigns_sequential_xref_ids()
    {
        var a = Store.AddPerson("A", "X", Sex.M);
        var b = Store.AddPerson("B", "X", Sex.F);
        Assert.Equal("I1", a.Id);
        Assert.Equal("I2", b.Id);
        Assert.Equal(a.Id, Store.RootPersonId);
    }

    [Fact]
    public void AddPartner_creates_family_and_is_idempotent()
    {
        var a = Store.AddPerson("A", "X", Sex.M);
        var b = Store.AddPerson("B", "X", Sex.F);
        var f1 = Store.AddPartner(a.Id, b.Id);
        var f2 = Store.AddPartner(b.Id, a.Id);
        Assert.Equal(f1.Id, f2.Id);
        Assert.Single(Store.Families);
    }

    [Fact]
    public void AddChild_prevents_cycles()
    {
        var a = Store.AddPerson("A", "X", Sex.M);
        var b = Store.AddPerson("B", "X", Sex.M);
        Store.AddChild(a.Id, b.Id);
        Assert.Throws<InvalidOperationException>(() => Store.AddChild(b.Id, a.Id));
        Assert.Throws<InvalidOperationException>(() => Store.AddParent(a.Id, b.Id));
    }

    [Fact]
    public void DeletePerson_cleans_references_and_empty_families()
    {
        var p = TestData.BuildFamily(Store);
        Store.DeletePerson(p["wife"].Id);
        Assert.Null(Store.GetPerson(p["wife"].Id));
        Assert.DoesNotContain(Store.Families, f => f.PartnerIds.Contains(p["wife"].Id));
        // me+wife family had no children -> must be gone entirely
        Assert.DoesNotContain(Store.Families,
            f => f.PartnerIds.Count == 1 && f.PartnerIds[0] == p["me"].Id && f.ChildRefs.Count == 0);
    }

    [Fact]
    public void Undo_restores_previous_state()
    {
        var a = Store.AddPerson("A", "X", Sex.M);
        Store.DeletePerson(a.Id);
        Assert.Null(Store.GetPerson(a.Id));
        Assert.True(Store.Undo());
        Assert.NotNull(Store.GetPerson(a.Id));
    }

    [Fact]
    public void Updates_are_persisted_and_reloadable()
    {
        var a = Store.AddPerson("Anna", "Alt", Sex.F);
        a.GetOrAddEvent(IndividualEventType.BIRT).Date = "ABT 1950";
        a.Events.Add(new IndividualEvent { Type = IndividualEventType.OCCU, Value = "Bäckerin" });
        Store.UpdatePersonFacts(a);

        var reloaded = Store.GetPerson(a.Id)!;
        Assert.Equal("ABT 1950", reloaded.Birth?.Date);
        Assert.Equal("Bäckerin", reloaded.Occupation);
    }

    [Fact]
    public void Validator_rejects_illegal_gedcom_values()
    {
        var a = Store.AddPerson("A", "X", Sex.M);
        a.Events.Add(new IndividualEvent { Type = IndividualEventType.EVEN }); // EVEN without TYPE
        Assert.Throws<InvalidOperationException>(() => Store.UpdatePersonFacts(a));

        var b = Store.GetPerson(a.Id)!;
        b.Events.Add(new IndividualEvent { Type = IndividualEventType.BIRT, Date = "not a date" });
        Assert.Throws<InvalidOperationException>(() => Store.UpdatePersonFacts(b));
    }

    [Fact]
    public void Family_status_round_trips_through_gedcom_events()
    {
        var a = Store.AddPerson("A", "X", Sex.M);
        var b = Store.AddPerson("B", "X", Sex.F);
        var fam = Store.AddPartner(a.Id, b.Id);
        Assert.Equal("NONE", FamilyStatus.GetStatus(fam));

        FamilyStatus.ApplyStatus(fam, "MARR");
        Assert.Equal("MARR", FamilyStatus.GetStatus(fam));

        FamilyStatus.ApplyStatus(fam, "SEP");
        Assert.Equal("SEP", FamilyStatus.GetStatus(fam));
        Assert.Contains(fam.Events, e => e.Type == FamilyEventType.MARR); // history preserved
        Assert.Contains(fam.Events, e => e.Type == FamilyEventType.EVEN && e.TypeDetail == FamilyStatus.SeparationType);

        FamilyStatus.ApplyStatus(fam, "DIV");
        Assert.Equal("DIV", FamilyStatus.GetStatus(fam));
        Store.UpdateFamily(fam); // must pass GEDCOM validation
    }
}
