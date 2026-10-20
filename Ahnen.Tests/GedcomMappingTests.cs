using System.Reflection;
using System.Text.Json.Serialization;
using Ahnen.Web.Models;

namespace Ahnen.Tests;

/// <summary>
/// GEDCOM compliance guard: every serialized schema field must have a documented
/// mapping row in the README's "Field mapping" table. If you add a model property,
/// document its GEDCOM 7 mapping (binding project rule: GEDCOM overrules).
/// </summary>
public class GedcomMappingTests
{
    [Fact]
    public void Every_schema_field_is_documented_in_readme()
    {
        var readme = FindReadme();
        var table = File.ReadAllText(readme);

        var modelTypes = new[]
        {
            typeof(FamilyTreeData), typeof(Person), typeof(PersonName), typeof(Address),
            typeof(MediaRef), typeof(IndividualEvent), typeof(FamilyEvent),
            typeof(Family), typeof(ChildRef)
        };

        var undocumented = new List<string>();
        foreach (var type in modelTypes)
        {
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetCustomAttribute<JsonIgnoreAttribute>() is not null) continue;
                var jsonName = char.ToLowerInvariant(prop.Name[0]) + prop.Name[1..];
                if (!table.Contains(jsonName, StringComparison.Ordinal))
                    undocumented.Add($"{type.Name}.{prop.Name} (json: {jsonName})");
            }
        }

        Assert.True(undocumented.Count == 0,
            "Missing GEDCOM mapping documentation in README.md for: " + string.Join(", ", undocumented));
    }

    private static string FindReadme()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "README.md");
            if (File.Exists(candidate) && File.ReadAllText(candidate).Contains("Field mapping"))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("README.md with mapping table not found");
    }
}
