using System.Text.Json;
using Ahnen.Web.Models;
using Ahnen.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ahnen.Tests;

/// <summary>The supplied 200-person test dataset must stay loadable, legal and chartable.</summary>
public class SampleTreeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ahnen-sample-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private DataStore LoadSample()
    {
        var dataDirectory = Path.Combine(_dir, "data");
        Directory.CreateDirectory(dataDirectory);
        var file = Path.Combine(dataDirectory, "familytree.json");
        if (!File.Exists(file))
            File.Copy(Path.Combine(AppContext.BaseDirectory, "data", "familytree.json"), file);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DataFileName"] = "familytree.json" }).Build();
        return new DataStore(config, new SampleEnv(_dir), NullLogger<DataStore>.Instance);
    }

    [Fact]
    public void Test_dataset_loads_validates_and_maps_to_chart_data()
    {
        var store = LoadSample();
        Assert.Equal(200, store.Persons.Count);
        GedcomValidator.ValidateLegal(new FamilyTreeData
        {
            Persons = [.. store.Persons], Families = [.. store.Families], RootPersonId = store.RootPersonId
        });

        var data = new ChartDataService(store).Build();
        Assert.Equal(200, data.Indis.Count);
        Assert.Equal(store.Families.Count, data.Fams.Count);
        Assert.All(data.Fams, f => Assert.InRange(f.Children.Count, 0, 6));
        Assert.Contains(data.Fams, f => f.Status == "DIV");
        Assert.Contains(data.Fams, f => f.Pedigrees is not null);
        Assert.Contains(data.Indis, i => i.Fams.Count >= 2);
        Assert.All(data.Indis, i => Assert.NotNull(i.BirthYear));
        JsonSerializer.Serialize(data);
    }

    [Fact]
    public void Existing_test_dataset_is_loaded_without_rewriting()
    {
        var store = LoadSample();
        var saved = JsonSerializer.Deserialize<FamilyTreeData>(
            File.ReadAllText(Path.Combine(_dir, "data", "familytree.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.Equal(200, saved.Persons.Count);
        Assert.Equal(store.RootPersonId, saved.RootPersonId);
        Assert.Equal(store.Families.Count, saved.Families.Count);
        Assert.Equal(200, LoadSample().Persons.Count);
        Assert.False(File.Exists(Path.Combine(_dir, "data", "familytree.json.bak")));
    }

    [Fact]
    public void Saved_data_is_preserved_on_reload()
    {
        var store = LoadSample();
        var added = store.AddPerson("Saved", "Person", Sex.U);
        store.SetRootPerson(added.Id);

        var reloaded = LoadSample();

        Assert.Equal(201, reloaded.Persons.Count);
        Assert.Equal(added.Id, reloaded.RootPersonId);
        Assert.Equal("Saved", reloaded.GetPerson(added.Id)!.PrimaryName.Given);
    }

    private sealed class SampleEnv(string root) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Ahnen.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Test";
    }
}
