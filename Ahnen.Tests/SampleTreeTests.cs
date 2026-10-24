using System.Text.Json;
using Ahnen.Web.Models;
using Ahnen.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

namespace Ahnen.Tests;

/// <summary>The default 200-person seed must stay loadable, legal and chartable.</summary>
public class SampleTreeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ahnen-sample-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private FamilyTreeStore LoadSample()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DataDirectory"] = _dir }).Build();
        return new FamilyTreeStore(config, new SampleEnv(AppContext.BaseDirectory));
    }

    [Fact]
    public void Default_seed_loads_validates_and_maps_to_chart_data()
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
    public void Default_seed_is_persisted_on_first_run()
    {
        var store = LoadSample();
        var saved = JsonSerializer.Deserialize<FamilyTreeData>(
            File.ReadAllText(Path.Combine(_dir, "familytree.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.Equal(200, saved.Persons.Count);
        Assert.Equal(store.RootPersonId, saved.RootPersonId);
        Assert.Equal(store.Families.Count, saved.Families.Count);
        Assert.Equal(200, LoadSample().Persons.Count);
    }

    [Fact]
    public void Existing_data_is_not_replaced_by_default_seed()
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
