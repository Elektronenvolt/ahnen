using System.ComponentModel;
using System.Globalization;
using System.Resources;
using Ahnen.Core;

namespace Ahnen.Desktop.Localization;

/// <summary>
/// Resolves UI strings from the desktop resources first, then the shared Core resources, and
/// notifies bindings (<c>Item[]</c>) when the culture changes so open windows refresh at once.
/// </summary>
public sealed class Localizer : INotifyPropertyChanged
{
    public static readonly string[] SupportedLanguages = ["en", "de"];

    private static readonly ResourceManager[] Managers =
    [
        new("Ahnen.Desktop.Resources.Strings", typeof(Localizer).Assembly),
        new("Ahnen.Core.Resources.SharedResources", typeof(SharedResources).Assembly)
    ];

    /// <summary>Instance used by XAML bindings (<see cref="LocExtension"/>).</summary>
    public static Localizer Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? CultureChanged;

    public CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en");
    public string Language => Culture.TwoLetterISOLanguageName;

    public string this[string key]
    {
        get
        {
            foreach (var manager in Managers)
                if (manager.GetString(key, Culture) is { } value)
                    return value;
            return $"[{key}]";
        }
    }

    public string Format(string key, params object[] args) =>
        string.Format(Culture, this[key], args);

    /// <summary>Falls back to English for unsupported languages. Returns the language actually applied.</summary>
    public string SetLanguage(string? language)
    {
        var code = SupportedLanguages.Contains(language) ? language! : "en";
        if (code == Language) return code;
        Culture = CultureInfo.GetCultureInfo(code);
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        CultureInfo.CurrentUICulture = Culture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        CultureChanged?.Invoke();
        return code;
    }

    /// <summary>Default for first start: German when the Windows UI language is German, else English.</summary>
    public static string DetectSystemLanguage() =>
        CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == "de" ? "de" : "en";
}
