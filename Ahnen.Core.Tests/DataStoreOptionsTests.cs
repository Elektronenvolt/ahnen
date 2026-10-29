using System.Text.Json;
using Ahnen.Core.Models;
using Ahnen.Core.Services;
using Microsoft.Extensions.Logging;

namespace Ahnen.Core.Tests;

public sealed class DataStoreOptionsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ahnen-core-options-" + Guid.NewGuid().ToString("N"));
    private readonly ApplicationLogBuffer _logs = new();

    public DataStoreOptionsTests() => Directory.CreateDirectory(_directory);

    private DataStore Open(string? fileName)
    {
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(new ApplicationLogProvider(_logs)));
        return new DataStore(new DataStoreOptions(_directory, fileName), factory.CreateLogger<DataStore>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../outside.json")]
    [InlineData("nested/tree.json")]
    [InlineData("nested\\tree.json")]
    [InlineData("/tmp/tree.json")]
    [InlineData("C:\\tree.json")]
    [InlineData("bad\0name.json")]
    public void Invalid_filename_disables_editing_and_logs_error(string? fileName)
    {
        var store = Open(fileName);
        AssertReadOnly(store);
        var entry = Assert.Single(_logs.Snapshot());
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("DataFileName", entry.Message);
        Assert.Empty(Directory.GetFiles(_directory, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Missing_file_blocks_all_mutations_without_creating_a_document()
    {
        var store = Open("missing.json");
        AssertReadOnly(store);
        Action[] mutations =
        [
            () => store.AddPerson("No", "Save"), () => store.UpdatePersonFacts(new Person()),
            () => store.UpdateNames("I1", []), () => store.DeletePerson("I1"),
            () => store.SetRootPerson("I1"), () => store.AddPartner("I1", "I2"),
            () => store.AddChild("I1", "I2"), () => store.AddParent("I1", "I2"),
            () => store.RemovePartner("F1", "I1"), () => store.RemoveChild("F1", "I2"),
            () => store.UpdateFamily(new Family()), () => store.SetChildPedigree("F1", "I1", Pedigree.ADOPTED),
            () => store.Undo(), () => new PhotoService(store).Delete("photos/missing.webp")
        ];
        foreach (var mutation in mutations) Assert.Throws<InvalidOperationException>(mutation);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new PhotoService(store).SaveAsync("I1", Stream.Null, "image.png"));
        var entry = Assert.Single(_logs.Snapshot());
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains(Path.Combine(_directory, "missing.json"), entry.Message);
        Assert.Empty(Directory.GetFiles(_directory, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("tree.json")]
    [InlineData("custom.data")]
    public void Selected_document_is_saved_backed_up_and_reloadable(string fileName)
    {
        var path = Path.Combine(_directory, fileName);
        File.WriteAllText(path, "{}");
        var store = Open(fileName);
        Assert.True(store.CanEdit);
        var person = store.AddPerson("Saved", "Person");
        Assert.Equal("{}", File.ReadAllText(path + ".bak"));
        Assert.Equal("Saved Person", Open(fileName).GetPerson(person.Id)?.DisplayName);
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Equal(Path.Combine(_directory, "photos"), store.PhotosDirectory);
        Assert.Empty(_logs.Snapshot());
    }

    [Theory]
    [InlineData("invalid json")]
    [InlineData("null")]
    public void Malformed_document_throws_instead_of_becoming_an_empty_tree(string contents)
    {
        File.WriteAllText(Path.Combine(_directory, "broken.json"), contents);
        Assert.Throws<JsonException>(() => Open("broken.json"));
        Assert.Equal(contents, File.ReadAllText(Path.Combine(_directory, "broken.json")));
        Assert.Empty(_logs.Snapshot());
    }

    private static void AssertReadOnly(DataStore store)
    {
        Assert.False(store.CanEdit);
        Assert.Empty(store.Persons);
        Assert.Empty(store.Families);
        Assert.Null(store.RootPersonId);
        Assert.Throws<InvalidOperationException>(store.EnsureCanEdit);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
