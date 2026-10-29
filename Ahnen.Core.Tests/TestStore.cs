using Ahnen.Core.Models;
using Ahnen.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ahnen.Testing;

/// <summary>Builds a store backed by a temp directory for tests.</summary>
public sealed class TestStore : IDisposable
{
    public DataStore Store { get; }
    public string ContentRootPath => _dir;
    private readonly string _dir;

    public TestStore()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ahnen-tests-" + Guid.NewGuid().ToString("N"));
        var dataDirectory = Path.Combine(_dir, "data");
        Directory.CreateDirectory(dataDirectory);
        File.WriteAllText(Path.Combine(dataDirectory, "familytree.json"), "{}");
        Store = new DataStore(new DataStoreOptions(dataDirectory, "familytree.json"), NullLogger<DataStore>.Instance);
    }

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
    }
}

public static class TestData
{
    /// <summary>
    /// Builds a three-generation family:
    /// grandpa+grandma → father, uncle; father+mother → me, brother, sister;
    /// uncle+auntInLaw → cousin; brother+sisterInLaw → nephew.
    /// </summary>
    public static Dictionary<string, Person> BuildFamily(DataStore store)
    {
        var p = new Dictionary<string, Person>
        {
            ["grandpa"] = store.AddPerson("Opa", "Test", Sex.M),
            ["grandma"] = store.AddPerson("Oma", "Test", Sex.F),
            ["father"] = store.AddPerson("Vater", "Test", Sex.M),
            ["mother"] = store.AddPerson("Mutter", "Alt", Sex.F),
            ["uncle"] = store.AddPerson("Onkel", "Test", Sex.M),
            ["auntInLaw"] = store.AddPerson("Tante", "Fremd", Sex.F),
            ["me"] = store.AddPerson("Ich", "Test", Sex.M),
            ["brother"] = store.AddPerson("Bruder", "Test", Sex.M),
            ["sister"] = store.AddPerson("Schwester", "Test", Sex.F),
            ["cousin"] = store.AddPerson("Cousin", "Test", Sex.M),
            ["sisterInLaw"] = store.AddPerson("Schwägerin", "Fremd", Sex.F),
            ["nephew"] = store.AddPerson("Neffe", "Test", Sex.M),
            ["wife"] = store.AddPerson("Frau", "Fremd", Sex.F)
        };

        store.AddPartner(p["grandpa"].Id, p["grandma"].Id);
        store.AddChild(p["grandpa"].Id, p["father"].Id);
        store.AddChild(p["grandpa"].Id, p["uncle"].Id);

        store.AddPartner(p["father"].Id, p["mother"].Id);
        store.AddChild(p["father"].Id, p["me"].Id);
        store.AddChild(p["father"].Id, p["brother"].Id);
        store.AddChild(p["father"].Id, p["sister"].Id);

        store.AddPartner(p["uncle"].Id, p["auntInLaw"].Id);
        store.AddChild(p["uncle"].Id, p["cousin"].Id);

        store.AddPartner(p["brother"].Id, p["sisterInLaw"].Id);
        store.AddChild(p["brother"].Id, p["nephew"].Id);

        var meWife = store.AddPartner(p["me"].Id, p["wife"].Id);
        FamilyStatus.ApplyStatus(meWife, "MARR");
        store.UpdateFamily(meWife);

        return p;
    }
}
