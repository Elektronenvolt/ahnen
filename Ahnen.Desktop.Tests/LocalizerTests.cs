using System.Globalization;
using System.Resources;
using Ahnen.Desktop.Localization;

namespace Ahnen.Desktop.Tests;

public class LocalizerTests
{
    [Fact]
    public void Defaults_to_english_and_resolves_shared_and_desktop_keys()
    {
        var localizer = new Localizer();

        Assert.Equal("en", localizer.Language);
        Assert.Equal("Family tree", localizer["Nav_Tree"]);
        Assert.Equal("Main navigation", localizer["Nav_Label"]);
    }

    [Fact]
    public void Switching_to_german_changes_both_resource_sources_and_raises_notifications()
    {
        var localizer = new Localizer();
        var cultureChanges = 0;
        var changedProperties = new List<string?>();
        localizer.CultureChanged += () => cultureChanges++;
        localizer.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        localizer.SetLanguage("de");

        Assert.Equal("Stammbaum", localizer["Nav_Tree"]);
        Assert.Equal("Hauptnavigation", localizer["Nav_Label"]);
        Assert.Equal(1, cultureChanges);
        Assert.Equal(["Item[]"], changedProperties);

        localizer.SetLanguage("de");
        Assert.Equal(1, cultureChanges);
        localizer.SetLanguage("en");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("fr")]
    public void Unsupported_language_falls_back_to_english(string? language)
    {
        var localizer = new Localizer();

        Assert.Equal("en", localizer.SetLanguage(language));
        Assert.Equal("Family tree", localizer["Nav_Tree"]);
    }

    [Fact]
    public void Missing_key_is_visible_instead_of_empty()
    {
        Assert.Equal("[No_Such_Key]", new Localizer()["No_Such_Key"]);
    }

    [Fact]
    public void Format_uses_the_current_language()
    {
        var localizer = new Localizer();
        localizer.SetLanguage("de");

        Assert.Equal("3 Personen, 2 Familien", localizer.Format("Document_Loaded", 3, 2));
        localizer.SetLanguage("en");
        Assert.Equal("3 people, 2 families", localizer.Format("Document_Loaded", 3, 2));
    }

    [Fact]
    public void Desktop_resources_define_the_same_keys_and_placeholders_in_both_languages()
    {
        var manager = new ResourceManager("Ahnen.Desktop.Resources.Strings", typeof(Localizer).Assembly);
        var english = Entries(manager, CultureInfo.InvariantCulture);
        var german = Entries(manager, CultureInfo.GetCultureInfo("de"));

        Assert.Equal(english.Keys.Order(), german.Keys.Order());
        foreach (var (key, value) in english)
        {
            Assert.False(string.IsNullOrWhiteSpace(value), key);
            Assert.False(string.IsNullOrWhiteSpace(german[key]), key);
            Assert.Equal(Placeholders(value), Placeholders(german[key]));
        }
    }

    private static Dictionary<string, string> Entries(ResourceManager manager, CultureInfo culture) =>
        manager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)!
            .Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string)e.Value!);

    private static string Placeholders(string text) =>
        string.Join(",", System.Text.RegularExpressions.Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).Order());
}
