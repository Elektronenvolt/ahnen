using System.Globalization;
using Ahnen.Web.Models;
using Ahnen.Web.Services;

namespace Ahnen.Tests;

public class ChartDataServiceTests : IDisposable
{
    private readonly TestStore _ts = new();
    private DataStore Store => _ts.Store;

    public void Dispose() => _ts.Dispose();

    private ChartDataService.TopolaData Build() => new ChartDataService(Store).Build();

    [Fact]
    public void Maps_family_links_and_partner_slots()
    {
        var p = TestData.BuildFamily(Store);
        var data = Build();

        var me = data.Indis.Single(i => i.Id == p["me"].Id);
        var fatherFam = data.Fams.Single(f => f.Id == me.Famc);
        Assert.Equal(p["father"].Id, fatherFam.Husb);
        Assert.Equal(p["mother"].Id, fatherFam.Wife);
        Assert.Contains(p["me"].Id, fatherFam.Children);

        var myOwnFam = data.Fams.Single(f => me.Fams.Contains(f.Id));
        Assert.Equal(p["wife"].Id, myOwnFam.Wife);
        Assert.Equal("M", me.Sex);
    }

    [Fact]
    public void Same_sex_partners_still_get_both_slots()
    {
        var a = Store.AddPerson("A", "X", Sex.M);
        var b = Store.AddPerson("B", "X", Sex.M);
        var family = Store.AddPartner(a.Id, b.Id);

        var fam = Build().Fams.Single(f => f.Id == family.Id);
        Assert.Equal(a.Id, fam.Husb);
        Assert.Equal(b.Id, fam.Wife);
    }

    [Fact]
    public void Female_first_partner_is_mapped_to_wife()
    {
        var w = Store.AddPerson("W", "X", Sex.F);
        var h = Store.AddPerson("H", "X", Sex.M);
        var family = Store.AddPartner(w.Id, h.Id);

        var fam = Build().Fams.Single(f => f.Id == family.Id);
        Assert.Equal(h.Id, fam.Husb);
        Assert.Equal(w.Id, fam.Wife);
    }

    [Fact]
    public void Multiple_marriages_are_all_listed()
    {
        var man = Store.AddPerson("Man", "X", Sex.M);
        var w1 = Store.AddPerson("W1", "X", Sex.F);
        var w2 = Store.AddPerson("W2", "X", Sex.F);
        var f1 = Store.AddPartner(man.Id, w1.Id);
        var f2 = Store.AddPartner(man.Id, w2.Id);

        var indi = Build().Indis.Single(i => i.Id == man.Id);
        Assert.Equal([f1.Id, f2.Id], indi.Fams.Order());
    }

    [Fact]
    public void Birth_family_is_preferred_over_adoptive_family()
    {
        var birthDad = Store.AddPerson("Birth", "Dad", Sex.M);
        var adoptDad = Store.AddPerson("Adopt", "Dad", Sex.M);
        var child = Store.AddPerson("Kid", "X", Sex.F);
        var adoptive = Store.AddChild(adoptDad.Id, child.Id, Pedigree.ADOPTED);
        var birth = Store.AddChild(birthDad.Id, child.Id, Pedigree.BIRTH);

        var indi = Build().Indis.Single(i => i.Id == child.Id);
        Assert.NotEqual(adoptive.Id, indi.Famc);
        Assert.Equal(birth.Id, indi.Famc);
    }

    [Fact]
    public void Single_parent_family_has_only_one_slot_filled()
    {
        var mum = Store.AddPerson("Mum", "X", Sex.F);
        var kid = Store.AddPerson("Kid", "X", Sex.M);
        var family = Store.AddChild(mum.Id, kid.Id);

        var fam = Build().Fams.Single(f => f.Id == family.Id);
        Assert.Null(fam.Husb);
        Assert.Equal(mum.Id, fam.Wife);
    }

    [Fact]
    public void Dates_places_and_death_confirmation_are_mapped()
    {
        var person = Store.AddPerson("Anna", "Alt", Sex.F);
        person.GetOrAddEvent(IndividualEventType.BIRT).Date = "ABT 1950";
        person.GetOrAddEvent(IndividualEventType.BIRT).Place = "Berlin";
        person.GetOrAddEvent(IndividualEventType.DEAT);
        Store.UpdatePersonFacts(person);

        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
        try
        {
            var indi = Build().Indis.Single(i => i.Id == person.Id);
            Assert.Equal("um 1950", indi.Birth!.Date!.Text);
            Assert.Equal("Berlin", indi.Birth.Place);
            Assert.Null(indi.Birth.Confirmed);
            Assert.True(indi.Death!.Confirmed);
            Assert.Null(indi.Death.Date);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void Missing_events_are_null_and_name_parts_are_split()
    {
        var person = Store.AddPerson("Max", "Muster", Sex.U);
        var indi = Build().Indis.Single(i => i.Id == person.Id);

        Assert.Null(indi.Birth);
        Assert.Null(indi.Death);
        Assert.Null(indi.Sex);
        Assert.Null(indi.Images);
        Assert.Equal("Max", indi.FirstName);
        Assert.Equal("Muster", indi.LastName);
    }

    [Fact]
    public void Family_status_marriage_year_and_non_birth_pedigree_are_exposed()
    {
        var dad = Store.AddPerson("Dad", "X", Sex.M);
        var mum = Store.AddPerson("Mum", "X", Sex.F);
        var kid = Store.AddPerson("Kid", "X", Sex.F);
        var adopted = Store.AddPerson("Adopted", "X", Sex.M);
        var family = Store.AddPartner(dad.Id, mum.Id);
        Store.AddChild(dad.Id, kid.Id, familyId: family.Id);
        Store.AddChild(dad.Id, adopted.Id, Pedigree.ADOPTED, family.Id);
        family = Store.GetFamily(family.Id)!;
        FamilyStatus.ApplyStatus(family, "DIV");
        family.Marriage!.Date = "5 MAY 1980";
        Store.UpdateFamily(family);

        var fam = Build().Fams.Single(f => f.Id == family.Id);
        Assert.Equal("DIV", fam.Status);
        Assert.Equal(1980, fam.MarriageYear);
        Assert.Equal(new Dictionary<string, string> { [adopted.Id] = "ADOPTED" }, fam.Pedigrees);
    }

    [Fact]
    public void Birth_year_and_deceased_flag_are_exposed()
    {
        var person = Store.AddPerson("Anna", "Alt", Sex.F);
        person.GetOrAddEvent(IndividualEventType.BIRT).Date = "3 MAR 1901";
        person.GetOrAddEvent(IndividualEventType.DEAT);
        Store.UpdatePersonFacts(person);
        var other = Store.AddPerson("Bob", "Alt", Sex.M);

        var data = Build();
        var indi = data.Indis.Single(i => i.Id == person.Id);
        Assert.Equal(1901, indi.BirthYear);
        Assert.True(indi.Deceased);
        var living = data.Indis.Single(i => i.Id == other.Id);
        Assert.Null(living.BirthYear);
        Assert.Null(living.Deceased);
    }

    [Fact]
    public void Maiden_name_comes_from_a_different_birth_name()
    {
        var person = Store.AddPerson("Eva", "Meier", Sex.F);
        Store.UpdateNames(person.Id,
        [
            new PersonName { Type = NameType.MARRIED, Given = "Eva", Surname = "Meier" },
            new PersonName { Type = NameType.BIRTH, Given = "Eva", Surname = "Schmidt" }
        ]);

        var indi = Build().Indis.Single(i => i.Id == person.Id);
        Assert.Equal("Meier", indi.LastName);
        Assert.Equal("Schmidt", indi.MaidenName);
    }
}
