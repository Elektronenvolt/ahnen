using System.Text.Json.Serialization;
using Ahnen.Web.Helpers;

namespace Ahnen.Web.Models;

/// <summary>A personal name; maps to GEDCOM NAME with TYPE and name pieces.</summary>
public class PersonName
{
    public NameType Type { get; set; } = NameType.BIRTH;
    public string Given { get; set; } = "";
    public string Surname { get; set; } = "";
    public string? Prefix { get; set; }   // NPFX
    public string? Suffix { get; set; }   // NSFX
    public string? Nickname { get; set; } // NICK

    [JsonIgnore]
    public string Full => new[] { Prefix, Given, Surname, Suffix }.JoinNonBlank();
}

/// <summary>Structured address; maps to GEDCOM ADDR (ADR1/ADR2, CITY, POST, CTRY).</summary>
public class Address
{
    public List<string> Lines { get; set; } = [];
    public string? City { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }

    [JsonIgnore]
    public bool IsEmpty => Lines.Count == 0 && string.IsNullOrWhiteSpace(City)
        && string.IsNullOrWhiteSpace(PostalCode) && string.IsNullOrWhiteSpace(Country);

    [JsonIgnore]
    public string Summary
    {
        get
        {
            string?[] parts = [.. Lines, new[] { PostalCode, City }.JoinNonBlank(), Country];
            return parts.JoinNonBlank(", ");
        }
    }
}

/// <summary>Media reference; maps to GEDCOM OBJE/FILE. Path is relative to the data directory.</summary>
public class MediaRef
{
    public string File { get; set; } = "";
    public string? Title { get; set; }
}

/// <summary>An individual event; maps to GEDCOM event tags with DATE/PLAC.</summary>
public class IndividualEvent
{
    public IndividualEventType Type { get; set; }
    /// <summary>GEDCOM EVEN.TYPE free text; required when Type is EVEN. Also used for OCCU/EDUC descriptions.</summary>
    public string? Value { get; set; }
    /// <summary>Canonical GEDCOM date string (see <see cref="GedcomDate"/>).</summary>
    public string? Date { get; set; }
    public string? Place { get; set; }
}

/// <summary>A family event; maps to GEDCOM family event tags with DATE/PLAC.</summary>
public class FamilyEvent
{
    public FamilyEventType Type { get; set; }
    /// <summary>GEDCOM EVEN.TYPE free text; required when Type is EVEN.</summary>
    public string? TypeDetail { get; set; }
    public string? Date { get; set; }
    public string? Place { get; set; }
}

/// <summary>Child link inside a family; exports as CHIL + INDI.FAMC.PEDI.</summary>
public class ChildRef
{
    public string PersonId { get; set; } = "";
    public Pedigree Pedigree { get; set; } = Pedigree.BIRTH;
}

/// <summary>An individual; maps to a GEDCOM INDI record.</summary>
public class Person
{
    /// <summary>Xref-style ID, e.g. "I1" (exports as @I1@).</summary>
    public string Id { get; set; } = "";
    public List<PersonName> Names { get; set; } = [new()];
    public Sex Sex { get; set; } = Sex.U;
    public List<IndividualEvent> Events { get; set; } = [];
    public Address Address { get; set; } = new();
    public List<string> Notes { get; set; } = [];
    public List<MediaRef> Media { get; set; } = [];

    [JsonIgnore] public PersonName PrimaryName => Names.FirstOrDefault() ?? new PersonName();
    [JsonIgnore] public string DisplayName => PrimaryName.Full is { Length: > 0 } f ? f : "?";
    [JsonIgnore] public IndividualEvent? Birth => Events.FirstOrDefault(e => e.Type == IndividualEventType.BIRT);
    [JsonIgnore] public IndividualEvent? Death => Events.FirstOrDefault(e => e.Type == IndividualEventType.DEAT);
    [JsonIgnore] public string? Occupation => Events.FirstOrDefault(e => e.Type == IndividualEventType.OCCU)?.Value;
    [JsonIgnore] public string? PhotoFile => Media.FirstOrDefault()?.File;
    [JsonIgnore] public bool IsDeceased => Death is not null;

    [JsonIgnore]
    public string LifeYears
    {
        get
        {
            var b = GedcomDate.Year(Birth?.Date);
            var d = GedcomDate.Year(Death?.Date);
            if (b is null && d is null) return "";
            return $"{b?.ToString() ?? "?"}–{(Death is not null ? d?.ToString() ?? "?" : "")}".TrimEnd('–');
        }
    }

    public IndividualEvent GetOrAddEvent(IndividualEventType type)
    {
        var ev = Events.FirstOrDefault(e => e.Type == type);
        if (ev is null)
        {
            ev = new IndividualEvent { Type = type };
            Events.Add(ev);
        }
        return ev;
    }
}

/// <summary>A family unit (partners + children); maps to a GEDCOM FAM record.</summary>
public class Family
{
    /// <summary>Xref-style ID, e.g. "F1" (exports as @F1@).</summary>
    public string Id { get; set; } = "";
    /// <summary>One or two partner person IDs (exports as HUSB/WIFE by sex).</summary>
    public List<string> PartnerIds { get; set; } = [];
    public List<FamilyEvent> Events { get; set; } = [];
    public List<ChildRef> ChildRefs { get; set; } = [];

    [JsonIgnore]
    public FamilyEvent? Marriage => Events.FirstOrDefault(e => e.Type == FamilyEventType.MARR);

    /// <summary>Latest status-defining event (MARR, DIV, ANUL, ENGA …) or null for unmarried partners.</summary>
    [JsonIgnore]
    public FamilyEvent? LatestStatusEvent => Events
        .Where(e => e.Type != FamilyEventType.EVEN || !string.IsNullOrEmpty(e.TypeDetail))
        .OrderBy(e => GedcomDate.Approximate(e.Date) ?? DateTime.MinValue)
        .LastOrDefault();

    public string? OtherPartner(string personId) =>
        PartnerIds.FirstOrDefault(id => id != personId);
}

/// <summary>Root document of the JSON data file. Everything maps 1:1 to GEDCOM 7.</summary>
public class FamilyTreeData
{
    public int SchemaVersion { get; set; } = 1;
    /// <summary>App metadata (exports as extension tag _ROOT).</summary>
    public string? RootPersonId { get; set; }
    public List<Person> Persons { get; set; } = [];
    public List<Family> Families { get; set; } = [];
}
