using System.Text.Json;
using Ahnen.Web.Models;
using Ahnen.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

namespace Ahnen.Tests;

/// <summary>The committed 200-person development tree must stay loadable, legal and chartable.</summary>
public class SampleTreeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ahnen-sample-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private FamilyTreeStore LoadSample()
    {
        Directory.CreateDirectory(_dir);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "sample-200", "familytree.json"),
            Path.Combine(_dir, "familytree.json"));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DataDirectory"] = _dir }).Build();
        return new FamilyTreeStore(config, new SampleEnv(_dir));
    }

    [Fact]
    public void Sample_tree_loads_validates_and_maps_to_chart_data()
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
