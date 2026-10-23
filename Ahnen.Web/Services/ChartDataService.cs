using System.Globalization;
using Ahnen.Web.Models;

namespace Ahnen.Web.Services;

/// <summary>Maps the GEDCOM-style data model to the Topola JSON input format (<c>JsonGedcomData</c>).</summary>
public class ChartDataService(FamilyTreeStore store)
{
    public record TopolaDate(string Text);

    public record TopolaEvent(TopolaDate? Date, string? Place, bool? Confirmed);

    public record TopolaImage(string Url);

    public record TopolaIndi(
        string Id,
        string? FirstName,
        string? LastName,
        string? MaidenName,
        string? Sex,
        string? Famc,
        List<string> Fams,
        TopolaEvent? Birth,
        TopolaEvent? Death,
        List<TopolaImage>? Images);

    public record TopolaFam(
        string Id,
        string? Husb,
        string? Wife,
        List<string> Children,
        TopolaEvent? Marriage);

    public record TopolaData(List<TopolaIndi> Indis, List<TopolaFam> Fams);

    /// <summary>Builds the full chart dataset. Null members are omitted when serialized for JS.</summary>
    public TopolaData Build()
    {
        var persons = store.Persons;
        var families = store.Families;
        var byId = persons.ToDictionary(p => p.Id);
        var culture = CultureInfo.CurrentUICulture;

        // A person climbs to exactly one parent family: prefer the birth family over adoptive ones.
        var famcOf = new Dictionary<string, (string FamilyId, bool Birth)>();
        foreach (var f in families)
            foreach (var c in f.ChildRefs)
            {
                var isBirth = c.Pedigree == Pedigree.BIRTH;
                if (!famcOf.TryGetValue(c.PersonId, out var current) || isBirth && !current.Birth)
                    famcOf[c.PersonId] = (f.Id, isBirth);
            }

        var indis = persons.Select(p =>
        {
            var name = p.PrimaryName;
            var maiden = p.Names.Skip(1)
                .FirstOrDefault(n => n.Type is NameType.MAIDEN or NameType.BIRTH
                                     && n.Surname.Length > 0 && n.Surname != name.Surname)?.Surname;
            return new TopolaIndi(
                p.Id,
                NullIfEmpty(Join(name.Prefix, name.Given)),
                NullIfEmpty(Join(name.Surname, name.Suffix)),
                maiden,
                p.Sex switch { Sex.M => "M", Sex.F => "F", _ => null },
                famcOf.TryGetValue(p.Id, out var famc) ? famc.FamilyId : null,
                families.Where(f => f.PartnerIds.Contains(p.Id)).Select(f => f.Id).ToList(),
                ToEvent(p.Birth, culture, confirmed: false),
                ToEvent(p.Death, culture, confirmed: true),
                p.PhotoFile is { } photo ? [new TopolaImage($"/media/{ThumbPath(photo)}")] : null);
        }).ToList();

        var fams = families.Select(f =>
        {
            var (husb, wife) = AssignPartners(f, byId);
            var children = f.ChildRefs.Select(c => c.PersonId).Where(byId.ContainsKey).ToList();
            var marriage = f.Marriage is { } m ? ToEvent(m.Date, m.Place, culture, confirmed: false) : null;
            return new TopolaFam(f.Id, husb, wife, children, marriage);
        }).ToList();

        return new TopolaData(indis, fams);
    }

    /// <summary>Topola has husband/wife slots: map by sex, falling back to partner order.</summary>
    private static (string? Husb, string? Wife) AssignPartners(Family f, Dictionary<string, Person> byId)
    {
        var ids = f.PartnerIds.Where(byId.ContainsKey).ToList();
        switch (ids.Count)
        {
            case 0:
                return (null, null);
            case 1:
                return byId[ids[0]].Sex == Sex.F ? (null, ids[0]) : (ids[0], null);
        }

        var (a, b) = (ids[0], ids[1]);
        if (byId[a].Sex == Sex.F && byId[b].Sex != Sex.F) return (b, a);
        if (byId[b].Sex == Sex.M && byId[a].Sex != Sex.M) return (b, a);
        return (a, b);
    }

    private static TopolaEvent? ToEvent(IndividualEvent? ev, CultureInfo culture, bool confirmed) =>
        ev is null ? null : ToEvent(ev.Date, ev.Place, culture, confirmed);

    private static TopolaEvent ToEvent(string? date, string? place, CultureInfo culture, bool confirmed)
    {
        var text = GedcomDate.Display(date, culture);
        return new TopolaEvent(
            text.Length > 0 ? new TopolaDate(text) : null,
            string.IsNullOrWhiteSpace(place) ? null : place,
            confirmed ? true : null);
    }

    private static string Join(params string?[] parts) =>
        string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    /// <summary>Thumbnail path convention shared with <see cref="PhotoService"/>.</summary>
    public static string ThumbPath(string mainFile)
    {
        var dir = Path.GetDirectoryName(mainFile)?.Replace('\\', '/');
        var name = Path.GetFileNameWithoutExtension(mainFile);
        var thumb = $"{name}_thumb.webp";
        return string.IsNullOrEmpty(dir) ? thumb : $"{dir}/{thumb}";
    }
}
