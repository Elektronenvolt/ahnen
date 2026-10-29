using Ahnen.Desktop.Localization;
using Ahnen.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Ahnen.Desktop.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly Localizer _localizer;
    private readonly PreferencesService _preferences;
    private readonly DocumentSession _document;

    public MainViewModel(Localizer localizer, PreferencesService preferences, DocumentSession document,
        LogViewModel logs, TreePageViewModel tree, EditPageViewModel edit)
    {
        _localizer = localizer;
        _preferences = preferences;
        _document = document;
        Logs = logs;
        Pages = [tree, edit];
        _selectedPage = tree;
        localizer.CultureChanged += () =>
        {
            OnPropertyChanged(nameof(IsEnglish));
            OnPropertyChanged(nameof(IsGerman));
            OnPropertyChanged(nameof(StatusText));
        };
    }

    public IReadOnlyList<PageViewModel> Pages { get; }
    public LogViewModel Logs { get; }

    [ObservableProperty]
    private PageViewModel _selectedPage;

    public bool IsEnglish => _localizer.Language == "en";
    public bool IsGerman => _localizer.Language == "de";

    public string StatusText
    {
        get
        {
            if (_document.LoadError is { } error) return _localizer.Format("Document_Error", error);
            if (!_document.HasDocument) return _localizer["Document_None"];
            var counts = _document.Store!.Counts;
            return _localizer.Format("Document_Loaded", counts.People, counts.Families);
        }
    }

    [RelayCommand]
    private void SelectPage(string index)
    {
        if (int.TryParse(index, out var i) && i >= 0 && i < Pages.Count)
            SelectedPage = Pages[i];
    }

    [RelayCommand]
    private void SetLanguage(string language)
    {
        _localizer.SetLanguage(language);
        _preferences.TrySave(_preferences.Load() with { Language = _localizer.Language });
    }
}
