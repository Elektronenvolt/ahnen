using Ahnen.Core.Services;
using Ahnen.Desktop.Localization;
using Ahnen.Desktop.Services;
using Ahnen.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ahnen.Desktop.Tests;

// Localizer.Instance is process-wide, so view-model tests must not run in parallel with each other.
[Collection("Shared localizer")]
public class MainViewModelTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly ServiceProvider _services;

    public MainViewModelTests()
    {
        Localizer.Instance.SetLanguage("en");
        _services = App.BuildServices(_root.Paths);
    }

    public void Dispose()
    {
        _services.Dispose();
        Localizer.Instance.SetLanguage("en");
        _root.Dispose();
    }

    private MainViewModel Main => _services.GetRequiredService<MainViewModel>();

    [Fact]
    public void Starts_on_the_tree_page_with_both_pages_available()
    {
        Assert.Same(_services.GetRequiredService<TreePageViewModel>(), Main.SelectedPage);
        Assert.Equal(["Family tree", "Edit data"], Main.Pages.Select(p => p.Title));
    }

    [Fact]
    public void Shared_singletons_are_resolved_once()
    {
        Assert.Same(Main, _services.GetRequiredService<MainViewModel>());
        Assert.Same(_services.GetRequiredService<ApplicationLogBuffer>(), _services.GetRequiredService<ApplicationLogBuffer>());
    }

    [Fact]
    public void Changing_language_refreshes_titles_and_status_and_persists_the_choice()
    {
        var changed = new List<string?>();
        Main.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Main.Pages[0].PropertyChanged += (_, e) => changed.Add("page:" + e.PropertyName);

        Main.SetLanguageCommand.Execute("de");

        Assert.True(Main.IsGerman);
        Assert.False(Main.IsEnglish);
        Assert.Equal(["Stammbaum", "Daten bearbeiten"], Main.Pages.Select(p => p.Title));
        Assert.Contains(nameof(MainViewModel.StatusText), changed);
        Assert.Contains("page:Title", changed);
        Assert.StartsWith("Es ist noch kein", Main.StatusText);
        Assert.Equal("de", _services.GetRequiredService<PreferencesService>().Load().Language);
    }

    [Fact]
    public void Language_choice_survives_an_unwritable_settings_file()
    {
        Directory.CreateDirectory(_root.Paths.SettingsFile);

        Main.SetLanguageCommand.Execute("de");

        Assert.True(Main.IsGerman);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("1", 1)]
    [InlineData("7", 1)]
    [InlineData("-1", 1)]
    [InlineData("x", 1)]
    public void SelectPage_switches_page_and_ignores_invalid_indexes(string index, int expected)
    {
        Main.SelectPageCommand.Execute("1");

        Main.SelectPageCommand.Execute(index);

        Assert.Same(Main.Pages[expected], Main.SelectedPage);
    }

    [Fact]
    public void Status_describes_a_missing_document_and_a_loaded_one()
    {
        Assert.Equal("No family tree document is available yet. Importing an existing document will be offered here.",
            Main.StatusText);

        using var other = new TempRoot();
        Directory.CreateDirectory(other.Paths.DataDirectory);
        File.WriteAllText(Path.Combine(other.Paths.DataDirectory, DesktopPaths.DocumentFileName), "{}");
        using var services = App.BuildServices(other.Paths);

        Assert.Equal("0 people, 0 families", services.GetRequiredService<MainViewModel>().StatusText);
    }

    [Fact]
    public void Status_reports_an_unreadable_document()
    {
        using var other = new TempRoot();
        Directory.CreateDirectory(other.Paths.DataDirectory);
        File.WriteAllText(Path.Combine(other.Paths.DataDirectory, DesktopPaths.DocumentFileName), "invalid json");
        using var services = App.BuildServices(other.Paths);

        Assert.StartsWith("The family tree document could not be loaded:",
            services.GetRequiredService<MainViewModel>().StatusText);
    }

    [Fact]
    public void Log_view_model_shows_new_entries_after_refresh()
    {
        var logs = _services.GetRequiredService<LogViewModel>();

        _services.GetRequiredService<ILogger<MainViewModelTests>>().LogWarning("visible in viewer");
        logs.Refresh();

        Assert.Contains(logs.Entries, e => e.Message == "visible in viewer" && e.Level == LogLevel.Warning);
        Assert.False(logs.IsEmpty);
    }
}
