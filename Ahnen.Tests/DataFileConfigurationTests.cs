using System.Text.Json;
using Ahnen.Core.Services;
using Ahnen.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Ahnen.Tests;

public sealed class DataFileConfigurationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ahnen-config-" + Guid.NewGuid().ToString("N"));
    private readonly ApplicationLogBuffer _logs = new();
    private string DataDirectory => Path.Combine(_root, "data");

    private DataStore CreateStore(string? fileName = "familytree.json",
        Dictionary<string, string?>? extraSettings = null)
    {
        var settings = extraSettings ?? new Dictionary<string, string?>();
        settings["DataFileName"] = fileName;
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(new ApplicationLogProvider(_logs)));
        return new DataStore(WebDataStoreOptions.Create(config, _root), factory.CreateLogger<DataStore>());
    }

    private void PrepareFile(string fileName, string contents = "{}")
    {
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(Path.Combine(DataDirectory, fileName), contents);
    }

    [Theory]
    [InlineData("familytree.json")]
    [InlineData("ahnen.json")]
    [InlineData("my-own-tree.data")]
    public void Any_configured_filename_is_used_for_save_reload_and_backup(string fileName)
    {
        PrepareFile(fileName);
        var store = CreateStore(fileName);
        Assert.True(store.CanEdit);
        Assert.Empty(store.Persons);
        Assert.Empty(store.Families);
        Assert.Null(store.RootPersonId);
        var selectedFile = Path.Combine(DataDirectory, fileName);
        Assert.Equal("{}", File.ReadAllText(selectedFile));

        var person = store.AddPerson("Saved", "Person");

        Assert.Equal("{}", File.ReadAllText(selectedFile + ".bak"));
        Assert.NotNull(CreateStore(fileName).GetPerson(person.Id));
        Assert.False(File.Exists(selectedFile + ".tmp"));
        Assert.Equal(Path.Combine(DataDirectory, "photos"), store.PhotosDirectory);
        Assert.Empty(_logs.Snapshot());
    }

    [Fact]
    public void Supplied_optional_file_contains_an_empty_object()
    {
        Assert.Equal("{}", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "ahnen.json")).Trim());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../outside.json")]
    [InlineData("nested/tree.json")]
    [InlineData("nested\\tree.json")]
    [InlineData("/tmp/tree.json")]
    [InlineData("C:\\tree.json")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("bad\0name.json")]
    public void Missing_or_invalid_configuration_is_empty_readonly_and_logged(string? fileName)
    {
        PrepareFile("familytree.json");
        var store = CreateStore(fileName);

        Assert.False(store.CanEdit);
        Assert.Empty(store.Persons);
        Assert.Empty(store.Families);
        Assert.Null(store.RootPersonId);
        var error = Assert.Single(_logs.Snapshot());
        Assert.Equal(LogLevel.Error, error.Level);
        Assert.Contains("DataFileName", error.Message);
        Assert.Equal("{}", File.ReadAllText(Path.Combine(DataDirectory, "familytree.json")));
        Assert.Single(Directory.GetFiles(DataDirectory));
    }

    [Fact]
    public async Task Missing_file_logs_error_and_all_mutation_paths_are_blocked()
    {
        var store = CreateStore("not-found.json");
        Assert.False(store.CanEdit);
        Assert.Empty(store.Persons);
        Assert.Empty(store.Families);
        Assert.Null(store.RootPersonId);
        var error = Assert.Single(_logs.Snapshot());
        Assert.Equal(LogLevel.Error, error.Level);
        Assert.Contains(Path.Combine(DataDirectory, "not-found.json"), error.Message);

        Assert.Throws<InvalidOperationException>(() => store.AddPerson("No", "Save"));
        Assert.Throws<InvalidOperationException>(() => store.UpdateNames("I1", []));
        Assert.Throws<InvalidOperationException>(() => store.Undo());
        Assert.Throws<InvalidOperationException>(() => store.SetRootPerson("I1"));
        var photos = new PhotoService(store);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            photos.SaveAsync("I1", Stream.Null, "image.png"));
        Assert.Throws<InvalidOperationException>(() => photos.Delete("photos/image.webp"));
        Assert.Empty(Directory.GetFiles(DataDirectory, "*", SearchOption.AllDirectories));
        Assert.True(Directory.Exists(store.PhotosDirectory));
        Assert.Empty(store.Persons);
    }

    [Fact]
    public void Directory_and_old_path_settings_do_not_override_fixed_data_folder()
    {
        PrepareFile("chosen.json");
        var store = CreateStore("chosen.json", new Dictionary<string, string?>
        {
            ["DataDirectory"] = Path.Combine(_root, "elsewhere"),
            ["FAMILYTREE_DATA_DIR"] = Path.Combine(_root, "elsewhere"),
            ["DataFilePath"] = "/outside.json"
        });

        Assert.True(store.CanEdit);
        Assert.Equal(DataDirectory, store.DataDirectory);
        Assert.False(Directory.Exists(Path.Combine(_root, "elsewhere")));
    }

    [Theory]
    [InlineData("invalid json")]
    [InlineData("null")]
    public void Malformed_data_is_not_silently_treated_as_missing(string contents)
    {
        PrepareFile("broken.json", contents);
        Assert.Throws<JsonException>(() => CreateStore("broken.json"));
        Assert.Empty(_logs.Snapshot());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
