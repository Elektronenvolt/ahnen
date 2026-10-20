using System.Text.Json;
using System.Text.Json.Serialization;
using Ahnen.Web.Models;

namespace Ahnen.Web.Services;

/// <summary>Maps the GEDCOM-style data model to the family-chart datum format.</summary>
public class ChartDataService(FamilyTreeStore store)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public record ChartDatum(
        string Id,
        Dictionary<string, object?> Data,
        ChartRels Rels);

    public record ChartRels(List<string> Parents, List<string> Spouses, List<string> Children);

    /// <summary>Builds the full chart dataset. Serialized to JSON for the JS module.</summary>
    public List<ChartDatum> Build()
    {
        var persons = store.Persons;
        var result = new List<ChartDatum>(persons.Count);
        foreach (var p in persons)
        {
            var parents = store.FamiliesAsChild(p.Id)
                .SelectMany(f => f.PartnerIds).Distinct().ToList();
            var asPartner = store.FamiliesAsPartner(p.Id);
            var spouses = asPartner
                .SelectMany(f => f.PartnerIds).Where(id => id != p.Id).Distinct().ToList();
            var children = asPartner
                .SelectMany(f => f.ChildRefs.Select(c => c.PersonId)).Distinct().ToList();

            var avatar = p.PhotoFile is { } photo ? $"/media/{ThumbPath(photo)}" : null;
            result.Add(new ChartDatum(
                p.Id,
                new Dictionary<string, object?>
                {
                    // family-chart only lays out M/F; display sex stays in our own UI
                    ["gender"] = p.Sex == Sex.F ? "F" : "M",
                    ["label"] = p.DisplayName,
                    ["dates"] = p.LifeYears,
                    ["avatar"] = avatar
                },
                new ChartRels(parents, spouses, children)));
        }
        return result;
    }

    public string BuildJson() => JsonSerializer.Serialize(Build(), JsonOptions);

    /// <summary>Thumbnail path convention shared with <see cref="PhotoService"/>.</summary>
    public static string ThumbPath(string mainFile)
    {
        var dir = Path.GetDirectoryName(mainFile)?.Replace('\\', '/');
        var name = Path.GetFileNameWithoutExtension(mainFile);
        var thumb = $"{name}_thumb.webp";
        return string.IsNullOrEmpty(dir) ? thumb : $"{dir}/{thumb}";
    }
}
