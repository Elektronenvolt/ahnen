using System.Text.Json;
using Ahnen.Core.Models;
using Ahnen.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ahnen.Tests;

public class NamePersistenceTests
{
    [Fact]
    public void Names_round_trip_independently_with_optional_details_and_duplicates()
    {
        using var fixture = new TestStore();
        var store = fixture.Store;
        var person = store.AddPerson("Erika", "Schmidt");
        var baseline = store.GetPerson(person.Id)!.Names;
        var names = Enum.GetValues<NameType>().Select(type => new PersonName
        {
            Type = type, Given = $" {type} given ", Surname = $"{type} surname",
            Prefix = "Dr.", Suffix = "Jr.", Nickname = " Éri "
        }).ToList();
        names.Add(new PersonName { Type = NameType.MARRIED, Given = "Other", Surname = "Marriage" });

        Assert.True(store.UpdateNames(person.Id, names, baseline));
        var saved = store.GetPerson(person.Id)!;
        Assert.Equal(names.Count, saved.Names.Count);
        Assert.Equal(2, saved.Names.Count(n => n.Type == NameType.MARRIED));
        Assert.Equal(" BIRTH given ", saved.PrimaryName.Given);
        Assert.Equal(" Éri ", saved.Names[0].Nickname);

        var file = Path.Combine(store.DataDirectory, "familytree.json");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var fromDisk = JsonSerializer.Deserialize<FamilyTreeData>(File.ReadAllText(file), options)!;
        Assert.Equal(" Éri ", fromDisk.Persons[0].Names[0].Nickname);
        Assert.Equal("Other", fromDisk.Persons[0].Names[^1].Given);

        var reloaded = new DataStore(
            new DataStoreOptions(Path.Combine(fixture.ContentRootPath, "data"), "familytree.json"),
            NullLogger<DataStore>.Instance);
        Assert.Equal(saved.Names.Select(n => n.Full), reloaded.GetPerson(person.Id)!.Names.Select(n => n.Full));
    }

    [Fact]
    public void Fact_save_cannot_overwrite_newer_names_and_name_save_preserves_facts()
    {
        using var fixture = new TestStore();
        var store = fixture.Store;
        var person = store.AddPerson("Erika", "Schmidt");
        var staleForm = store.GetPerson(person.Id)!;
        var names = store.GetPerson(person.Id)!.Names;
        names.Add(new PersonName { Type = NameType.MARRIED, Given = "Erika", Surname = "Mustermann" });
        store.UpdateNames(person.Id, names);
        staleForm.GetOrAddEvent(IndividualEventType.BIRT).Date = "1950";
        store.UpdatePersonFacts(staleForm);
        Assert.Equal(2, store.GetPerson(person.Id)!.Names.Count);

        names[1].Surname = "Updated";
        store.UpdateNames(person.Id, names);
        Assert.Equal("1950", store.GetPerson(person.Id)!.Birth!.Date);
        Assert.Equal("Schmidt", store.GetPerson(person.Id)!.PrimaryName.Surname);
    }

    [Fact]
    public void Invalid_unsaved_date_does_not_prevent_independent_name_save()
    {
        using var fixture = new TestStore();
        var store = fixture.Store;
        var person = store.AddPerson("Before", "Name");
        var editorCopy = store.GetPerson(person.Id)!;
        editorCopy.GetOrAddEvent(IndividualEventType.BIRT).Date = "invalid date";
        editorCopy.Names[0].Given = "After";
        store.UpdateNames(person.Id, editorCopy.Names);
        var saved = store.GetPerson(person.Id)!;
        Assert.Equal("After", saved.PrimaryName.Given);
        Assert.Null(saved.Birth);
    }

    [Fact]
    public void Stale_name_save_is_rejected_without_overwriting_latest_values()
    {
        using var fixture = new TestStore();
        var store = fixture.Store;
        var person = store.AddPerson("Before", "Name");
        var baseline = store.GetPerson(person.Id)!.Names;
        store.UpdateNames(person.Id, [new PersonName { Given = "Newer", Surname = "Name" }]);
        Assert.Throws<InvalidOperationException>(() => store.UpdateNames(person.Id,
            [new PersonName { Given = "Stale" }], baseline));
        Assert.Equal("Newer", store.GetPerson(person.Id)!.PrimaryName.Given);
    }

    [Fact]
    public void Invalid_removal_does_not_change_memory_or_disk_and_undo_restores_names()
    {
        using var fixture = new TestStore();
        var store = fixture.Store;
        var person = store.AddPerson("Original", "Name");
        Assert.Throws<InvalidOperationException>(() => store.UpdateNames(person.Id, []));
        Assert.Single(store.GetPerson(person.Id)!.Names);
        store.UpdateNames(person.Id, [new PersonName { Given = "Changed" }]);
        Assert.False(store.UpdateNames(person.Id, store.GetPerson(person.Id)!.Names));
        Assert.True(store.Undo());
        Assert.Equal("Original", store.GetPerson(person.Id)!.PrimaryName.Given);
    }
}
