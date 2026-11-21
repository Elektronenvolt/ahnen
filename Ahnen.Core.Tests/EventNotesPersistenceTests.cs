using Ahnen.Core.Models;
using Ahnen.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ahnen.Core.Tests;

public class EventNotesPersistenceTests : IDisposable
{
    private readonly TestStore _testStore = new();
    private DataStore Store => _testStore.Store;
    private string DataDirectory => Path.Combine(_testStore.ContentRootPath, "data");

    public void Dispose() => _testStore.Dispose();

    [Fact]
    public void Legacy_events_without_notes_load_with_empty_collections_without_rewriting()
    {
        const string legacy = """
            {"persons":[{"id":"I1","notes":["Common"],"events":[{"type":"DEAT","date":"1950"}]}],
             "families":[{"id":"F1","partnerIds":["I1"],"events":[{"type":0,"date":"1970"}]}]}
            """;
        File.WriteAllText(Path.Combine(DataDirectory, "familytree.json"), legacy);
        var store = Reload();

        Assert.Equal(legacy, File.ReadAllText(Path.Combine(DataDirectory, "familytree.json")));
        Assert.Equal(["Common"], store.GetPerson("I1")!.Notes);
        Assert.Empty(store.GetPerson("I1")!.Death!.Notes);
        Assert.Null(store.GetPerson("I1")!.Death!.Cause);
        Assert.Empty(Assert.Single(store.GetFamily("F1")!.Events).Notes);
    }

    [Fact]
    public void Event_notes_survive_reload_and_undo_without_mixing_common_or_marriage_notes()
    {
        var person = Store.AddPerson("Alex", "Example");
        var firstPartner = Store.AddPerson("Pat", "First");
        var secondPartner = Store.AddPerson("Sam", "Second");
        var first = Store.AddPartner(person.Id, firstPartner.Id);
        var second = Store.AddPartner(person.Id, secondPartner.Id);
        person = Store.GetPerson(person.Id)!;
        person.Notes = ["Common"];
        person.GetOrAddEvent(IndividualEventType.BIRT).Notes = ["Birth one", "Birth two"];
        var death = person.GetOrAddEvent(IndividualEventType.DEAT);
        death.Cause = "Natural causes";
        death.Notes = ["Death"];
        Store.UpdatePersonFacts(person);
        FamilyStatus.ApplyStatus(first, "MARR");
        first.Marriage!.Notes = ["First marriage"];
        Store.UpdateFamily(first);
        FamilyStatus.ApplyStatus(second, "MARR");
        second.Marriage!.Notes = ["Second marriage"];
        Store.UpdateFamily(second);

        var reloaded = Reload();
        var saved = reloaded.GetPerson(person.Id)!;
        Assert.Equal(["Common"], saved.Notes);
        Assert.Equal(["Birth one", "Birth two"], saved.Birth!.Notes);
        Assert.Equal(["Death"], saved.Death!.Notes);
        Assert.Equal("Natural causes", saved.Death.Cause);
        Assert.True(saved.IsDeceased);
        Assert.Equal(["First marriage"], reloaded.FamiliesAsPartner(firstPartner.Id).Single().Marriage!.Notes);
        Assert.Equal(["Second marriage"], reloaded.FamiliesAsPartner(secondPartner.Id).Single().Marriage!.Notes);

        second = Store.GetFamily(second.Id)!;
        second.Marriage!.Notes.Clear();
        Store.UpdateFamily(second);
        Assert.Empty(Store.GetFamily(second.Id)!.Marriage!.Notes);
        Assert.True(Store.Undo());
        Assert.Equal(["Second marriage"], Reload().GetFamily(second.Id)!.Marriage!.Notes);
    }

    private DataStore Reload() =>
        new(new DataStoreOptions(DataDirectory, "familytree.json"), NullLogger<DataStore>.Instance);
}
