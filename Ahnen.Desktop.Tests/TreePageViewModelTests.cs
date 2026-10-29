using System.Text.Json;
using Ahnen.Core.Services;
using Ahnen.Desktop.Localization;
using Ahnen.Desktop.Services;
using Ahnen.Desktop.ViewModels;
using Ahnen.Desktop.Visualization;
using Microsoft.Extensions.DependencyInjection;

namespace Ahnen.Desktop.Tests;

[Collection("Shared localizer")]
public class TreePageViewModelTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly ServiceProvider _services;
    private TreePageViewModel Tree => _services.GetRequiredService<TreePageViewModel>();

    public TreePageViewModelTests()
    {
        Localizer.Instance.SetLanguage("en");
        File.WriteAllText(Path.Combine(_root.Paths.DataDirectory, DesktopPaths.DocumentFileName),
            JsonSerializer.Serialize(TreeLayoutTests.Fixture(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        _services = App.BuildServices(_root.Paths);
    }

    public void Dispose()
    {
        _services.Dispose();
        Localizer.Instance.SetLanguage("en");
        _root.Dispose();
    }

    [Fact]
    public async Task Initial_load_shows_real_people_and_selected_details()
    {
        await Tree.InitializeAsync();
        await Tree.SelectionTask;

        Assert.Equal(9, Tree.People.Count);
        Assert.Contains(Tree.Scene.Cards, c => c.Person.Id == "C");
        Assert.Equal("C", Tree.SelectedPerson?.Id);
        Assert.Equal("C", Tree.RootId);
        Assert.Contains("C | Unknown", Tree.Details);
        Assert.Contains("this person", Tree.Relationship);
        Assert.False(Tree.IsBusy);
        Assert.Empty(Tree.ErrorText);
    }

    [Fact]
    public async Task Changing_chart_while_initial_loading_is_pending_keeps_the_loaded_document()
    {
        var initialLoad = Tree.InitializeAsync();
        Tree.Chart = TreeChart.Descendants;
        Tree.Orientation = TreeOrientation.Horizontal;
        await Task.WhenAll(initialLoad, Tree.LayoutTask);

        Assert.Equal(9, Tree.People.Count);
        Assert.Contains(Tree.Scene.Cards, c => c.Person.Id == "F");
        Assert.DoesNotContain(Tree.Scene.Cards, c => c.Person.Id == "A");
        Assert.False(Tree.IsBusy);
        Assert.Empty(Tree.ErrorText);
    }

    [Fact]
    public async Task Overview_includes_isolated_people_without_changing_stored_root_or_preferred_chart()
    {
        await Tree.InitializeAsync();
        Tree.Chart = TreeChart.Ancestors;
        await Tree.LayoutTask;
        Tree.ShowAllCommand.Execute(null);
        await Tree.LayoutTask;

        Assert.True(Tree.IsOverview);
        Assert.Null(Tree.SelectedPerson);
        Assert.Equal(9, Tree.Scene.Cards.Count);
        Assert.Equal(TreeChart.Ancestors, Tree.Chart);
        Assert.Equal("C", _services.GetRequiredService<DocumentSession>().Store!.RootPersonId);
    }

    [Fact]
    public async Task Shift_selection_changes_only_details_and_normal_selection_returns_from_overview_to_focus()
    {
        await Tree.InitializeAsync();
        Tree.ShowAllCommand.Execute(null);
        await Tree.LayoutTask;
        var overview = Tree.Scene;
        Tree.SelectPerson("A", false);
        await Tree.SelectionTask;

        Assert.Same(overview, Tree.Scene);
        Assert.True(Tree.IsOverview);
        Assert.Equal("C", Tree.FocusId);
        Assert.Equal("A", Tree.SelectedPerson?.Id);
        Assert.Contains("parent", Tree.Relationship);

        string? centered = null;
        Tree.FocusRequested += id => centered = id;
        Tree.SelectPerson("isolated", true);
        await Tree.LayoutTask;

        Assert.False(Tree.IsOverview);
        Assert.Equal("isolated", Tree.FocusId);
        Assert.Equal("isolated", centered);
        Assert.Single(Tree.Scene.Cards);
        Assert.Equal("C", Tree.RootId);
    }

    [Fact]
    public async Task Search_matches_name_and_id_without_mutating_the_document()
    {
        await Tree.InitializeAsync();
        Tree.SearchText = "ISOLATED";

        Assert.Equal("isolated", Assert.Single(Tree.SearchResults).Id);
        Assert.Equal(9, Tree.People.Count);
        Tree.SearchText = "does not exist";
        Assert.Empty(Tree.SearchResults);
    }

    [Fact]
    public async Task Root_is_changed_only_by_explicit_save_and_generates_a_backup()
    {
        await Tree.InitializeAsync();
        Tree.SelectPerson("A", false);
        await Tree.SetRootCommand.ExecuteAsync(null);

        Assert.Equal("A", Tree.RootId);
        Assert.Equal("C", Tree.FocusId);
        Assert.Equal("A", _services.GetRequiredService<DocumentSession>().Store!.RootPersonId);
        Assert.Equal(0, Assert.Single(Tree.Scene.Cards, c => c.Person.Id == "A").Generation);
        Assert.True(File.Exists(Path.Combine(_root.Paths.DataDirectory, DesktopPaths.DocumentFileName + ".bak")));
        Assert.Contains("this person", Tree.Relationship);
    }

    [Fact]
    public async Task Failed_root_save_is_visible_and_keeps_the_old_root()
    {
        await Tree.InitializeAsync();
        Tree.SelectPerson("A", false);
        Directory.CreateDirectory(Path.Combine(_root.Paths.DataDirectory, DesktopPaths.DocumentFileName + ".tmp"));
        await Tree.SetRootCommand.ExecuteAsync(null);

        Assert.Equal("C", Tree.RootId);
        Assert.Equal("C", _services.GetRequiredService<DocumentSession>().Store!.RootPersonId);
        Assert.Contains("failed", Tree.ErrorText);
        Assert.Contains(_services.GetRequiredService<ApplicationLogBuffer>().Snapshot(), entry => entry.Message.Contains("could not be saved"));
    }

    [Fact]
    public async Task Settings_round_trip_without_overwriting_language_and_latest_layout_wins()
    {
        var preferences = _services.GetRequiredService<PreferencesService>();
        preferences.TrySave(new Preferences("de"));
        await Tree.InitializeAsync();
        Tree.Chart = TreeChart.Ancestors;
        Tree.Chart = TreeChart.Descendants;
        Tree.Orientation = TreeOrientation.Horizontal;
        Tree.Colors = TreeColors.Generation;
        Tree.ShowIds = true;
        Tree.Generations = 1;
        await Tree.LayoutTask;

        var saved = preferences.Load();
        Assert.Equal("de", saved.Language);
        Assert.Equal(TreeChart.Descendants, saved.Chart);
        Assert.Equal(TreeColors.Generation, saved.Colors);
        Assert.Equal(TreeOrientation.Horizontal, saved.Orientation);
        Assert.True(saved.ShowIds);
        Assert.Equal(1, saved.Generations);
        Assert.DoesNotContain(Tree.Scene.Cards, c => c.Person.Id == "A");
        Assert.Contains(Tree.Scene.Cards, c => c.Person.Id == "F");
        Assert.False(Tree.IsBusy);
    }

    [Fact]
    public async Task Language_switch_refreshes_options_and_details()
    {
        await Tree.InitializeAsync();
        Localizer.Instance.SetLanguage("de");
        await Tree.SelectionTask;

        Assert.Contains(Tree.ChartChoices, choice => choice.Value == TreeChart.Ancestors && choice.Label == "Vorfahren");
        Assert.Contains("diese Person", Tree.Relationship);
        Assert.Contains("Personen", Tree.SceneStatus);
    }

    [Fact]
    public async Task Missing_document_shows_no_sample_or_success_shaped_data()
    {
        using var root = new TempRoot();
        using var services = App.BuildServices(root.Paths);
        var tree = services.GetRequiredService<TreePageViewModel>();
        await tree.InitializeAsync();

        Assert.Empty(tree.People);
        Assert.Empty(tree.Scene.Cards);
        Assert.Equal("No family tree data is loaded.", tree.SceneStatus);
    }

    [Fact]
    public void Snapshot_is_detached_and_keeps_people_families_and_root_together()
    {
        var store = _services.GetRequiredService<DocumentSession>().Store!;
        var snapshot = store.GetSnapshot();
        snapshot.Persons.Clear();
        snapshot.Families.Clear();
        snapshot.RootPersonId = "changed";

        Assert.Equal(9, store.Persons.Count);
        Assert.Equal(3, store.Families.Count);
        Assert.Equal((9, 3), store.Counts);
        Assert.Equal("C", store.RootPersonId);
    }
}
