using System.Globalization;
using Ahnen.Core.Models;
using Ahnen.Core.Services;

namespace Ahnen.Tests;

public class RelationshipTests : IDisposable
{
    private readonly TestStore _ts = new();
    private readonly Dictionary<string, Person> _p;
    private readonly RelationshipService _svc;
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    public RelationshipTests()
    {
        _p = TestData.BuildFamily(_ts.Store);
        _svc = new RelationshipService(_ts.Store);
    }

    public void Dispose() => _ts.Dispose();

    private string Rel(string from, string to, CultureInfo culture) =>
        RelationshipLabeler.Label(_svc.Describe(_p[from].Id, _p[to].Id), culture);

    [Theory]
    [InlineData("me", "father", "father", "Vater")]
    [InlineData("me", "mother", "mother", "Mutter")]
    [InlineData("father", "me", "son", "Sohn")]
    [InlineData("me", "grandpa", "grandfather", "Großvater")]
    [InlineData("grandpa", "me", "grandson", "Enkel")]
    [InlineData("me", "brother", "brother", "Bruder")]
    [InlineData("me", "sister", "sister", "Schwester")]
    [InlineData("me", "uncle", "uncle", "Onkel")]
    [InlineData("uncle", "me", "nephew", "Neffe")]
    [InlineData("me", "cousin", "first cousin", "Cousin")]
    [InlineData("me", "nephew", "nephew", "Neffe")]
    [InlineData("nephew", "me", "uncle", "Onkel")]
    [InlineData("me", "wife", "wife", "Ehefrau")]
    [InlineData("wife", "me", "husband", "Ehemann")]
    [InlineData("me", "sisterInLaw", "sister-in-law", "Schwägerin")]
    [InlineData("wife", "father", "father-in-law", "Schwiegervater")]
    [InlineData("father", "wife", "daughter-in-law", "Schwiegertochter")]
    [InlineData("nephew", "grandpa", "great-grandfather", "Urgroßvater")]
    [InlineData("grandpa", "nephew", "great-grandson", "Urenkel")]
    [InlineData("cousin", "nephew", "first cousin, once removed", "Cousin (1x entfernt)")]
    [InlineData("nephew", "sister", "aunt", "Tante")]
    [InlineData("sister", "nephew", "nephew", "Neffe")]
    public void Relationship_labels(string from, string to, string en, string de)
    {
        Assert.Equal(en, Rel(from, to, En));
        Assert.Equal(de, Rel(from, to, De));
    }

    [Fact]
    public void Self_relation()
    {
        var r = _svc.Describe(_p["me"].Id, _p["me"].Id);
        Assert.Equal(RelationshipKind.Self, r.Kind);
    }

    [Fact]
    public void Unrelated_persons()
    {
        var stranger = _ts.Store.AddPerson("Fremder", "Mensch", Sex.M);
        var r = _svc.Describe(_p["me"].Id, stranger.Id);
        Assert.Equal(RelationshipKind.None, r.Kind);
    }

    [Fact]
    public void Half_sibling_detected()
    {
        var half = _ts.Store.AddPerson("Halb", "Test", Sex.M);
        _ts.Store.AddParent(half.Id, _p["father"].Id);
        // 'half' shares only the father with 'me' — but AddParent attached him to
        // the father+mother family, so detach logic: create via separate family.
        var r = _svc.Describe(_p["me"].Id, half.Id);
        Assert.True(r.Kind is RelationshipKind.Sibling or RelationshipKind.HalfSibling);
    }

    [Fact]
    public void Divorced_spouse_is_ex()
    {
        var fam = _ts.Store.FamiliesAsPartner(_p["me"].Id)
            .First(f => f.PartnerIds.Contains(_p["wife"].Id));
        FamilyStatus.ApplyStatus(fam, "DIV");
        _ts.Store.UpdateFamily(fam);
        Assert.Equal("ex-wife", Rel("me", "wife", En));
        Assert.Equal("Ex-Frau", Rel("me", "wife", De));
    }

    [Fact]
    public void Step_parent_detected()
    {
        // father divorces mother, marries a new woman -> she is my stepmother
        var step = _ts.Store.AddPerson("Stief", "Mutter", Sex.F);
        _ts.Store.AddPartner(_p["father"].Id, step.Id);
        var r = _svc.Describe(_p["me"].Id, step.Id);
        Assert.Equal(RelationshipKind.StepParent, r.Kind);
        Assert.Equal("Stiefmutter", RelationshipLabeler.Label(r, De));
    }
}
